using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.Metrics;
using Conductor.Types;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Conductor.Service.Database;

public sealed class ConnectionPoolManager : IConnectionPoolManager
{
    private readonly ConcurrentDictionary<string, ConnectionPool> pools = new();
    private readonly Timer cleanupTimer;
    private readonly ILogger logging = Log.ForContext<ConnectionPoolManager>();
    private readonly Meter _meter;
    private volatile bool disposed;
    private readonly Gauge<int> activeConnectionsGauge;
    private readonly Gauge<int> poolSizeGauge;

    public ConnectionPoolManager()
    {
        _meter = new Meter("Conductor.ConnectionPool", "1.0.0");

        activeConnectionsGauge = _meter.CreateGauge<int>(
            "conductor_connections_active",
            description: "Current number of active database connections");

        poolSizeGauge = _meter.CreateGauge<int>(
            "conductor_connection_pool_size",
            description: "Current connection pool size");

        cleanupTimer = new Timer(async _ => await CleanupIdleConnectionsAsync(),
            null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public DbConnection GetConnection(string connectionString, string dbType)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ConnectionPoolManager));

        var key = GeneratePoolKey(connectionString, dbType);
        var pool = pools.GetOrAdd(key, _ => new ConnectionPool(connectionString, dbType, logging));

        return pool.GetConnection();
    }

    public async Task<DbConnection> GetConnectionAsync(string connectionString, string dbType, CancellationToken cancellationToken = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ConnectionPoolManager));

        var key = GeneratePoolKey(connectionString, dbType);
        var pool = pools.GetOrAdd(key, _ => new ConnectionPool(connectionString, dbType, logging));

        return await pool.GetConnectionAsync(cancellationToken);
    }

    public void ReturnConnection(string connectionKey, DbConnection connection)
    {
        if (disposed || connection == null) return;

        if (pools.TryGetValue(connectionKey, out var pool))
        {
            pool.ReturnConnection(connection);
        }
        else
        {
            try { connection.Dispose(); } catch { }
        }
    }

    public ConnectionPoolStats GetPoolStats()
    {
        var totalActive = 0;
        var totalPoolSize = 0;
        var poolDetails = new List<PoolDetail>();

        foreach (var kvp in pools)
        {
            var stats = kvp.Value.GetStats();
            totalActive += stats.ActiveConnections;
            totalPoolSize += stats.PoolSize;

            poolDetails.Add(new PoolDetail
            {
                PoolKey = kvp.Key,
                ActiveConnections = stats.ActiveConnections,
                PoolSize = stats.PoolSize,
                IdleConnections = stats.IdleConnections,
                CreatedConnections = stats.CreatedConnections,
                LastActivity = stats.LastActivity
            });
        }

        activeConnectionsGauge.Record(totalActive);
        poolSizeGauge.Record(totalPoolSize);

        return new ConnectionPoolStats
        {
            TotalActivePools = pools.Count,
            TotalActiveConnections = totalActive,
            TotalPoolSize = totalPoolSize,
            PoolDetails = poolDetails
        };
    }

    public async Task CleanupIdleConnectionsAsync()
    {
        if (disposed) return;

        logging.Debug("Starting connection pool cleanup");
        var cleanupTasks = new List<Task>();

        foreach (var pool in pools.Values)
        {
            cleanupTasks.Add(pool.CleanupIdleConnectionsAsync());
        }

        try
        {
            await Task.WhenAll(cleanupTasks);
            logging.Debug("Connection pool cleanup completed");
        }
        catch (Exception ex)
        {
            logging.Error(ex, "Error during connection pool cleanup");
        }
    }

    private static string GeneratePoolKey(string connectionString, string dbType) =>
        $"{dbType}:{connectionString.GetHashCode()}";

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        cleanupTimer?.Dispose();

        var disposeTasks = pools.Values.Select(pool => pool.DisposeAsync().AsTask());
        await Task.WhenAll(disposeTasks);

        pools.Clear();
        _meter?.Dispose();

        logging.Information("Connection pool manager disposed");
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(30));
        GC.SuppressFinalize(this);
    }
}

public sealed class ConnectionPool : IAsyncDisposable
{
    private readonly ConcurrentQueue<PooledConnection> availableConnections = new();
    private readonly ConcurrentDictionary<DbConnection, PooledConnection> activeConnections = new();
    private readonly SemaphoreSlim semaphore;
    private readonly string connectionString;
    private readonly string dbType;
    private readonly ILogger logging;
    private readonly Lock statsLock = new();
    private const int MaxPoolSize = 50;
    private const int MinPoolSize = 2;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private int createdConnections;
    private DateTimeOffset lastActivity = DateTimeOffset.UtcNow;

    public ConnectionPool(string conStr, string type, ILogger logger)
    {
        connectionString = conStr;
        dbType = type;
        logging = logger.ForContext("PoolKey", $"{dbType}:{connectionString.GetHashCode()}");
        semaphore = new SemaphoreSlim(MaxPoolSize, MaxPoolSize);
        
        _ = Task.Run(PrewarmPoolAsync);
    }

    public DbConnection GetConnection()
    {
        return GetConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<DbConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(ConnectionTimeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await semaphore.WaitAsync(combinedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            logging.Warning("Connection acquisition timeout for {DbType}", dbType);
            throw new TimeoutException($"Failed to acquire connection within {ConnectionTimeout}");
        }

        try
        {
            lastActivity = DateTime.UtcNow;

            if (availableConnections.TryDequeue(out var pooledConnection))
            {
                if (IsConnectionValid(pooledConnection.Connection))
                {
                    activeConnections.TryAdd(pooledConnection.Connection, pooledConnection);
                    logging.Debug("Reused pooled connection");
                    return pooledConnection.Connection;
                }
                else
                {
                    await pooledConnection.DisposeAsync();
                }
            }

            var newConnection = await CreateNewConnectionAsync(combinedCts.Token);
            var newPooledConnection = new PooledConnection(newConnection, DateTime.UtcNow);
            
            activeConnections.TryAdd(newConnection, newPooledConnection);

            Interlocked.Add(ref createdConnections, 1);
            
            logging.Debug("Created new database connection");
            return newConnection;
        }
        catch
        {
            semaphore.Release();
            throw;
        }
    }

    public void ReturnConnection(DbConnection connection)
    {
        if (connection == null || !activeConnections.TryRemove(connection, out var pooledConnection))
        {
            semaphore.Release();
            return;
        }

        try
        {
            if (IsConnectionValid(connection) && availableConnections.Count < MaxPoolSize)
            {
                pooledConnection.LastUsed = DateTime.UtcNow;
                availableConnections.Enqueue(pooledConnection);
                logging.Debug("Returned connection to pool");
            }
            else
            {
                pooledConnection.DisposeAsync().AsTask().Wait(1000);
            }
        }
        catch (Exception ex)
        {
            logging.Warning(ex, "Error returning connection to pool");
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task CleanupIdleConnectionsAsync()
    {
        var cutoffTime = DateTime.UtcNow - IdleTimeout;
        var connectionsToRemove = new List<PooledConnection>();
        
        while (availableConnections.TryPeek(out var connection) && connection.LastUsed < cutoffTime)
        {
            if (availableConnections.TryDequeue(out var removedConnection))
            {
                connectionsToRemove.Add(removedConnection);
            }
        }

        var disposeTasks = connectionsToRemove.Select(conn => conn.DisposeAsync().AsTask());
        await Task.WhenAll(disposeTasks);
        
        if (connectionsToRemove.Count > 0)
        {
            logging.Information("Cleaned up {Count} idle connections", connectionsToRemove.Count);
        }
    }

    public PoolStats GetStats()
    {
        lock (statsLock)
        {
            return new PoolStats
            {
                ActiveConnections = activeConnections.Count,
                IdleConnections = availableConnections.Count,
                PoolSize = activeConnections.Count + availableConnections.Count,
                CreatedConnections = createdConnections,
                LastActivity = lastActivity
            };
        }
    }

    private async Task PrewarmPoolAsync()
    {
        try
        {
            for (int i = 0; i < MinPoolSize; i++)
            {
                var connection = await CreateNewConnectionAsync(CancellationToken.None);
                var pooledConnection = new PooledConnection(connection, DateTime.UtcNow);
                availableConnections.Enqueue(pooledConnection);
            }
            logging.Information("Pre-warmed connection pool with {Count} connections", MinPoolSize);
        }
        catch (Exception ex)
        {
            logging.Warning(ex, "Failed to pre-warm connection pool");
        }
    }

    private async Task<DbConnection> CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        var factory = DBExchangeFactory.Create(dbType);
        var connection = factory.CreateConnection(connectionString);
        
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static bool IsConnectionValid(DbConnection connection)
    {
        try
        {
            return connection?.State == ConnectionState.Open;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose all connections
        var allConnections = new List<PooledConnection>();
        
        while (availableConnections.TryDequeue(out var conn))
        {
            allConnections.Add(conn);
        }
        
        foreach (var activeConn in activeConnections.Values)
        {
            allConnections.Add(activeConn);
        }

        var disposeTasks = allConnections.Select(conn => conn.DisposeAsync().AsTask());
        await Task.WhenAll(disposeTasks);
        
        activeConnections.Clear();
        semaphore?.Dispose();
    }
}
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.Metrics;
using Conductor.Shared;
using Conductor.Types;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Conductor.Service.Database;

public sealed class ConnectionPoolManager : IConnectionPoolManager
{
    private readonly ConcurrentDictionary<string, ResilientConnectionPool> pools = new();
    private readonly Timer cleanupTimer;
    private readonly ILogger logging = Log.ForContext<ConnectionPoolManager>();
    private readonly Meter meter;
    private volatile bool disposed;
    private readonly Gauge<int> activeConnectionsGauge;
    private readonly Gauge<int> poolSizeGauge;
    private readonly Counter<int> connectionRetriesCounter;
    private readonly Counter<int> connectionFailuresCounter;

    public ConnectionPoolManager()
    {
        meter = new Meter("Conductor.ConnectionPool", "1.0.0");

        activeConnectionsGauge = meter.CreateGauge<int>(
            "conductor_connections_active",
            description: "Current number of active database connections");

        poolSizeGauge = meter.CreateGauge<int>(
            "conductor_connection_pool_size",
            description: "Current connection pool size");

        connectionRetriesCounter = meter.CreateCounter<int>(
            "conductor_connection_retries_total",
            description: "Total number of connection retry attempts");

        connectionFailuresCounter = meter.CreateCounter<int>(
            "conductor_connection_failures_total",
            description: "Total number of connection failures");

        cleanupTimer = new Timer(async _ => await CleanupIdleConnectionsAsync(),
            null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public DbConnection GetConnection(string connectionString, string dbType)
    {
        return GetConnectionAsync(connectionString, dbType, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<DbConnection> GetConnectionAsync(string connectionString, string dbType, CancellationToken cancellationToken = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ConnectionPoolManager));

        var key = GeneratePoolKey(connectionString, dbType);
        var pool = pools.GetOrAdd(key, _ => new ResilientConnectionPool(connectionString, dbType, logging));

        const int maxRetries = 3;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var connection = await pool.GetConnectionAsync(cancellationToken);
                
                if (await IsConnectionHealthy(connection))
                {
                    if (attempt > 1)
                    {
                        logging.Information("Successfully obtained connection on attempt {Attempt} for {DbType}", 
                            attempt, dbType);
                    }
                    return connection;
                }
                else
                {
                    logging.Warning("Connection health check failed on attempt {Attempt} for {DbType}", 
                        attempt, dbType);
                    
                    pool.ReturnConnection(connection, markAsUnhealthy: true);
                    lastException = new InvalidOperationException("Connection failed health check");
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                connectionRetriesCounter.Add(1, new KeyValuePair<string, object?>("dbType", dbType));
                
                logging.Warning(ex, "Connection attempt {Attempt}/{MaxRetries} failed for {DbType}: {Error}", 
                    attempt, maxRetries, dbType, ex.Message);

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 100));
                    await Task.Delay(delay, cancellationToken);
                    
                    await pool.CleanupUnhealthyConnectionsAsync();
                }
            }
        }

        connectionFailuresCounter.Add(1, new KeyValuePair<string, object?>("dbType", dbType));
        logging.Error(lastException, "Failed to obtain connection after {MaxRetries} attempts for {DbType}", 
            maxRetries, dbType);
        
        throw new InvalidOperationException($"Failed to obtain database connection for {dbType} after {maxRetries} attempts", lastException);
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
            logging.Warning("Attempting to return connection to non-existent pool {ConnectionKey}", connectionKey);
            try { connection.Dispose(); } catch { }
        }
    }

    private static async Task<bool> IsConnectionHealthy(DbConnection connection)
    {
        try
        {
            if (connection.State != ConnectionState.Open)
                return false;

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 5; 
            
            var result = await command.ExecuteScalarAsync();
            return result != null;
        }
        catch
        {
            return false;
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
        meter?.Dispose();

        logging.Information("Connection pool manager disposed");
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(30));
        GC.SuppressFinalize(this);
    }
}

public sealed class ResilientConnectionPool : IAsyncDisposable
{
    private readonly ConcurrentQueue<PooledConnectionWithHealth> availableConnections = new();
    private readonly ConcurrentDictionary<DbConnection, PooledConnectionWithHealth> activeConnections = new();
    private readonly SemaphoreSlim semaphore;
    private readonly string connectionString;
    private readonly string dbType;
    private readonly ILogger logging;
    private readonly Lock statsLock = new();
    private static readonly int MaxPoolSize = Settings.ConnectionPoolMaxSize;
    private static readonly int MinPoolSize = Settings.ConnectionPoolMinSize;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(Settings.ConnectionIdleTimeoutMinutes);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(Settings.ConnectionTimeout);
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(5);
    private int createdConnections;
    private DateTimeOffset lastActivity = DateTimeOffset.UtcNow;

    public ResilientConnectionPool(string conStr, string type, ILogger logger)
    {
        connectionString = conStr;
        dbType = type;
        logging = logger.ForContext("PoolKey", $"{dbType}:{connectionString.GetHashCode()}");
        semaphore = new SemaphoreSlim(MaxPoolSize, MaxPoolSize);

        _ = Task.Run(PrewarmPoolAsync);
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

            while (availableConnections.TryDequeue(out var pooledConnection))
            {
                if (await IsConnectionStillHealthy(pooledConnection))
                {
                    activeConnections.TryAdd(pooledConnection.Connection, pooledConnection);
                    logging.Debug("Reused healthy pooled connection");
                    return pooledConnection.Connection;
                }
                else
                {
                    logging.Debug("Disposing unhealthy pooled connection");
                    await pooledConnection.DisposeAsync();
                }
            }

            var newConnection = await CreateNewConnectionAsync(combinedCts.Token);
            var newPooledConnection = new PooledConnectionWithHealth(newConnection, DateTime.UtcNow);

            activeConnections.TryAdd(newConnection, newPooledConnection);
            Interlocked.Increment(ref createdConnections);

            logging.Debug("Created new database connection");
            return newConnection;
        }
        catch
        {
            semaphore.Release();
            throw;
        }
    }

    public void ReturnConnection(DbConnection connection, bool markAsUnhealthy = false)
    {
        if (connection == null || !activeConnections.TryRemove(connection, out var pooledConnection))
        {
            semaphore.Release();
            return;
        }

        try
        {
            if (!markAsUnhealthy && IsConnectionValidForReuse(connection) && availableConnections.Count < MaxPoolSize)
            {
                pooledConnection.LastUsed = DateTime.UtcNow;
                pooledConnection.LastHealthCheck = DateTime.UtcNow;
                availableConnections.Enqueue(pooledConnection);
                logging.Debug("Returned healthy connection to pool");
            }
            else
            {
                if (markAsUnhealthy)
                {
                    logging.Debug("Disposing connection marked as unhealthy");
                }
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

    public async Task CleanupUnhealthyConnectionsAsync()
    {
        var connectionsToRemove = new List<PooledConnectionWithHealth>();
        var tempQueue = new Queue<PooledConnectionWithHealth>();

        while (availableConnections.TryDequeue(out var connection))
        {
            if (await IsConnectionStillHealthy(connection))
            {
                tempQueue.Enqueue(connection);
            }
            else
            {
                connectionsToRemove.Add(connection);
            }
        }

        while (tempQueue.TryDequeue(out var healthyConnection))
        {
            availableConnections.Enqueue(healthyConnection);
        }

        var disposeTasks = connectionsToRemove.Select(conn => conn.DisposeAsync().AsTask());
        await Task.WhenAll(disposeTasks);

        if (connectionsToRemove.Count > 0)
        {
            logging.Information("Cleaned up {Count} unhealthy connections", connectionsToRemove.Count);
        }
    }

    public async Task CleanupIdleConnectionsAsync()
    {
        await CleanupUnhealthyConnectionsAsync();

        var cutoffTime = DateTime.UtcNow - IdleTimeout;
        var connectionsToRemove = new List<PooledConnectionWithHealth>();
        var tempQueue = new Queue<PooledConnectionWithHealth>();

        while (availableConnections.TryDequeue(out var connection))
        {
            if (connection.LastUsed < cutoffTime)
            {
                connectionsToRemove.Add(connection);
            }
            else
            {
                tempQueue.Enqueue(connection);
            }
        }

        while (tempQueue.TryDequeue(out var activeConnection))
        {
            availableConnections.Enqueue(activeConnection);
        }

        var disposeTasks = connectionsToRemove.Select(conn => conn.DisposeAsync().AsTask());
        await Task.WhenAll(disposeTasks);

        if (connectionsToRemove.Count > 0)
        {
            logging.Information("Cleaned up {Count} idle connections", connectionsToRemove.Count);
        }
    }

    private static async Task<bool> IsConnectionStillHealthy(PooledConnectionWithHealth pooledConnection)
    {
        try
        {
            var connection = pooledConnection.Connection;
            
            if (DateTime.UtcNow - pooledConnection.LastHealthCheck < HealthCheckInterval)
            {
                return IsConnectionValidForReuse(connection);
            }

            if (connection.State != ConnectionState.Open)
                return false;

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 2;
            
            var result = await command.ExecuteScalarAsync();
            var isHealthy = result != null;
            
            if (isHealthy)
            {
                pooledConnection.LastHealthCheck = DateTime.UtcNow;
            }
            
            return isHealthy;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsConnectionValidForReuse(DbConnection connection)
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
                var pooledConnection = new PooledConnectionWithHealth(connection, DateTime.UtcNow);
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

    public async ValueTask DisposeAsync()
    {
        var allConnections = new List<PooledConnectionWithHealth>();

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

public sealed class PooledConnectionWithHealth(DbConnection connection, DateTimeOffset lastUse) : IAsyncDisposable, IDisposable
{
    private readonly DbConnection connection = connection ?? throw new ArgumentNullException(nameof(connection));
    private volatile bool disposed;
    
    public DateTimeOffset LastUsed { get; set; } = lastUse;
    public DateTimeOffset LastHealthCheck { get; set; } = lastUse;
    public DbConnection Connection => disposed ? throw new ObjectDisposedException(nameof(PooledConnectionWithHealth)) : connection;

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            if (connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            if (connection.State != ConnectionState.Closed)
            {
                connection.Close();
            }
            connection.Dispose();
        }
        catch { }
    }
}
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

    // FIXED: Add pre-warming coordination
    private readonly SemaphoreSlim prewarmSemaphore = new(3, 3); // Limit concurrent pre-warming

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
        var pool = pools.GetOrAdd(key, _ =>
        {
            var newPool = new ResilientConnectionPool(connectionString, dbType, logging);

            Task.Run(async () =>
            {
                var acquired = await prewarmSemaphore.WaitAsync(30000, CancellationToken.None);
                if (acquired)
                {
                    try
                    {
                        await newPool.PrewarmAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logging.Debug("Pre-warm task completed for pool {PoolKey}: {Result}", key, ex.Message);
                    }
                    finally
                    {
                        prewarmSemaphore.Release();
                    }
                }
                else
                {
                    logging.Debug("Pre-warm semaphore timeout for pool {PoolKey}", key);
                }
            });

            return newPool;
        });

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
        if (disposed || connection is null) return;

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
            return result is not null;
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
        prewarmSemaphore?.Dispose();

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
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(Settings.ConnectionIdleTimeoutMinutes);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(Math.Min(Settings.ConnectionTimeout, 30)); // Cap at 30s
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim GlobalPrewarmSemaphore = new(Environment.ProcessorCount, Environment.ProcessorCount);
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> PrewarmTasks = new();

    private int createdConnections;
    private DateTimeOffset lastActivity = DateTimeOffset.UtcNow;
    private volatile bool disposed;

    public ResilientConnectionPool(string conStr, string type, ILogger logger)
    {
        connectionString = conStr;
        dbType = type;
        logging = logger.ForContext("PoolKey", $"{dbType}:{connectionString.GetHashCode()}");
        semaphore = new SemaphoreSlim(Settings.ConnectionPoolMaxSize, Settings.ConnectionPoolMaxSize);

        // FIXED: Don't automatically start pre-warming in constructor
        // Let the ConnectionPoolManager coordinate this
    }

    // FIXED: Add controlled pre-warming method
    public async Task PrewarmAsync(CancellationToken cancellationToken = default)
    {
        var poolKey = $"{dbType}:{connectionString.GetHashCode()}";

        // Check if this pool is already being pre-warmed
        var tcs = PrewarmTasks.GetOrAdd(poolKey, _ => new TaskCompletionSource<bool>());

        // If another thread is already pre-warming this pool, wait for it
        if (tcs.Task.IsCompleted)
        {
            return; // Already pre-warmed
        }

        // Try to acquire global pre-warm semaphore to limit concurrent pre-warming
        var acquired = await GlobalPrewarmSemaphore.WaitAsync(5000, cancellationToken);
        if (!acquired)
        {
            logging.Information("Pre-warm semaphore timeout for pool {PoolKey}, skipping pre-warm", poolKey);
            tcs.TrySetResult(false);
            return;
        }

        try
        {
            await PrewarmPoolAsync(cancellationToken);
            tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            // FIXED: Handle all exceptions gracefully without warnings
            logging.Information("Pre-warm skipped for pool {PoolKey}: {Reason}", poolKey, ex.Message);
            tcs.TrySetResult(false); // Mark as completed but not successful
        }
        finally
        {
            GlobalPrewarmSemaphore.Release();
        }
    }

    private async Task PrewarmPoolAsync(CancellationToken cancellationToken = default)
    {
        if (disposed) return;

        var successfulConnections = 0;
        var maxAttempts = Settings.ConnectionPoolMinSize * 2; // Allow some failures
        var connectionTimeouts = 0;
        var maxTimeouts = 3; // Stop after too many timeouts

        logging.Debug("Starting pre-warm for connection pool (target: {Settings.ConnectionPoolMinSize} connections)", Settings.ConnectionPoolMinSize);

        for (int attempt = 0; attempt < maxAttempts && successfulConnections < Settings.ConnectionPoolMinSize && connectionTimeouts < maxTimeouts && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                // FIXED: Add progressive delay and shorter timeouts for pre-warming
                if (attempt > 0)
                {
                    var delay = Math.Min(200 * attempt, 2000); // Max 2 second delay
                    await Task.Delay(delay, cancellationToken);
                }

                // FIXED: Use shorter timeout for pre-warming connections
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // Shorter timeout for pre-warm
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var connection = await CreateNewConnectionAsync(combinedCts.Token);
                var pooledConnection = new PooledConnectionWithHealth(connection, DateTime.UtcNow);

                availableConnections.Enqueue(pooledConnection);
                successfulConnections++;

                logging.Debug("Pre-warmed connection {SuccessfulConnections}/{Settings.ConnectionPoolMinSize}", successfulConnections, Settings.ConnectionPoolMinSize);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logging.Debug("Pre-warm cancelled for connection pool");
                break;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Timeout expired") || ex.Message.Contains("pool"))
            {
                // FIXED: Handle connection pool timeout gracefully
                connectionTimeouts++;
                logging.Debug("Connection pool timeout during pre-warm attempt {Attempt} (timeout {TimeoutCount}/{MaxTimeouts}): {Message}",
                    attempt + 1, connectionTimeouts, maxTimeouts, ex.Message);

                if (connectionTimeouts >= maxTimeouts)
                {
                    logging.Information("Too many connection timeouts during pre-warm, stopping early. Successfully created {SuccessfulConnections} connections",
                        successfulConnections);
                    break;
                }
            }
            catch (TimeoutException ex)
            {
                // FIXED: Handle timeout exceptions specifically
                connectionTimeouts++;
                logging.Debug("Timeout during pre-warm attempt {Attempt} (timeout {TimeoutCount}/{MaxTimeouts}): {Message}",
                    attempt + 1, connectionTimeouts, maxTimeouts, ex.Message);

                if (connectionTimeouts >= maxTimeouts)
                {
                    logging.Information("Too many timeouts during pre-warm, stopping early. Successfully created {SuccessfulConnections} connections",
                        successfulConnections);
                    break;
                }
            }
            catch (Exception ex)
            {
                // FIXED: Handle any other exceptions gracefully
                logging.Debug("Pre-warm connection attempt {Attempt}/{MaxAttempts} failed: {Error}",
                    attempt + 1, maxAttempts, ex.GetType().Name);

                // Don't log full exception details for common connection issues during pre-warm
                if (attempt >= maxAttempts - 1)
                {
                    logging.Information("Pre-warm completed with some failures. Successfully created {SuccessfulConnections}/{TargetConnections} connections",
                        successfulConnections, Settings.ConnectionPoolMinSize);
                    break;
                }
            }
        }

        if (successfulConnections > 0)
        {
            logging.Information("Pre-warmed connection pool with {SuccessfulConnections}/{TargetConnections} connections",
                successfulConnections, Settings.ConnectionPoolMinSize);
        }
        else
        {
            logging.Information("Pre-warm completed with no successful connections - pool will create connections on demand");
        }
    }

    public async Task<DbConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ResilientConnectionPool));

        // FIXED: Use shorter timeout with progressive retry
        var baseTimeout = TimeSpan.FromSeconds(10);
        var maxRetries = 3;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            using var timeoutCts = new CancellationTokenSource(baseTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await semaphore.WaitAsync(combinedCts.Token);
                break; // Successfully acquired semaphore
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Timeout occurred, not user cancellation
                if (retry < maxRetries - 1)
                {
                    logging.Warning("Connection acquisition timeout on attempt {Retry}/{MaxRetries}, retrying...",
                        retry + 1, maxRetries);

                    // FIXED: Try to clean up unhealthy connections before retry
                    _ = Task.Run(() => CleanupUnhealthyConnectionsAsync(), CancellationToken.None);

                    // Progressive delay
                    await Task.Delay(1000 * (retry + 1), cancellationToken);
                    continue;
                }

                logging.Error("Failed to acquire connection after {MaxRetries} attempts for {DbType}", maxRetries, dbType);
                throw new TimeoutException($"Failed to acquire connection within {baseTimeout.TotalSeconds * maxRetries}s total time");
            }
        }

        try
        {
            lastActivity = DateTime.UtcNow;

            // Try to reuse existing connection
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

            // Create new connection if none available
            var newConnection = await CreateNewConnectionAsync(cancellationToken);
            var newPooledConnection = new PooledConnectionWithHealth(newConnection, DateTime.UtcNow);

            activeConnections.TryAdd(newConnection, newPooledConnection);
            Interlocked.Increment(ref createdConnections);

            logging.Debug("Created new database connection (total created: {CreatedConnections})", createdConnections);
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
        if (connection is null || !activeConnections.TryRemove(connection, out var pooledConnection))
        {
            semaphore.Release();
            return;
        }

        try
        {
            if (!markAsUnhealthy && IsConnectionValidForReuse(connection) && availableConnections.Count < Settings.ConnectionPoolMaxSize)
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
            var isHealthy = result is not null;

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

    // FIXED: Enhanced connection creation with better error handling
    private async Task<DbConnection> CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 2; // Reduced retries for pre-warming
        Exception? lastException = null;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var factory = DBExchangeFactory.Create(dbType);
                var connection = factory.CreateConnection(connectionString);

                // FIXED: Even shorter timeout for pre-warming to avoid blocking
                var timeoutSeconds = attempt == 0 ? 5 : 10; // First attempt is fast, second is slightly longer
                using var connectionCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionCts.Token);

                await connection.OpenAsync(combinedCts.Token);

                // Verify connection is actually usable
                if (connection.State != ConnectionState.Open)
                {
                    connection.Dispose();
                    throw new InvalidOperationException("Connection failed to open properly");
                }

                return connection;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Don't retry on user cancellation
            }
            catch (Exception ex)
            {
                lastException = ex;

                // Don't log warnings for every failed pre-warm attempt - this is normal
                if (attempt == maxRetries - 1)
                {
                    // Only log on final attempt
                    logging.Debug("All pre-warm connection attempts failed: {Error}", ex.Message);
                }

                if (attempt < maxRetries - 1)
                {
                    // Very short delay between pre-warm retries
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        throw new InvalidOperationException($"Failed to create pre-warm connection after {maxRetries} attempts", lastException);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

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
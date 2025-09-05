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
    private readonly SemaphoreSlim prewarmSemaphore = new(3, 3);

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
        try
        {
            return GetConnectionAsync(connectionString, dbType, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logging.Error(ex, "Synchronous connection acquisition failed for {DbType}", dbType);
            throw;
        }
    }

    public async Task<DbConnection> GetConnectionAsync(string connectionString, string dbType, CancellationToken cancellationToken = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ConnectionPoolManager));

        var key = GeneratePoolKey(connectionString, dbType);
        var pool = pools.GetOrAdd(key, _ =>
        {
            var newPool = new ResilientConnectionPool(connectionString, dbType, logging);

            // Start pre-warming in background with coordination
            Task.Run(async () =>
            {
                var acquired = await prewarmSemaphore.WaitAsync(30000, CancellationToken.None);
                if (acquired)
                {
                    try
                    {
                        await newPool.PrewarmAsync(CancellationToken.None);
                        logging.Debug("Pre-warm completed successfully for pool {PoolKey}", key);
                    }
                    catch (Exception ex)
                    {
                        logging.Debug("Pre-warm finished for pool {PoolKey}: {Result}", key, ex.Message);
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

                    // Try to cleanup unhealthy connections before retry
                    _ = Task.Run(() => pool.CleanupUnhealthyConnectionsAsync(), CancellationToken.None);
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            var result = await command.ExecuteScalarAsync(cts.Token);
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
    private SemaphoreSlim semaphore;
    private readonly string connectionString;
    private readonly string dbType;
    private readonly ILogger logging;
    private readonly Lock statsLock = new();
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(Settings.ConnectionIdleTimeoutMinutes);
    private readonly TimeSpan connectionTimeout;
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim GlobalPrewarmSemaphore = new(Environment.ProcessorCount, Environment.ProcessorCount);
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> PrewarmTasks = new();

    private int createdConnections;
    private DateTimeOffset lastActivity = DateTimeOffset.UtcNow;
    private volatile bool disposed;
    private readonly bool isMySql;

    public ResilientConnectionPool(string conStr, string type, ILogger logger)
    {
        connectionString = conStr;
        dbType = type;
        isMySql = dbType.ToLower().Contains("mysql");
        logging = logger.ForContext("PoolKey", $"{dbType}:{connectionString.GetHashCode()}");
        
        // MySQL-specific timeout handling
        connectionTimeout = isMySql ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(30);
        
        semaphore = new SemaphoreSlim(Settings.ConnectionPoolMaxSize, Settings.ConnectionPoolMaxSize);
    }

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
            logging.Information("Pre-warm skipped for pool {PoolKey}: {Reason}", poolKey, ex.Message);
            tcs.TrySetResult(false);
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
        var maxAttempts = Settings.ConnectionPoolMinSize * 2;
        var connectionTimeouts = 0;
        var maxTimeouts = isMySql ? 2 : 3;

        logging.Debug("Starting pre-warm for connection pool (target: {MinSize} connections)", Settings.ConnectionPoolMinSize);

        for (int attempt = 0; attempt < maxAttempts && successfulConnections < Settings.ConnectionPoolMinSize && connectionTimeouts < maxTimeouts && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = isMySql ? Math.Min(500 * attempt, 3000) : Math.Min(200 * attempt, 2000);
                    await Task.Delay(delay, cancellationToken);
                }

                var connection = await CreateNewConnectionAsync(cancellationToken);
                var pooledConnection = new PooledConnectionWithHealth(connection, DateTime.UtcNow);

                availableConnections.Enqueue(pooledConnection);
                successfulConnections++;

                logging.Debug("Pre-warmed connection {SuccessfulConnections}/{MinSize}", successfulConnections, Settings.ConnectionPoolMinSize);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logging.Debug("Pre-warm cancelled for connection pool");
                break;
            }
            catch (Exception ex) when (IsTimeoutException(ex))
            {
                connectionTimeouts++;
                logging.Debug("Connection timeout during pre-warm attempt {Attempt} (timeout {TimeoutCount}/{MaxTimeouts}): {Message}",
                    attempt + 1, connectionTimeouts, maxTimeouts, ex.Message);

                if (connectionTimeouts >= maxTimeouts)
                {
                    logging.Information("Too many connection timeouts during pre-warm, stopping early. Successfully created {SuccessfulConnections} connections",
                        successfulConnections);
                    break;
                }
            }
            catch (Exception ex)
            {
                logging.Debug("Pre-warm connection attempt {Attempt}/{MaxAttempts} failed: {Error}",
                    attempt + 1, maxAttempts, ex.GetType().Name);

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

        var baseTimeout = isMySql ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(10);
        var maxRetries = 3;
        bool semaphoreAcquired = false;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(baseTimeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                await semaphore.WaitAsync(combinedCts.Token);
                semaphoreAcquired = true;
                break; // Successfully acquired semaphore
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (retry < maxRetries - 1)
                {
                    logging.Warning("Connection acquisition timeout on attempt {Retry}/{MaxRetries}, retrying...",
                        retry + 1, maxRetries);

                    // Log current pool status for debugging
                    LogPoolStatus();

                    // Try to clean up unhealthy connections before retry
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
                    semaphoreAcquired = false; // Connection successfully obtained
                    return pooledConnection.Connection;
                }
                else
                {
                    logging.Debug("Disposing unhealthy pooled connection");
                    await SafeDisposeConnectionAsync(pooledConnection);
                }
            }

            // Create new connection
            var newConnection = await CreateNewConnectionAsync(cancellationToken);
            var newPooledConnection = new PooledConnectionWithHealth(newConnection, DateTime.UtcNow);

            activeConnections.TryAdd(newConnection, newPooledConnection);
            Interlocked.Increment(ref createdConnections);

            logging.Debug("Created new database connection (total created: {CreatedConnections})", createdConnections);
            semaphoreAcquired = false; // Connection successfully obtained
            return newConnection;
        }
        catch (Exception ex)
        {
            logging.Error(ex, "Failed to get connection for {DbType}", dbType);
            throw;
        }
        finally
        {
            // CRITICAL: Always release semaphore if still acquired
            if (semaphoreAcquired)
            {
                semaphore.Release();
                logging.Debug("Released semaphore due to exception");
            }
        }
    }

    public void ReturnConnection(DbConnection connection, bool markAsUnhealthy = false)
    {
        if (connection is null) 
        {
            semaphore.Release();
            return;
        }

        if (!activeConnections.TryRemove(connection, out var pooledConnection))
        {
            // Connection not tracked - dispose and release semaphore
            try { connection.Dispose(); } catch { }
            semaphore.Release();
            logging.Debug("Returned untracked connection");
            return;
        }

        try
        {
            if (!markAsUnhealthy && 
                !disposed && 
                IsConnectionValidForReuse(connection) && 
                availableConnections.Count < Settings.ConnectionPoolMaxSize)
            {
                pooledConnection.LastUsed = DateTime.UtcNow;
                pooledConnection.LastHealthCheck = DateTime.UtcNow;
                availableConnections.Enqueue(pooledConnection);
                logging.Debug("Returned healthy connection to pool");
            }
            else
            {
                logging.Debug("Disposing connection (unhealthy: {Unhealthy}, disposed: {Disposed})", 
                    markAsUnhealthy, disposed);
                
                // Safe disposal with timeout
                _ = Task.Run(async () => await SafeDisposeConnectionAsync(pooledConnection));
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

        var disposeTasks = connectionsToRemove.Select(conn => SafeDisposeConnectionAsync(conn));
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

        var disposeTasks = connectionsToRemove.Select(conn => SafeDisposeConnectionAsync(conn));
        await Task.WhenAll(disposeTasks);

        if (connectionsToRemove.Count > 0)
        {
            logging.Information("Cleaned up {Count} idle connections", connectionsToRemove.Count);
        }
    }

    public void LogPoolStatus()
    {
        var stats = GetStats();
        logging.Information("Pool Status - Active: {Active}, Idle: {Idle}, Semaphore: {Available}/{Max}, DbType: {DbType}", 
            stats.ActiveConnections, 
            stats.IdleConnections, 
            semaphore.CurrentCount, 
            Settings.ConnectionPoolMaxSize,
            dbType);
    }

    public async Task EmergencyResetAsync()
    {
        logging.Warning("Performing emergency pool reset for {DbType}", dbType);
        
        // Force dispose all connections
        var allConnections = new List<PooledConnectionWithHealth>();
        
        while (availableConnections.TryDequeue(out var conn))
            allConnections.Add(conn);
        
        foreach (var activeConn in activeConnections.Values)
            allConnections.Add(activeConn);
        
        activeConnections.Clear();
        
        // Dispose connections with timeout
        var disposeTasks = allConnections.Select(conn => SafeDisposeConnectionAsync(conn));
        await Task.WhenAll(disposeTasks);
        
        // Reset semaphore
        var oldSemaphore = semaphore;
        semaphore = new SemaphoreSlim(Settings.ConnectionPoolMaxSize, Settings.ConnectionPoolMaxSize);
        oldSemaphore?.Dispose();
        
        logging.Information("Emergency pool reset completed for {DbType}", dbType);
    }

    private async Task<bool> IsConnectionStillHealthy(PooledConnectionWithHealth pooledConnection)
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
            command.CommandTimeout = isMySql ? 5 : 2;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            var result = await command.ExecuteScalarAsync(cts.Token);
            var isHealthy = result is not null;

            if (isHealthy)
            {
                pooledConnection.LastHealthCheck = DateTime.UtcNow;
            }

            return isHealthy;
        }
        catch (Exception ex)
        {
            if (isMySql && (ex.Message.Contains("MySQL") || ex.Message.Contains("timeout")))
            {
                logging.Debug("MySQL connection health check failed: {Error}", ex.Message);
            }
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

    private static bool IsTimeoutException(Exception ex)
    {
        return ex is TimeoutException ||
               ex is OperationCanceledException ||
               (ex is InvalidOperationException && (ex.Message.Contains("Timeout expired") || ex.Message.Contains("timeout"))) ||
               ex.Message.Contains("pool");
    }

    private async Task SafeDisposeConnectionAsync(PooledConnectionWithHealth pooledConnection)
    {
        try
        {
            var disposeTask = pooledConnection.DisposeAsync().AsTask();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            logging.Debug("Connection disposal failed: {Error}", ex.Message);
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

    private async Task<DbConnection> CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 3; // Increased for MySqlConnector
        Exception? lastException = null;
        var connectionStringVariants = GetMySQLConnectionStringVariants(connectionString);

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            DbConnection? connection = null;
            try
            {
                var factory = DBExchangeFactory.Create(dbType);
                
                // Try different connection string variants for MySQL SSL issues
                var effectiveConnectionString = isMySql && attempt < connectionStringVariants.Count 
                    ? connectionStringVariants[attempt] 
                    : connectionString;
                
                if (attempt > 0 && isMySql)
                {
                    logging.Information("MySQL connection attempt {Attempt} using variant: {Variant}", 
                        attempt + 1, SanitizeConnectionString(effectiveConnectionString));
                }
                
                connection = factory.CreateConnection(effectiveConnectionString);

                // MySQL-specific timeout handling with MySqlConnector
                var openTimeout = isMySql ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(15);
                
                using var connectionCts = new CancellationTokenSource(openTimeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, connectionCts.Token);

                await connection.OpenAsync(combinedCts.Token);

                // Verify connection is actually usable
                if (connection.State != ConnectionState.Open)
                {
                    throw new InvalidOperationException("Connection failed to open properly");
                }

                // MySQL-specific connection test
                if (isMySql)
                {
                    using var testCmd = connection.CreateCommand();
                    testCmd.CommandText = "SELECT 1";
                    testCmd.CommandTimeout = 10;
                    
                    using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var testCombinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, testCts.Token);
                    
                    await testCmd.ExecuteScalarAsync(testCombinedCts.Token);
                }

                if (attempt > 0)
                {
                    logging.Information("MySQL connection succeeded on attempt {Attempt} using: {Variant}", 
                        attempt + 1, SanitizeConnectionString(effectiveConnectionString));
                }

                return connection;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                connection?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                connection?.Dispose();
                lastException = ex;
                
                if (attempt == maxRetries - 1)
                {
                    logging.Error("Connection creation failed for {DbType} after all attempts: {Error}", dbType, ex.Message);
                    
                    // Provide specific guidance for persistent SSL issues
                    if (isMySql && IsSSLAuthenticationError(ex))
                    {
                        logging.Error("Persistent MySQL SSL issues detected. MySqlConnector should handle this better than MySql.Data. " +
                                    "This may indicate server-side SSL configuration problems. " +
                                    "Contact database administrator or check network connectivity.");
                    }
                }
                else if (isMySql)
                {
                    logging.Warning("MySQL connection attempt {Attempt}/{MaxRetries} failed: {Error}. Trying next variant...", 
                        attempt + 1, maxRetries, ex.Message);
                }

                if (attempt < maxRetries - 1)
                {
                    var delay = isMySql ? 1500 : 500;
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        throw new InvalidOperationException($"Failed to create connection for {dbType} after {maxRetries} attempts. " +
            $"Last error: {lastException?.Message}", lastException);
    }

    private List<string> GetMySQLConnectionStringVariants(string originalConnectionString)
    {
        if (!isMySql) return new List<string> { originalConnectionString };

        var variants = new List<string>();
        
        // Original connection string first
        variants.Add(originalConnectionString);
        
        // Variant 1: MySqlConnector with SSL disabled
        variants.Add(EnsureMySqlConnectorSSLSettings(originalConnectionString, "None"));
        
        // Variant 2: MySqlConnector with SSL required but certificate validation disabled
        variants.Add(EnsureMySqlConnectorSSLSettings(originalConnectionString, "Required"));
        
        return variants;
    }

    private static string EnsureMySqlConnectorSSLSettings(string connectionString, string sslMode)
    {
        var builder = new System.Collections.Generic.Dictionary<string, string>();
        
        // Parse existing connection string
        foreach (var pair in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                builder[parts[0].Trim().ToLower()] = parts[1].Trim();
            }
        }
        
        // Set MySqlConnector-specific SSL settings
        if (sslMode == "None")
        {
            builder["sslmode"] = "None";
            builder["allowpublickeyretrieval"] = "true";
        }
        else if (sslMode == "Required")
        {
            builder["sslmode"] = "Required";
            builder["allowpublickeyretrieval"] = "true";
            builder["trustservercertificate"] = "true"; // MySqlConnector equivalent
        }
        
        // Remove any conflicting SSL settings
        builder.Remove("ssl");
        builder.Remove("ssl mode");
        
        // Rebuild connection string
        var result = string.Join(";", builder.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return result;
    }

    private static bool IsSSLAuthenticationError(Exception? ex)
    {
        if (ex == null) return false;
        
        return ex is System.Security.Authentication.AuthenticationException ||
               ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("frame size", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("corrupted frame", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeConnectionString(string connectionString)
    {
        // Remove sensitive information for logging
        return System.Text.RegularExpressions.Regex.Replace(connectionString, 
            @"(Password|Pwd)\s*=\s*[^;]*", "Password=***", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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

        var disposeTasks = allConnections.Select(conn => SafeDisposeConnectionAsync(conn));
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
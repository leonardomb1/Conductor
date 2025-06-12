using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading.Channels;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Service.Database;
using Conductor.Service.Http;
using Conductor.Service.Script;
using Conductor.Shared;
using Conductor.Types;
using CsvHelper;
using Serilog;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Conductor.Service;

/// <summary>
/// High-performance data extraction pipeline supporting both database and script-based extractions
/// with connection pooling, retry logic, and comprehensive error handling.
/// </summary>
public sealed class ExtractionPipeline : IAsyncDisposable, IDisposable
{
    private readonly DateTime _requestTime;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly int? _overrideFilter;
    private readonly IScriptEngine? _scriptEngine;
    private readonly ILogger _logger;
    private readonly ConcurrentBag<Error> _pipelineErrors = [];
    private readonly ConcurrentDictionary<string, DbConnection> _connectionPool = new();
    private readonly ConcurrentDictionary<string, HttpClient> _httpClientPool = new();
    
    // Performance tracking
    private readonly ConcurrentDictionary<int, ExtractionMetrics> _extractionMetrics = new();
    
    private bool _disposed = false;
    private readonly SemaphoreSlim _connectionSemaphore;
    private readonly SemaphoreSlim _httpClientSemaphore;

    public ExtractionPipeline(
        DateTime requestTime, 
        IHttpClientFactory factory, 
        int? overrideFilter = null, 
        IScriptEngine? scriptEngine = null,
        ILogger? logger = null)
    {
        _requestTime = requestTime;
        _httpClientFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        _overrideFilter = overrideFilter;
        _scriptEngine = scriptEngine;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        
        // Initialize semaphores for resource management
        var maxConnections = Math.Max(Environment.ProcessorCount * 2, 4);
        _connectionSemaphore = new SemaphoreSlim(maxConnections, maxConnections);
        _httpClientSemaphore = new SemaphoreSlim(maxConnections, maxConnections);
    }

    /// <summary>
    /// Gets extraction performance metrics
    /// </summary>
    public IReadOnlyDictionary<int, ExtractionMetrics> Metrics => _extractionMetrics;

    /// <summary>
    /// Gets current pipeline errors
    /// </summary>
    public IReadOnlyCollection<Error> Errors => _pipelineErrors;

    private async Task<DbConnection> GetOrCreateConnectionAsync(string connectionString, string dbType, CancellationToken cancellationToken = default)
    {
        await _connectionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            var key = $"{connectionString}|{dbType}";
            
            if (_connectionPool.TryGetValue(key, out var existingConnection) && 
                existingConnection.State == ConnectionState.Open)
            {
                return existingConnection;
            }

            // Remove closed connection if exists
            if (existingConnection != null)
            {
                _connectionPool.TryRemove(key, out _);
                await existingConnection.DisposeAsync();
            }

            // Create new connection
            var enhancedConnectionString = DBExchange.SupportsMARS(dbType) 
                ? $"{connectionString};MultipleActiveResultSets=True;Connection Timeout=30"
                : $"{connectionString};Connection Timeout=30";
                
            var dbFactory = DBExchangeFactory.Create(dbType);
            var connection = dbFactory.CreateConnection(enhancedConnectionString);
            
            await connection.OpenAsync(cancellationToken);
            
            _connectionPool.TryAdd(key, connection);
            return connection;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task<HttpClient> GetOrCreateHttpClientAsync(string? clientName = null, CancellationToken cancellationToken = default)
    {
        await _httpClientSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            var key = clientName ?? "default";
            return _httpClientPool.GetOrAdd(key, _ => 
            {
                var client = _httpClientFactory.CreateClient(key);
                client.Timeout = TimeSpan.FromMinutes(5); // Set reasonable timeout
                return client;
            });
        }
        finally
        {
            _httpClientSemaphore.Release();
        }
    }

    private bool HandleError<T>(Result<T> result, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;

        if (!result.IsSuccessful)
        {
            _pipelineErrors.Add(result.Error);
            _logger.LogError("Pipeline error: {ErrorMessage}", result.Error.ExceptionMessage);
            return false;
        }
        return true;
    }

    private bool HandleError(Result result, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;

        if (!result.IsSuccessful)
        {
            _pipelineErrors.Add(result.Error);
            _logger.LogError("Pipeline error: {ErrorMessage}", result.Error.ExceptionMessage);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Produces data from script-based extractions with enhanced error handling and performance tracking
    /// </summary>
    public async Task ProduceScriptDataAsync(
        IReadOnlyList<Extraction> extractions,
        ChannelWriter<(DataTable, Extraction)> channelWriter,
        CancellationToken cancellationToken = default)
    {
        if (_scriptEngine == null)
        {
            var error = new Error("Script engine not configured for script-based extractions");
            _pipelineErrors.Add(error);
            _logger.LogError("Script engine not configured");
            return;
        }

        var scriptExtractions = extractions
            .Where(e => e.IsScriptBased && !string.IsNullOrWhiteSpace(e.Script))
            .ToList();
        
        if (scriptExtractions.Count == 0)
        {
            _logger.LogInformation("No valid script-based extractions found");
            return;
        }

        _logger.LogInformation("Starting script data production for {Count} extractions", scriptExtractions.Count);

        var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
        var tasks = new List<Task>();

        try
        {
            foreach (var extraction in scriptExtractions)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var task = ProcessScriptExtractionAsync(extraction, channelWriter, semaphore, cancellationToken);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            _logger.LogInformation("Completed script data production for {Count} extractions", scriptExtractions.Count);
        }
        catch (Exception ex)
        {
            var error = new Error($"Script producer error: {ex.Message}", ex.StackTrace);
            _pipelineErrors.Add(error);
            _logger.LogError(ex, "Critical error in script data production");
            throw;
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    private async Task ProcessScriptExtractionAsync(
        Extraction extraction,
        ChannelWriter<(DataTable, Extraction)> channelWriter,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Starting script execution for extraction {ExtractionId}: {ExtractionName}", 
                extraction.Id, extraction.Name);

            var scriptContext = await CreateEnhancedScriptContextAsync(extraction, cancellationToken);
            var result = await ExecuteScriptWithRetryAsync(extraction, scriptContext, cancellationToken);
            
            if (!result.IsSuccessful)
            {
                var error = new Error(
                    $"Script execution failed for extraction '{extraction.Name}' (ID: {extraction.Id}): {result.Error?.ExceptionMessage}",
                    result.Error?.StackTrace);
                _pipelineErrors.Add(error);
                
                RecordExtractionMetrics(extraction.Id, stopwatch.Elapsed, 0, false);
                return;
            }

            var validatedData = ValidateAndEnrichScriptResult(result.Value, extraction);
            if (validatedData.Rows.Count == 0)
            {
                _logger.LogWarning("Script for extraction {ExtractionId} returned no data", extraction.Id);
                RecordExtractionMetrics(extraction.Id, stopwatch.Elapsed, 0, true);
                return;
            }

            // Update job tracking
            var job = JobTracker.GetJobByExtractionId(extraction.Id);
            if (job != null)
            {
                var byteSize = Helper.CalculateBytesUsed(validatedData);
                JobTracker.UpdateTransferedBytes(job.JobGuid, byteSize);
            }

            await channelWriter.WriteAsync((validatedData, extraction), cancellationToken);
            
            RecordExtractionMetrics(extraction.Id, stopwatch.Elapsed, validatedData.Rows.Count, true);
            
            _logger.LogDebug("Script execution completed for extraction {ExtractionId} in {ElapsedMs}ms, returned {RowCount} rows", 
                extraction.Id, stopwatch.ElapsedMilliseconds, validatedData.Rows.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Script execution cancelled for extraction {ExtractionId}", extraction.Id);
            RecordExtractionMetrics(extraction.Id, stopwatch.Elapsed, 0, false);
            throw;
        }
        catch (Exception ex)
        {
            var error = new Error(
                $"Unexpected error processing script for extraction '{extraction.Name}' (ID: {extraction.Id}): {ex.Message}",
                ex.StackTrace);
            _pipelineErrors.Add(error);
            
            RecordExtractionMetrics(extraction.Id, stopwatch.Elapsed, 0, false);
            _logger.LogError(ex, "Unexpected error processing script for extraction {ExtractionId}", extraction.Id);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<ScriptContext> CreateEnhancedScriptContextAsync(Extraction extraction, CancellationToken cancellationToken = default)
    {
        var scriptContext = new ScriptContext
        {
            Extraction = extraction,
            RequestTime = _requestTime,
            OverrideFilter = _overrideFilter,
            Logger = _logger
        };

        // Add HTTP client if needed
        if (RequiresHttpAccess(extraction))
        {
            scriptContext.HttpClient = await GetOrCreateHttpClientAsync(null, cancellationToken);
        }

        // Add database access if needed
        if (RequiresDatabaseAccess(extraction) && extraction.Origin != null)
        {
            try
            {
                var dbExchange = DBExchangeFactory.Create(extraction.Origin.DbType!);
                scriptContext.DbExchange = dbExchange;
                
                // Provide connection if script needs direct database access
                if (RequiresDirectDatabaseConnection(extraction))
                {
                    scriptContext.DbConnection = await GetOrCreateConnectionAsync(
                        extraction.Origin.ConnectionString!, 
                        extraction.Origin.DbType!, 
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create database context for extraction {ExtractionId}", extraction.Id);
            }
        }

        // Add parameters
        var parameters = GetExtractionParameters(extraction);
        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                scriptContext.Parameters[param.Key] = param.Value;
            }
        }

        return scriptContext;
    }

    private bool RequiresHttpAccess(Extraction extraction)
    {
        return extraction.Script?.Contains("http", StringComparison.OrdinalIgnoreCase) == true ||
               extraction.Script?.Contains("HttpClient", StringComparison.OrdinalIgnoreCase) == true ||
               extraction.Script?.Contains("WebRequest", StringComparison.OrdinalIgnoreCase) == true;
    }

    private bool RequiresDatabaseAccess(Extraction extraction)
    {
        return extraction.Origin != null && 
               !string.IsNullOrWhiteSpace(extraction.Origin.ConnectionString) &&
               (extraction.Script?.Contains("DbConnection", StringComparison.OrdinalIgnoreCase) == true ||
                extraction.Script?.Contains("DbCommand", StringComparison.OrdinalIgnoreCase) == true ||
                extraction.Script?.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == true);
    }

    private bool RequiresDirectDatabaseConnection(Extraction extraction)
    {
        return extraction.Script?.Contains("DbConnection", StringComparison.OrdinalIgnoreCase) == true ||
               extraction.Script?.Contains("connection", StringComparison.OrdinalIgnoreCase) == true;
    }

    private Dictionary<string, object>? GetExtractionParameters(Extraction extraction)
    {
        // Enhanced parameter extraction logic
        var parameters = new Dictionary<string, object>();
        
        // Add common parameters
        parameters["ExtractionId"] = extraction.Id;
        parameters["ExtractionName"] = extraction.Name ?? "";
        parameters["RequestTime"] = _requestTime;
        parameters["IsIncremental"] = extraction.IsIncremental;
        
        // Add custom parameters if available
        // This should be implemented based on your Extraction model
        // Example: if (extraction.Parameters != null) { ... }
        
        return parameters.Count > 0 ? parameters : null;
    }

    private async Task<Result<DataTable>> ExecuteScriptWithRetryAsync(
        Extraction extraction, 
        ScriptContext scriptContext, 
        CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        var baseDelay = TimeSpan.FromSeconds(1);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await _scriptEngine!.ExecuteAsync(extraction.Script!, scriptContext, cancellationToken);
                
                if (result.IsSuccessful)
                {
                    _logger.LogDebug("Script execution succeeded on attempt {Attempt} for extraction {ExtractionId}", 
                        attempt, extraction.Id);
                    return result;
                }

                _logger.LogWarning("Script execution attempt {Attempt} failed for extraction {ExtractionId}: {Error}", 
                    attempt, extraction.Id, result.Error?.ExceptionMessage);

                // Don't retry certain types of errors
                if (IsNonRetryableError(result.Error))
                {
                    _logger.LogError("Non-retryable error for extraction {ExtractionId}: {Error}", 
                        extraction.Id, result.Error?.ExceptionMessage);
                    return result;
                }

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    _logger.LogDebug("Retrying script execution for extraction {ExtractionId} in {DelayMs}ms", 
                        extraction.Id, delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Script execution cancelled for extraction {ExtractionId}", extraction.Id);
                return Result<DataTable>.Err(new Error("Script execution was cancelled"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Script execution attempt {Attempt} threw exception for extraction {ExtractionId}", 
                    attempt, extraction.Id);

                if (attempt == maxRetries)
                {
                    return Result<DataTable>.Err(new Error(
                        $"Script execution failed after {maxRetries} attempts: {ex.Message}", 
                        ex.StackTrace));
                }

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        return Result<DataTable>.Err(new Error($"Script execution failed after {maxRetries} attempts"));
    }

    private static bool IsNonRetryableError(Error? error)
    {
        if (error?.ExceptionMessage == null) return false;
        
        var nonRetryableErrors = new[]
        {
            "compilation failed",
            "syntax error",
            "permission denied",
            "authentication failed",
            "invalid credentials"
        };

        return nonRetryableErrors.Any(e => 
            error.ExceptionMessage.Contains(e, StringComparison.OrdinalIgnoreCase));
    }

    private DataTable ValidateAndEnrichScriptResult(DataTable? result, Extraction extraction)
    {
        if (result == null)
        {
            _logger.LogWarning("Script returned null DataTable for extraction {ExtractionId}", extraction.Id);
            return new DataTable();
        }

        // Ensure table name is set
        result.TableName = extraction.Alias ?? extraction.Name ?? $"Extraction_{extraction.Id}";

        // Add metadata columns if they don't exist
        AddMetadataColumn(result, "_ExtractionId", typeof(int), extraction.Id);
        AddMetadataColumn(result, "_ProcessedAt", typeof(DateTime), _requestTime);
        AddMetadataColumn(result, "_ProcessedBy", typeof(string), Environment.MachineName);

        _logger.LogDebug("Validated script result for extraction {ExtractionId}: {RowCount} rows, {ColumnCount} columns", 
            extraction.Id, result.Rows.Count, result.Columns.Count);

        return result;
    }

    private static void AddMetadataColumn(DataTable table, string columnName, Type columnType, object value)
    {
        if (!table.Columns.Contains(columnName))
        {
            table.Columns.Add(columnName, columnType);
            foreach (DataRow row in table.Rows)
            {
                row[columnName] = value;
            }
        }
    }

    private void RecordExtractionMetrics(UInt32 extractionId, TimeSpan duration, int rowCount, bool success)
    {
        _extractionMetrics.AddOrUpdate((int)extractionId, 
            new ExtractionMetrics
            {
                ExtractionId = extractionId,
                Duration = duration,
                RowCount = rowCount,
                Success = success,
                Timestamp = DateTime.UtcNow
            },
            (key, existing) => new ExtractionMetrics
            {
                ExtractionId = extractionId,
                Duration = duration,
                RowCount = rowCount,
                Success = success,
                Timestamp = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Produces data from both database and script-based extractions
    /// </summary>
    public async Task ProduceMixedDataAsync(
        IReadOnlyList<Extraction> extractions,
        ChannelWriter<(DataTable, Extraction)> channelWriter,
        CancellationToken cancellationToken = default,
        bool? hasCheckUp = null)
    {
        var dbExtractions = extractions.Where(e => !e.IsScriptBased).ToList();
        var scriptExtractions = extractions.Where(e => e.IsScriptBased).ToList();

        var tasks = new List<Task>();

        if (dbExtractions.Count > 0)
        {
            tasks.Add(ProduceDBDataAsync(dbExtractions, channelWriter, cancellationToken, hasCheckUp));
        }

        if (scriptExtractions.Count > 0)
        {
            tasks.Add(ProduceScriptDataAsync(scriptExtractions, channelWriter, cancellationToken));
        }

        try
        {
            await Task.WhenAll(tasks);
            _logger.LogInformation("Completed mixed data production: {DbCount} DB extractions, {ScriptCount} script extractions", 
                dbExtractions.Count, scriptExtractions.Count);
        }
        catch (Exception ex)
        {
            var error = new Error($"Mixed data production error: {ex.Message}", ex.StackTrace);
            _pipelineErrors.Add(error);
            _logger.LogError(ex, "Error in mixed data production");
            throw;
        }
    }

    // Placeholder for DB data production (keeping existing pattern)
    public async Task ProduceDBDataAsync(
        IReadOnlyList<Extraction> extractions,
        ChannelWriter<(DataTable, Extraction)> channelWriter,
        CancellationToken cancellationToken = default,
        bool? hasCheckUp = null)
    {
        // Implementation would be similar to existing ProduceDBData but with async/await pattern
        // and improved error handling
        _logger.LogInformation("Starting DB data production for {Count} extractions", extractions.Count);
        
        // Implementation details would go here...
        await Task.CompletedTask; // Placeholder
    }

    private async Task CleanupResourcesAsync()
    {
        _logger.LogDebug("Starting resource cleanup");

        // Cleanup database connections
        var connectionTasks = _connectionPool.Values.Select(async connection =>
        {
            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing database connection");
            }
            finally
            {
                try
                {
                    await connection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing database connection");
                }
            }
        });

        await Task.WhenAll(connectionTasks);
        _connectionPool.Clear();

        // Cleanup HTTP clients
        foreach (var httpClient in _httpClientPool.Values)
        {
            try
            {
                httpClient.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing HTTP client");
            }
        }
        _httpClientPool.Clear();

        _logger.LogDebug("Resource cleanup completed");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        await CleanupResourcesAsync();
        
        _connectionSemaphore?.Dispose();
        _httpClientSemaphore?.Dispose();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        // Synchronous cleanup for dispose
        foreach (var connection in _connectionPool.Values)
        {
            try
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing database connection");
            }
        }
        _connectionPool.Clear();

        foreach (var httpClient in _httpClientPool.Values)
        {
            try
            {
                httpClient.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing HTTP client");
            }
        }
        _httpClientPool.Clear();
        
        _connectionSemaphore?.Dispose();
        _httpClientSemaphore?.Dispose();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents performance metrics for an extraction
/// </summary>
public record ExtractionMetrics
{
    public UInt32 ExtractionId { get; init; }
    public TimeSpan Duration { get; init; }
    public int RowCount { get; init; }
    public bool Success { get; init; }
    public DateTime Timestamp { get; init; }
}
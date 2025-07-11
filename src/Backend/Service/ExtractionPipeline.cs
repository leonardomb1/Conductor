using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Service.Database;
using Conductor.Service.Http;
using Conductor.Service.Script;
using Conductor.Shared;
using Conductor.Types;
using CsvHelper;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Conductor.Service;

public sealed class ExtractionPipeline : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentBag<Error> pipelineErrors = [];
    private readonly IConnectionPoolManager connectionPoolManager;
    private readonly IDataTableMemoryManager memoryManager;
    private readonly ILogger logger = Log.ForContext<ExtractionPipeline>()
        ?? throw new InvalidOperationException("Serilog logger not configured");
    private readonly DateTime requestTime;
    private readonly IHttpClientFactory factory;
    private readonly IJobTracker jobTracker;
    private readonly int? overrideFilter;
    private readonly IScriptEngine? scriptEngine;
    private readonly ActivitySource activitySource = new("Conductor.ExtractionPipeline");
    private volatile bool disposed;
    private readonly Lock disposeLock = new();

    public ExtractionPipeline(
        DateTime requestTime,
        IHttpClientFactory factory,
        IJobTracker jobTracker,
        int? overrideFilter,
        IConnectionPoolManager connectionPoolManager,
        IDataTableMemoryManager memoryManager,
        IScriptEngine? scriptEngine = null)
    {
        this.requestTime = requestTime;
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        this.jobTracker = jobTracker ?? throw new ArgumentNullException(nameof(jobTracker));
        this.overrideFilter = overrideFilter;
        this.connectionPoolManager = connectionPoolManager ?? throw new ArgumentNullException(nameof(connectionPoolManager));
        this.memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
        this.scriptEngine = scriptEngine;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ReturnOnCancellation(CancellationToken token, out Error? error)
    {
        if (token.IsCancellationRequested)
        {
            error = new Error("Cancellation Requested");
            return true;
        }
        error = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryProcessOperation(Result operation, DbConnection? connection, CancellationTokenSource? cancellationTokenSource, out Error? error)
    {
        if (!operation.IsSuccessful)
        {
            try
            {
                connection?.Close();
                cancellationTokenSource?.Cancel();
            }
            catch { }
            error = operation.Error;
            return false;
        }

        error = null;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryProcessOperation<T>(Result<T> operation, DbConnection? connection, CancellationTokenSource? cancellationTokenSource, out Error? error)
    {
        if (!operation.IsSuccessful)
        {
            try
            {
                connection?.Close();
                cancellationTokenSource?.Cancel();
            }
            catch { }
            error = operation.Error;
            return false;
        }

        error = null;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<DbConnection> GetConnectionAsync(string connectionString, string dbType, CancellationToken cancellationToken = default)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(ExtractionPipeline));

        string finalConnectionString = DBExchange.SupportsMARS(dbType)
            ? DBExchange.EnsureMARSEnabled(connectionString, dbType)
            : connectionString;

        return await connectionPoolManager.GetConnectionAsync(finalConnectionString, dbType, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReturnConnection(string connectionString, string dbType, DbConnection connection)
    {
        if (disposed || connection is null) return;

        string finalConnectionString = DBExchange.SupportsMARS(dbType)
            ? DBExchange.EnsureMARSEnabled(connectionString, dbType)
            : connectionString;

        string connectionKey = GenerateConnectionKey(finalConnectionString, dbType);
        connectionPoolManager.ReturnConnection(connectionKey, connection);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GenerateConnectionKey(string connectionString, string dbType)
    {
        return string.Concat(dbType.AsSpan(), ":".AsSpan(), connectionString.GetHashCode().ToString().AsSpan());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ManagedDataTable CreateManagedDataTable(string identifier)
    {
        return memoryManager.CreateManagedDataTable($"{identifier}_{Guid.NewGuid():N}_{requestTime.Ticks}");
    }

    private static async Task<Result> WriteToCsvMemoryAsync(DataTable data, Stream memory, bool writeHeader = true)
    {
        ILogger logger = Log.ForContext<ExtractionPipeline>();

        try
        {
            using StreamWriter writer = new(memory, leaveOpen: true);
            using CsvWriter csv = new(writer, CultureInfo.InvariantCulture);

            if (writeHeader)
            {
                int cols = data.Columns.Count;
                for (var i = 0; i < cols; i++)
                {
                    csv.WriteField(data.Columns[i].ColumnName);
                }
                await csv.NextRecordAsync().ConfigureAwait(false);
            }

            var rowCount = data.Rows.Count;
            var columnCount = data.Columns.Count;

            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var row = data.Rows[rowIndex];
                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    csv.WriteField(row[columnIndex]);
                }
                await csv.NextRecordAsync().ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
            logger.Debug("Successfully wrote {RowCount} rows to CSV memory stream", data.Rows.Count);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to write CSV data to memory stream");
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    private static async Task<Result> WriteMemoryStreamToFileAsync(MemoryStream memory, string filePath)
    {
        ILogger logger = Log.ForContext<ExtractionPipeline>();

        try
        {
            memory.Position = 0;
            using FileStream fileStream = new(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: Settings.FileStreamBufferSize);
            await memory.CopyToAsync(fileStream).ConfigureAwait(false);
            logger.Information("Successfully wrote CSV file: {FilePath} ({Size:N0} bytes)", filePath, memory.Length);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to write memory stream to file: {FilePath}", filePath);
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> ChannelParallelize(
        List<Extraction> extractions,
        Func<List<Extraction>, Channel<(ManagedDataTable, Extraction)>, DateTime, CancellationToken, bool?, Task> produceData,
        Func<Channel<(ManagedDataTable, Extraction)>, DateTime, CancellationToken, Task> consumeData,
        CancellationToken token,
        bool? shouldCheckUp = null
    )
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(ExtractionPipeline));

        ArgumentNullException.ThrowIfNull(extractions);
        ArgumentNullException.ThrowIfNull(produceData);
        ArgumentNullException.ThrowIfNull(consumeData);

        using var activity = activitySource.StartActivity("ChannelParallelize");
        activity?.SetTag("extraction.count", extractions.Count);
        activity?.SetTag("request.time", requestTime);

        logger.Information("Starting extraction pipeline with {ExtractionCount} extractions at {RequestTime}",
            extractions.Count, requestTime);

        var channelOptions = new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        var channel = Channel.CreateUnbounded<(ManagedDataTable, Extraction)>(channelOptions);
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);

        var stopwatch = Stopwatch.StartNew();

        var producer = Task.Run(
            async () => await produceData(extractions, channel, requestTime, cancellationTokenSource.Token, shouldCheckUp).ConfigureAwait(false),
            cancellationTokenSource.Token
        );

        var consumer = Task.Run(
            async () => await consumeData(channel, requestTime, cancellationTokenSource.Token).ConfigureAwait(false),
            cancellationTokenSource.Token
        );

        try
        {
            await Task.WhenAll(producer, consumer).ConfigureAwait(false);
            stopwatch.Stop();

            activity?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("success", true);

            logger.Information("Extraction pipeline completed successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("success", false);
            activity?.SetTag("error", ex.Message);

            logger.Error(ex, "Extraction pipeline flow was interrupted and failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            await CleanupResourcesAsync().ConfigureAwait(false);

            if (!pipelineErrors.IsEmpty)
            {
                logger.Warning("Pipeline completed with {ErrorCount} errors", pipelineErrors.Count);
                if (Settings.SendWebhookOnError && !Settings.WebhookUri.IsNullOrEmpty())
                {
                    _ = Task.Run(async () => await Helper.SendErrorNotification(factory, [.. pipelineErrors]).ConfigureAwait(false));
                }
            }
        }

        return pipelineErrors.IsEmpty ? Result.Ok() : Result.Err(pipelineErrors.ToList());
    }

    private async Task CleanupResourcesAsync()
    {
        logger.Debug("Cleaning up extraction pipeline resources");

        try
        {
            await memoryManager.CleanupExpiredTablesAsync().ConfigureAwait(false);
            await connectionPoolManager.CleanupIdleConnectionsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during resource cleanup");
            pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
        }
    }

    private async Task<Result<ulong>> ProduceDataCheck(Extraction extraction, CancellationToken token)
    {
        using var activity = activitySource.StartActivity("ProduceDataCheck");
        activity?.SetTag("extraction.id", extraction.Id);

        logger.Debug("Performing data check for extraction {ExtractionId} ({ExtractionName})", extraction.Id, extraction.Name);

        if (ReturnOnCancellation(token, out var error))
            return error!;

        if (extraction.Destination is null)
            return new Error("Extraction destination is null");

        using var cancellationTokenSource = new CancellationTokenSource();
        var metadata = DBExchangeFactory.Create(extraction.Destination.DbType);

        DbConnection? connection = null;
        try
        {
            connection = await GetConnectionAsync(extraction.Destination.ConnectionString, extraction.Destination.DbType, token);

            var exists = await metadata.Exists(extraction, connection).ConfigureAwait(false);
            if (!TryProcessOperation(exists, connection, cancellationTokenSource, out error))
            {
                logger.Error("Failed to check if table exists for extraction {ExtractionId}: {Error}", extraction.Id, error!.ExceptionMessage);
                return error!;
            }

            ulong destinationRowCount = 0;
            if (exists.Value)
            {
                logger.Debug("Table exists for extraction {ExtractionId}", extraction.Id);

                if (!extraction.IsIncremental)
                {
                    logger.Information("Truncating table for non-incremental extraction {ExtractionId}", extraction.Id);
                    var truncate = await metadata.TruncateTable(extraction, connection).ConfigureAwait(false);
                    if (!TryProcessOperation(truncate, connection, cancellationTokenSource, out error))
                    {
                        logger.Error("Failed to truncate table for extraction {ExtractionId}: {Error}", extraction.Id, error!.ExceptionMessage);
                        return error!;
                    }
                }

                var count = await metadata.CountTableRows(extraction, connection).ConfigureAwait(false);
                if (!TryProcessOperation(count, connection, cancellationTokenSource, out error))
                {
                    logger.Error("Failed to count table rows for extraction {ExtractionId}: {Error}", extraction.Id, error!.ExceptionMessage);
                    return error!;
                }

                destinationRowCount = count.Value;
                logger.Debug("Destination table has {RowCount} rows for extraction {ExtractionId}", destinationRowCount, extraction.Id);
            }

            activity?.SetTag("destination.rows", destinationRowCount);
            return destinationRowCount;
        }
        finally
        {
            if (connection is not null)
            {
                ReturnConnection(extraction.Destination.ConnectionString, extraction.Destination.DbType, connection);
            }
        }
    }

    public async Task ProduceHttpData(
        List<Extraction> extractions,
        Channel<(ManagedDataTable, Extraction)> channel,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(extractions);
        ArgumentNullException.ThrowIfNull(channel);

        using var activity = activitySource.StartActivity("ProduceHttpData");
        activity?.SetTag("extraction.count", extractions.Count);

        logger.Information("Starting HTTP data production for {ExtractionCount} extractions", extractions.Count);

        var options = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Settings.ParallelRule.Value.MaxDegreeOfParallelism
        };

        if (token.IsCancellationRequested) return;

        var completedCount = 0;
        var startTime = DateTime.UtcNow;

        try
        {
            await Parallel.ForEachAsync(extractions, options, async (extraction, cancellationToken) =>
            {
                using var extractionActivity = activitySource.StartActivity("HttpExtraction");
                extractionActivity?.SetTag("extraction.id", extraction.Id);

                var extractionLogger = logger.ForContext("ExtractionId", extraction.Id)
                                           .ForContext("ExtractionName", extraction.Name);

                if (cancellationToken.IsCancellationRequested) return;

                extractionLogger.Debug("Starting HTTP data fetch for extraction {ExtractionId}", extraction.Id);

                var (exchange, httpMethod) = HTTPExchangeFactory.Create(factory, extraction.PaginationType, extraction.HttpMethod);

                var fetchResult = await exchange.FetchEndpointData(extraction, httpMethod).ConfigureAwait(false);
                if (!fetchResult.IsSuccessful)
                {
                    extractionLogger.Error("HTTP fetch failed for extraction {ExtractionId}: {Error}", extraction.Id, fetchResult.Error.ExceptionMessage);
                    pipelineErrors.Add(fetchResult.Error!);
                    extractionActivity?.SetTag("success", false);
                    return;
                }

                var dataTableResult = Converter.ProcessJsonDocument(fetchResult.Value);
                if (!dataTableResult.IsSuccessful)
                {
                    extractionLogger.Error("JSON processing failed for extraction {ExtractionId}: {Error}", extraction.Id, dataTableResult.Error.ExceptionMessage);
                    pipelineErrors.Add(dataTableResult.Error!);
                    extractionActivity?.SetTag("success", false);
                    return;
                }

                var managedTable = CreateManagedDataTable($"http_extraction_{extraction.Id}");
                managedTable.Table.Merge(dataTableResult.Value);
                dataTableResult.Value.Dispose();

                extractionLogger.Information("HTTP data fetch completed for extraction {ExtractionId}: {RowCount} rows",
                    extraction.Id, managedTable.Table.Rows.Count);

                extractionActivity?.SetTag("rows.count", managedTable.Table.Rows.Count);
                extractionActivity?.SetTag("success", true);

                await channel.Writer.WriteAsync((managedTable, extraction), cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref completedCount);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "HTTP producer thread error");
            pipelineErrors.Add(new Error($"HTTP producer thread error: {ex.Message}", ex.StackTrace));
            activity?.SetTag("error", ex.Message);
        }
        finally
        {
            channel.Writer.Complete();
            var duration = DateTime.UtcNow - startTime;
            activity?.SetTag("completed.count", completedCount);
            activity?.SetTag("duration.ms", duration.TotalMilliseconds);

            logger.Information("HTTP data production completed: {CompletedCount}/{TotalCount} extractions in {Duration}ms",
                completedCount, extractions.Count, duration.TotalMilliseconds);
        }
    }

    public async Task ProduceScriptData(
        List<Extraction> extractions,
        Channel<(ManagedDataTable, Extraction)> channel,
        DateTime requestTime,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(extractions);
        ArgumentNullException.ThrowIfNull(channel);

        using var activity = activitySource.StartActivity("ProduceScriptData");
        activity?.SetTag("extraction.count", extractions.Count);

        logger.Information("Starting script data production for {ExtractionCount} extractions", extractions.Count);

        if (scriptEngine is null)
        {
            logger.Error("Script engine not configured for script-based extractions");
            pipelineErrors.Add(new Error("Script engine not configured"));
            channel.Writer.Complete();
            return;
        }

        var options = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Settings.ParallelRule.Value.MaxDegreeOfParallelism
        };

        if (token.IsCancellationRequested) return;

        var completedCount = 0;
        var startTime = DateTime.UtcNow;

        try
        {
            await Parallel.ForEachAsync(extractions, options, async (extraction, cancellationToken) =>
            {
                using var extractionActivity = activitySource.StartActivity("ScriptExtraction");
                extractionActivity?.SetTag("extraction.id", extraction.Id);

                var extractionLogger = logger.ForContext("ExtractionId", extraction.Id)
                                           .ForContext("ExtractionName", extraction.Name);

                if (cancellationToken.IsCancellationRequested || !extraction.IsScriptBased) return;

                extractionLogger.Debug("Starting script execution for extraction {ExtractionId}", extraction.Id);

                var scriptContext = new ScriptContext
                {
                    Extraction = extraction,
                    RequestTime = requestTime,
                    OverrideFilter = overrideFilter,
                };

                var result = await scriptEngine.ExecuteAsync(extraction.Script!, scriptContext, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccessful)
                {
                    extractionLogger.Error("Script execution failed for extraction {ExtractionId}: {Error}", extraction.Id, result.Error.ExceptionMessage);
                    pipelineErrors.Add(result.Error!);
                    extractionActivity?.SetTag("success", false);
                    return;
                }

                var managedTable = CreateManagedDataTable($"script_extraction_{extraction.Id}");
                managedTable.Table.Merge(result.Value);
                result.Value.Dispose();

                extractionLogger.Information("Script execution completed for extraction {ExtractionId}: {RowCount} rows",
                    extraction.Id, managedTable.Table.Rows.Count);

                extractionActivity?.SetTag("rows.count", managedTable.Table.Rows.Count);
                extractionActivity?.SetTag("success", true);

                await channel.Writer.WriteAsync((managedTable, extraction), cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref completedCount);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Script producer thread error");
            pipelineErrors.Add(new Error($"Script producer thread error: {ex.Message}", ex.StackTrace));
            activity?.SetTag("error", ex.Message);
        }
        finally
        {
            channel.Writer.Complete();
            var duration = DateTime.UtcNow - startTime;
            activity?.SetTag("completed.count", completedCount);
            activity?.SetTag("duration.ms", duration.TotalMilliseconds);

            logger.Information("Script data production completed: {CompletedCount}/{TotalCount} extractions in {Duration}ms",
                completedCount, extractions.Count, duration.TotalMilliseconds);
        }
    }

    public async Task ProduceDBData(
        List<Extraction> extractions,
        Channel<(ManagedDataTable, Extraction)> channel,
        DateTime requestTime,
        CancellationToken token,
        bool? hasCheckUp = null
    )
    {
        ArgumentNullException.ThrowIfNull(extractions);
        ArgumentNullException.ThrowIfNull(channel);

        using var activity = activitySource.StartActivity("ProduceDBData");
        activity?.SetTag("extraction.count", extractions.Count);

        logger.Information("Starting database data production for {ExtractionCount} extractions", extractions.Count);

        if (token.IsCancellationRequested) return;

        var options = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Settings.ParallelRule.Value.MaxDegreeOfParallelism
        };

        var completedCount = 0;
        var totalRowsProduced = 0L;
        var startTime = DateTime.UtcNow;

        try
        {
            await Parallel.ForEachAsync(extractions, options, async (extraction, cancellationToken) =>
            {
                using var extractionActivity = activitySource.StartActivity("DBExtraction");
                extractionActivity?.SetTag("extraction.id", extraction.Id);

                var extractionLogger = logger.ForContext("ExtractionId", extraction.Id)
                                            .ForContext("ExtractionName", extraction.Name);

                if (cancellationToken.IsCancellationRequested) return;
                if (extraction.Origin is null) return;
                if (extraction.Origin.DbType is null || extraction.Origin.ConnectionString is null) return;

                string dbType = extraction.Origin.DbType;
                string connectionString = extraction.Origin.ConnectionString;

                extractionLogger.Debug(
                    "Starting database data fetch for extraction {ExtractionId} using {DbType} (MARS: {MARSSupported})",
                    extraction.Id, dbType, DBExchange.SupportsMARS(dbType)
                );

                bool shouldPartition = false;

                if (hasCheckUp is not null && hasCheckUp.Value)
                {
                    var result = await ProduceDataCheck(extraction, cancellationToken).ConfigureAwait(false);
                    if (!result.IsSuccessful)
                    {
                        extractionLogger?.Error("Data check failed for extraction {ExtractionId}", extraction.Id);
                        extractionActivity?.SetTag("success", false);
                        return;
                    }
                    shouldPartition = result.Value > 0;

                    if (shouldPartition)
                    {
                        extractionLogger?.Information("Using partitioned fetch for extraction {ExtractionId} (existing rows: {ExistingRows})",
                            extraction.Id, result.Value);
                    }
                }

                var fetcher = DBExchangeFactory.Create(extraction.Origin.DbType);
                var extractionRowCount = 0;
                ulong currentOffset = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    Result<DataTable> attempt;
                    DbConnection? connection = null;

                    try
                    {
                        connection = await GetConnectionAsync(connectionString, dbType, cancellationToken);
                        attempt = await fetcher.FetchDataTable(
                            extraction,
                            requestTime,
                            shouldPartition,
                            currentOffset,
                            connection,
                            cancellationToken,
                            overrideFilter,
                            Settings.ProducerLineMax
                        ).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        extractionLogger?.Warning("Connection disposed during fetch for extraction {ExtractionId}", extraction.Id);
                        break;
                    }
                    finally
                    {
                        if (connection is not null)
                        {
                            ReturnConnection(extraction.Origin.ConnectionString, extraction.Origin.DbType, connection);
                        }
                    }

                    if (!attempt.IsSuccessful)
                    {
                        if (attempt.HasMultipleErrors)
                        {
                            foreach (var err in attempt.Errors)
                            {
                                var error = err ?? new Error("Unknown database fetch error occurred");
                                extractionLogger?.Error("Database fetch failed for extraction {ExtractionId}: {Error}", extraction.Id, error.ExceptionMessage);
                                pipelineErrors.Add(error);
                            }
                        }
                        else
                        {
                            var error = attempt.Error ?? new Error("Unknown database fetch error occurred");
                            extractionLogger?.Error("Database fetch failed for extraction {ExtractionId}: {Error}", extraction.Id, error.ExceptionMessage);
                            pipelineErrors.Add(error);
                        }

                        extractionActivity?.SetTag("success", false);
                        break;
                    }

                    if (attempt.Value.Rows.Count == 0)
                    {
                        extractionLogger?.Debug("No more data to fetch for extraction {ExtractionId}", extraction.Id);
                        attempt.Value.Dispose();
                        break;
                    }

                    extractionRowCount += attempt.Value.Rows.Count;
                    currentOffset += (ulong)attempt.Value.Rows.Count;
                    Interlocked.Add(ref totalRowsProduced, attempt.Value.Rows.Count);

                    extractionLogger?.Debug("Fetched {RowCount} rows for extraction {ExtractionId} (total fetched: {TotalFetched})",
                        attempt.Value.Rows.Count, extraction.Id, currentOffset);

                    var managedTable = CreateManagedDataTable($"db_extraction_{extraction.Id}_batch_{currentOffset}");

                    Helper.GetAndSetByteUsageForExtraction(attempt.Value, extraction.Id, jobTracker);

                    managedTable.Table.Merge(attempt.Value);
                    attempt.Value.Dispose();

                    await channel.Writer.WriteAsync((managedTable, extraction), cancellationToken).ConfigureAwait(false);
                }

                if (extractionRowCount > 0)
                {
                    extractionLogger?.Information("Database data fetch completed for extraction {ExtractionId}: {TotalRows} rows",
                        extraction.Id, extractionRowCount);
                }

                extractionActivity?.SetTag("rows.count", extractionRowCount);
                extractionActivity?.SetTag("success", true);

                Interlocked.Increment(ref completedCount);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Database producer thread error");
            pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
            activity?.SetTag("error", ex.Message);
        }
        finally
        {
            channel.Writer.Complete();
            var duration = DateTime.UtcNow - startTime;
            activity?.SetTag("completed.count", completedCount);
            activity?.SetTag("total.rows", totalRowsProduced);
            activity?.SetTag("duration.ms", duration.TotalMilliseconds);

            logger.Information("Database data production completed: {CompletedCount}/{TotalCount} extractions, {TotalRows:N0} rows in {Duration}ms",
                completedCount, extractions.Count, totalRowsProduced, duration.TotalMilliseconds);
        }
    }

    public async Task ConsumeDataToCsv(Channel<(ManagedDataTable, Extraction)> channel, DateTime requestTime, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(channel);

        using var activity = activitySource.StartActivity("ConsumeDataToCsv");
        logger.Information("Starting CSV data consumption");

        if (token.IsCancellationRequested) return;

        var options = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Settings.ParallelRule.Value.MaxDegreeOfParallelism
        };

        var totalRowsProcessed = 0L;
        var totalFilesCreated = 0;
        var startTime = DateTime.UtcNow;

        try
        {
            while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                if (token.IsCancellationRequested) return;

                var fetchedData = new List<(ManagedDataTable data, Extraction metadata)>(Settings.ConsumerFetchMax);

                for (ushort i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (ManagedDataTable data, Extraction metadata) item); i++)
                {
                    fetchedData.Add(item);
                }

                logger.Debug("Processing batch of {BatchSize} extractions for CSV output", fetchedData.Count);

                await Parallel.ForEachAsync(fetchedData, options, async (item, cancellationToken) =>
                {
                    using var batchActivity = activitySource.StartActivity("CsvBatchWrite");
                    batchActivity?.SetTag("extraction.id", item.metadata.Id);

                    var extractionLogger = logger.ForContext("ExtractionId", item.metadata.Id)
                                            .ForContext("ExtractionName", item.metadata.Name);

                    if (cancellationToken.IsCancellationRequested) return;

                    var insertTaskResult = Result.Ok();

                    var tableKey = item.metadata.Alias ?? item.metadata.Name;
                    var filePath = Path.Combine(Settings.CsvOutputPath, $"{tableKey}_{requestTime:yyyyMMddHH}.csv");

                    var writeHeader = !File.Exists(filePath);

                    extractionLogger.Debug("Writing {RowCount} rows to CSV file: {FilePath} (WriteHeader: {WriteHeader})",
                        item.data.Table.Rows.Count, filePath, writeHeader);

                    for (byte attempt = 0; attempt < Settings.PipelineAttemptMax; attempt++)
                    {
                        if (attempt > 0)
                        {
                            extractionLogger.Warning("Retrying CSV write for extraction {ExtractionId}, attempt {Attempt}",
                                item.metadata.Id, attempt + 1);
                        }

                        using var memory = new MemoryStream();
                        var writeToMemory = await WriteToCsvMemoryAsync(item.data.Table, memory, writeHeader).ConfigureAwait(false);

                        if (!writeToMemory.IsSuccessful)
                        {
                            insertTaskResult = Result.Err(writeToMemory.Error);
                            await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        var writeToFile = await WriteMemoryStreamToFileAsync(memory, filePath).ConfigureAwait(false);

                        if (!writeToFile.IsSuccessful)
                        {
                            insertTaskResult = Result.Err(writeToFile.Error);
                            await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        extractionLogger.Information("Successfully wrote CSV file for extraction {ExtractionId}: {FilePath} ({RowCount} rows)",
                            item.metadata.Id, filePath, item.data.Table.Rows.Count);

                        Interlocked.Add(ref totalRowsProcessed, item.data.Table.Rows.Count);
                        Interlocked.Increment(ref totalFilesCreated);

                        batchActivity?.SetTag("rows.count", item.data.Table.Rows.Count);
                        batchActivity?.SetTag("success", true);

                        insertTaskResult = Result.Ok();
                        break;
                    }

                    if (!insertTaskResult.IsSuccessful)
                    {
                        extractionLogger.Error("Failed to write CSV file for extraction {ExtractionId} after {MaxAttempts} attempts",
                            item.metadata.Id, Settings.PipelineAttemptMax);
                        pipelineErrors.Add(insertTaskResult.Error);
                        batchActivity?.SetTag("success", false);
                    }

                    item.data.Dispose();
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            activity?.SetTag("files.created", totalFilesCreated);
            activity?.SetTag("rows.processed", totalRowsProcessed);
            activity?.SetTag("duration.ms", duration.TotalMilliseconds);

            logger.Information("CSV data consumption completed: {TotalFiles} files created, {TotalRows:N0} rows processed in {Duration}ms",
                totalFilesCreated, totalRowsProcessed, duration.TotalMilliseconds);
        }
    }

    public async Task ConsumeDataToDB(Channel<(ManagedDataTable, Extraction)> channel, DateTime requestTime, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(channel);

        using var activity = activitySource.StartActivity("ConsumeDataToDB");
        logger.Information("Starting database data consumption");

        if (token.IsCancellationRequested) return;

        var options = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Settings.ParallelRule.Value.MaxDegreeOfParallelism
        };

        var tablesExecutionState = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        var totalRowsProcessed = 0L;
        var totalTablesProcessed = 0;
        var startTime = DateTime.UtcNow;

        try
        {
            while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                if (token.IsCancellationRequested) return;

                var fetchedData = new List<(ManagedDataTable data, Extraction metadata)>(Settings.ConsumerFetchMax);

                for (ushort i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (ManagedDataTable data, Extraction metadata) item); i++)
                {
                    fetchedData.Add(item);
                }

                logger.Debug("Processing batch of {BatchSize} extractions for database output", fetchedData.Count);

                await Parallel.ForEachAsync(fetchedData, options, async (item, cancellationToken) =>
                {
                    using var batchActivity = activitySource.StartActivity("DatabaseBatchWrite");
                    batchActivity?.SetTag("extraction.id", item.metadata.Id);

                    var extractionLogger = logger.ForContext("ExtractionId", item.metadata.Id)
                                            .ForContext("ExtractionName", item.metadata.Name);

                    if (cancellationToken.IsCancellationRequested) return;
                    if (item.metadata.Destination is null) return;

                    var insertTaskResult = Result.Ok();
                    var inserter = DBExchangeFactory.Create(item.metadata.Destination.DbType);
                    DbConnection? connection = null;

                    try
                    {
                        connection = await GetConnectionAsync(
                            item.metadata.Destination.ConnectionString,
                            item.metadata.Destination.DbType,
                            cancellationToken);

                        var tableKey = item.metadata.Alias ?? item.metadata.Name;

                        extractionLogger.Debug("Processing {RowCount} rows for table {TableKey} in {DbType}",
                            item.data.Table.Rows.Count, tableKey, item.metadata.Destination.DbType);

                        var operationSuccessful = false;

                        for (byte attempt = 0; attempt < Settings.PipelineAttemptMax && !operationSuccessful; attempt++)
                        {
                            if (attempt > 0)
                            {
                                extractionLogger.Warning("Retrying database operation for extraction {ExtractionId}, attempt {Attempt}",
                                    item.metadata.Id, attempt + 1);
                            }

                            try
                            {
                                var exists = await inserter.Exists(item.metadata, connection).ConfigureAwait(false);
                                if (!exists.IsSuccessful)
                                {
                                    insertTaskResult = Result.Err(exists.Error);
                                    await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                                    continue;
                                }

                                if (!exists.Value)
                                {
                                    extractionLogger.Information("Creating table {TableKey} for extraction {ExtractionId}",
                                        tableKey, item.metadata.Id);
                                    var createResult = await inserter.CreateTable(item.data.Table, item.metadata, connection).ConfigureAwait(false);
                                    if (!createResult.IsSuccessful)
                                    {
                                        insertTaskResult = Result.Err(createResult.Error);
                                        await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                                        continue;
                                    }
                                }

                                var count = await inserter.CountTableRows(item.metadata, connection).ConfigureAwait(false);
                                if (!count.IsSuccessful)
                                {
                                    insertTaskResult = Result.Err(count.Error);
                                    await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                                    continue;
                                }

                                var isBulkInsertContinuousExecution = tablesExecutionState.GetOrAdd(tableKey, _ => count.Value == 0);

                                if (tablesExecutionState.TryGetValue(tableKey, out var currentStrategy) && currentStrategy != isBulkInsertContinuousExecution)
                                {
                                    isBulkInsertContinuousExecution = currentStrategy;
                                }
                                else if (!tablesExecutionState.ContainsKey(tableKey))
                                {
                                    extractionLogger.Information("Table {TableKey} load strategy: {Strategy} (existing rows: {ExistingRows})",
                                        tableKey, isBulkInsertContinuousExecution ? "BulkLoad" : "MergeLoad", count.Value);
                                }

                                Result loadResult;
                                if (isBulkInsertContinuousExecution)
                                {
                                    extractionLogger.Debug("Performing bulk load for table {TableKey}", tableKey);
                                    loadResult = await inserter.BulkLoad(item.data.Table, item.metadata, connection).ConfigureAwait(false);
                                }
                                else
                                {
                                    extractionLogger.Debug("Performing merge load for table {TableKey}", tableKey);
                                    loadResult = await inserter.MergeLoad(item.data.Table, item.metadata, requestTime, connection).ConfigureAwait(false);
                                }

                                if (!loadResult.IsSuccessful)
                                {
                                    insertTaskResult = Result.Err(loadResult.Error);
                                    extractionLogger.Warning("Load operation failed for table {TableKey} on attempt {Attempt}: {Error}",
                                        tableKey, attempt + 1, loadResult.Error.ExceptionMessage);

                                    if (isBulkInsertContinuousExecution && attempt < Settings.PipelineAttemptMax - 1)
                                    {
                                        extractionLogger.Information("Switching from bulk load to merge load for table {TableKey} due to failure", tableKey);
                                        tablesExecutionState.TryUpdate(tableKey, false, true);
                                    }

                                    await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                                    continue;
                                }

                                operationSuccessful = true;
                                insertTaskResult = Result.Ok();

                                extractionLogger.Information("Successfully processed {RowCount} rows for table {TableKey}",
                                    item.data.Table.Rows.Count, tableKey);

                                Interlocked.Add(ref totalRowsProcessed, item.data.Table.Rows.Count);
                                Interlocked.Increment(ref totalTablesProcessed);

                                batchActivity?.SetTag("rows.count", item.data.Table.Rows.Count);
                                batchActivity?.SetTag("table.key", tableKey);
                                batchActivity?.SetTag("load.strategy", isBulkInsertContinuousExecution ? "BulkLoad" : "MergeLoad");
                                batchActivity?.SetTag("success", true);
                            }
                            catch (Exception retryEx)
                            {
                                extractionLogger.Warning(retryEx, "Exception during database operation attempt {Attempt} for extraction {ExtractionId}",
                                    attempt + 1, item.metadata.Id);
                                insertTaskResult = Result.Err(new Error(retryEx.Message, retryEx.StackTrace));

                                if (attempt < Settings.PipelineAttemptMax - 1)
                                {
                                    await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                                }
                            }
                        }

                        if (!operationSuccessful)
                        {
                            extractionLogger.Error("Failed to process table {TableKey} for extraction {ExtractionId} after {MaxAttempts} attempts",
                                tableKey, item.metadata.Id, Settings.PipelineAttemptMax);
                            pipelineErrors.Add(insertTaskResult.Error);
                            batchActivity?.SetTag("success", false);
                        }
                    }
                    catch (Exception ex)
                    {
                        extractionLogger.Error(ex, "Unexpected error processing extraction {ExtractionId}", item.metadata.Id);
                        pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
                        batchActivity?.SetTag("success", false);
                        batchActivity?.SetTag("error", ex.Message);
                    }
                    finally
                    {
                        if (connection is not null)
                        {
                            ReturnConnection(item.metadata.Destination.ConnectionString, item.metadata.Destination.DbType, connection);
                        }

                        item.data.Dispose();
                    }
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            activity?.SetTag("tables.processed", totalTablesProcessed);
            activity?.SetTag("rows.processed", totalRowsProcessed);
            activity?.SetTag("duration.ms", duration.TotalMilliseconds);

            logger.Information("Database data consumption completed: {TotalTables} tables processed, {TotalRows:N0} rows processed in {Duration}ms",
                totalTablesProcessed, totalRowsProcessed, duration.TotalMilliseconds);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;

        lock (disposeLock)
        {
            if (disposed) return;
            disposed = true;
        }

        logger.Debug("Disposing extraction pipeline (async)");

        await CleanupResourcesAsync().ConfigureAwait(false);
        activitySource?.Dispose();

        GC.SuppressFinalize(this);
        logger.Debug("Extraction pipeline disposed successfully");
    }

    public void Dispose()
    {
        if (disposed) return;

        lock (disposeLock)
        {
            if (disposed) return;
            disposed = true;
        }

        logger.Debug("Disposing extraction pipeline (sync)");

        try
        {
            CleanupResourcesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Error during synchronous resource cleanup");
        }

        activitySource?.Dispose();
        GC.SuppressFinalize(this);
        logger.Debug("Extraction pipeline disposed successfully");
    }
}
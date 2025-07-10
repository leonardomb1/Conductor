using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
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

public sealed class ExtractionPipeline(DateTime requestTime, IHttpClientFactory factory, IJobTracker jobTracker, int? overrideFilter, IScriptEngine? scriptEngine = null) : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentBag<Error> pipelineErrors = [];
    private readonly ConcurrentDictionary<string, PooledConnection> connectionPool = new(StringComparer.Ordinal);
    private readonly ILogger logger = Log.ForContext<ExtractionPipeline>();
    private readonly DateTime requestTime = requestTime;
    private readonly IHttpClientFactory factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly IJobTracker jobTracker = jobTracker ?? throw new ArgumentNullException(nameof(jobTracker));
    private readonly int? overrideFilter = overrideFilter;
    private readonly IScriptEngine? scriptEngine = scriptEngine;
    private volatile bool disposed;
    private readonly Lock disposeLock = new();

    private sealed class PooledConnection(DbConnection connection) : IAsyncDisposable, IDisposable
    {
        private readonly DbConnection connection = connection ?? throw new ArgumentNullException(nameof(connection));
        private volatile bool disposed;

        public DbConnection Connection => disposed ? throw new ObjectDisposedException(nameof(PooledConnection)) : connection;

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
            catch {}
        }
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
    private static bool TryProcessOperation(Result operation, DbConnection connection, CancellationTokenSource cancellationTokenSource, out Error? error)
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
    private static bool TryProcessOperation<T>(Result<T> operation, DbConnection connection, CancellationTokenSource cancellationTokenSource, out Error? error)
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

    private DbConnection GetOrCreateConnection(string connectionString, string dbType)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(ExtractionPipeline));

        var key = string.Concat(connectionString.AsSpan(), " ".AsSpan(), dbType.AsSpan());
        
        var pooledConnection = connectionPool.GetOrAdd(key, _ =>
        {
            logger.Debug("Creating new database connection for {DbType}", dbType);

            var enhancedConnectionString = DBExchange.SupportsMARS(dbType) 
                ? connectionString + ";MultipleActiveResultSets=True"
                : connectionString;

            if (DBExchange.SupportsMARS(dbType))
            {
                logger.Debug("Enabled MARS for {DbType}", dbType);
            }

            var dbFactory = DBExchangeFactory.Create(dbType);
            var connection = dbFactory.CreateConnection(enhancedConnectionString);
            connection.Open();

            logger.Information("Database connection established for {DbType}", dbType);
            return new PooledConnection(connection);
        });

        return pooledConnection.Connection;
    }

    private static async Task<Result> WriteToCsvMemoryAsync(DataTable data, Stream memory, bool writeHeader = true)
    {
        var logger = Log.ForContext<ExtractionPipeline>();
        logger.Debug("Writing {RowCount} rows to CSV memory stream, WriteHeader: {WriteHeader}", data.Rows.Count, writeHeader);

        try
        {
            using var writer = new StreamWriter(memory, leaveOpen: true);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            if (writeHeader)
            {
                int count = data.Columns.Count;
                for (int i = 0; i < count; i++)
                {
                    csv.WriteField(data.Columns[i].ColumnName);
                }
                await csv.NextRecordAsync().ConfigureAwait(false);
            }

            var rowCount = data.Rows.Count;
            var columnCount = data.Columns.Count;
            
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var row = data.Rows[rowIndex];
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
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
        var logger = Log.ForContext<ExtractionPipeline>();
        logger.Debug("Writing memory stream to file: {FilePath}", filePath);

        try
        {
            memory.Position = 0;
            using var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 81920);
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
        Func<List<Extraction>, Channel<(DataTable, Extraction)>, DateTime, CancellationToken, bool?, Task> produceData,
        Func<Channel<(DataTable, Extraction)>, DateTime, CancellationToken, Task> consumeData,
        CancellationToken token,
        bool? shouldCheckUp = null
    )
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(ExtractionPipeline));

        ArgumentNullException.ThrowIfNull(extractions);
        ArgumentNullException.ThrowIfNull(produceData);
        ArgumentNullException.ThrowIfNull(consumeData);

        logger.Information("Starting extraction pipeline with {ExtractionCount} extractions at {RequestTime}",
            extractions.Count, requestTime);

        var channelOptions = new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        var channel = Channel.CreateUnbounded<(DataTable, Extraction)>(channelOptions);
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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

            logger.Information("Extraction pipeline completed successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.Error(ex, "Extraction pipeline flow was interrupted and failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            await CleanupConnectionsAsync().ConfigureAwait(false);

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

    private async Task CleanupConnectionsAsync()
    {
        logger.Debug("Cleaning up {ConnectionCount} database connections", connectionPool.Count);

        var cleanupTasks = connectionPool.Values.Select(async pooledConnection =>
        {
            try
            {
                await pooledConnection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error disposing database connection during cleanup");
                pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
            }
        });

        await Task.WhenAll(cleanupTasks).ConfigureAwait(false);
        connectionPool.Clear();
    }

    private static async Task<Result<ulong>> ProduceDataCheck(Extraction extraction, CancellationToken token)
    {
        var logger = Log.ForContext<ExtractionPipeline>();
        logger.Debug("Performing data check for extraction {ExtractionId} ({ExtractionName})", extraction.Id, extraction.Name);

        if (ReturnOnCancellation(token, out Error? error)) 
            return error!;

        if (extraction.Destination is null)
            return new Error("Extraction destination is null");

        using var cancellationTokenSource = new CancellationTokenSource();
        var metadata = DBExchangeFactory.Create(extraction.Destination.DbType);
        using var connection = metadata.CreateConnection(extraction.Destination.ConnectionString);

        await connection.OpenAsync(token).ConfigureAwait(false);

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
        else
        {
            logger.Debug("Table does not exist for extraction {ExtractionId}", extraction.Id);
        }

        await connection.CloseAsync().ConfigureAwait(false);
        return destinationRowCount;
    }

    public async Task ProduceHttpData(
        List<Extraction> extractions,
        Channel<(DataTable, Extraction)> channel,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(extractions);
        ArgumentNullException.ThrowIfNull(channel);

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
                    return;
                }

                var dataTableResult = Converter.ProcessJsonDocument(fetchResult.Value);
                if (!dataTableResult.IsSuccessful)
                {
                    extractionLogger.Error("JSON processing failed for extraction {ExtractionId}: {Error}", extraction.Id, dataTableResult.Error.ExceptionMessage);
                    pipelineErrors.Add(dataTableResult.Error!);
                    return;
                }

                extractionLogger.Information("HTTP data fetch completed for extraction {ExtractionId}: {RowCount} rows",
                    extraction.Id, dataTableResult.Value.Rows.Count);

                await channel.Writer.WriteAsync((dataTableResult.Value, extraction), cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref completedCount);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "HTTP producer thread error");
            pipelineErrors.Add(new Error($"HTTP producer thread error: {ex.Message}", ex.StackTrace));
        }
        finally
        {
            channel.Writer.Complete();
            var duration = DateTime.UtcNow - startTime;
            logger.Information("HTTP data production completed: {CompletedCount}/{TotalCount} extractions in {Duration}ms",
                completedCount, extractions.Count, duration.TotalMilliseconds);
        }
    }

    public async Task ProduceScriptData(
        List<Extraction> extractions,
        Channel<(DataTable, Extraction)> channel,
        DateTime requestTime,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(extractions);
        ArgumentNullException.ThrowIfNull(channel);

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
                    return;
                }

                extractionLogger.Information("Script execution completed for extraction {ExtractionId}: {RowCount} rows",
                    extraction.Id, result.Value.Rows.Count);

                await channel.Writer.WriteAsync((result.Value, extraction), cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref completedCount);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Script producer thread error");
            pipelineErrors.Add(new Error($"Script producer thread error: {ex.Message}", ex.StackTrace));
        }
        finally
        {
            channel.Writer.Complete();
            var duration = DateTime.UtcNow - startTime;
            logger.Information("Script data production completed: {CompletedCount}/{TotalCount} extractions in {Duration}ms",
                completedCount, extractions.Count, duration.TotalMilliseconds);
        }
    }

    public async Task ProduceDBData(
        List<Extraction> extractions,
        Channel<(DataTable, Extraction)> channel,
        DateTime requestTime,
        CancellationToken token,
        bool? hasCheckUp = null
    )
    {
        ArgumentNullException.ThrowIfNull(extractions);
        ArgumentNullException.ThrowIfNull(channel);

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
                var extractionLogger = logger.ForContext("ExtractionId", extraction.Id)
                                        .ForContext("ExtractionName", extraction.Name);

                if (cancellationToken.IsCancellationRequested) return;
                if (extraction.Origin is null) return;
                if (extraction.Origin.DbType is null || extraction.Origin.ConnectionString is null) return;

                extractionLogger.Debug("Starting database data fetch for extraction {ExtractionId}", extraction.Id);

                bool shouldPartition = false;

                if (hasCheckUp is not null && hasCheckUp.Value)
                {
                    var result = await ProduceDataCheck(extraction, cancellationToken).ConfigureAwait(false);
                    if (!result.IsSuccessful)
                    {
                        extractionLogger.Error("Data check failed for extraction {ExtractionId}", extraction.Id);
                        return;
                    }
                    shouldPartition = result.Value > 0;

                    if (shouldPartition)
                    {
                        extractionLogger.Information("Using partitioned fetch for extraction {ExtractionId} (existing rows: {ExistingRows})",
                            extraction.Id, result.Value);
                    }
                }

                var fetcher = DBExchangeFactory.Create(extraction.Origin.DbType);
                var extractionRowCount = 0;
                ulong currentOffset = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    Result<DataTable> attempt;
                    try
                    {
                        var connection = GetOrCreateConnection(extraction.Origin.ConnectionString, extraction.Origin.DbType);
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
                        extractionLogger.Warning("Connection disposed during fetch for extraction {ExtractionId}", extraction.Id);
                        break;
                    }

                    if (!attempt.IsSuccessful)
                    {
                        extractionLogger.Error("Database fetch failed for extraction {ExtractionId}: {Error}", extraction.Id, attempt.Error.ExceptionMessage);
                        pipelineErrors.Add(attempt.Error);
                        break;
                    }

                    if (attempt.Value.Rows.Count == 0)
                    {
                        extractionLogger.Debug("No more data to fetch for extraction {ExtractionId}", extraction.Id);
                        break;
                    }

                    extractionRowCount += attempt.Value.Rows.Count;
                    currentOffset += (ulong)attempt.Value.Rows.Count;
                    Interlocked.Add(ref totalRowsProduced, attempt.Value.Rows.Count);

                    extractionLogger.Debug("Fetched {RowCount} rows for extraction {ExtractionId} (total fetched: {TotalFetched})",
                        attempt.Value.Rows.Count, extraction.Id, currentOffset);

                    Helper.GetAndSetByteUsageForExtraction(attempt.Value, extraction.Id, jobTracker);

                    await channel.Writer.WriteAsync((attempt.Value, extraction), cancellationToken).ConfigureAwait(false);
                }

                if (extractionRowCount > 0)
                {
                    extractionLogger.Information("Database data fetch completed for extraction {ExtractionId}: {TotalRows} rows",
                        extraction.Id, extractionRowCount);
                }

                Interlocked.Increment(ref completedCount);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Database producer thread error");
            pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
        }
        finally
        {
            channel.Writer.Complete();
            var duration = DateTime.UtcNow - startTime;
            logger.Information("Database data production completed: {CompletedCount}/{TotalCount} extractions, {TotalRows:N0} rows in {Duration}ms",
                completedCount, extractions.Count, totalRowsProduced, duration.TotalMilliseconds);
        }
    }

    public async Task ConsumeDataToCsv(Channel<(DataTable, Extraction)> channel, DateTime requestTime, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(channel);

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

                var fetchedData = new List<(DataTable data, Extraction metadata)>(Settings.ConsumerFetchMax);

                for (ushort i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (DataTable data, Extraction metadata) item); i++)
                {
                    fetchedData.Add(item);
                }

                logger.Debug("Processing batch of {BatchSize} extractions for CSV output", fetchedData.Count);

                await Parallel.ForEachAsync(fetchedData, options, async (item, cancellationToken) =>
                {
                    var extractionLogger = logger.ForContext("ExtractionId", item.metadata.Id)
                                            .ForContext("ExtractionName", item.metadata.Name);

                    if (cancellationToken.IsCancellationRequested) return;

                    Result insertTaskResult = Result.Ok();

                    string tableKey = item.metadata.Alias ?? item.metadata.Name;
                    string filePath = Path.Combine(Settings.CsvOutputPath, $"{tableKey}_{requestTime:yyyyMMddHH}.csv");

                    bool writeHeader = !File.Exists(filePath);

                    extractionLogger.Debug("Writing {RowCount} rows to CSV file: {FilePath} (WriteHeader: {WriteHeader})",
                        item.data.Rows.Count, filePath, writeHeader);

                    for (byte attempt = 0; attempt < Settings.PipelineAttemptMax; attempt++)
                    {
                        if (attempt > 0)
                        {
                            extractionLogger.Warning("Retrying CSV write for extraction {ExtractionId}, attempt {Attempt}",
                                item.metadata.Id, attempt + 1);
                        }

                        using var memory = new MemoryStream();
                        var writeToMemory = await WriteToCsvMemoryAsync(item.data, memory, writeHeader).ConfigureAwait(false);

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
                            item.metadata.Id, filePath, item.data.Rows.Count);

                        Interlocked.Add(ref totalRowsProcessed, item.data.Rows.Count);
                        Interlocked.Increment(ref totalFilesCreated);

                        insertTaskResult = Result.Ok();
                        break;
                    }

                    if (!insertTaskResult.IsSuccessful)
                    {
                        extractionLogger.Error("Failed to write CSV file for extraction {ExtractionId} after {MaxAttempts} attempts",
                            item.metadata.Id, Settings.PipelineAttemptMax);
                        pipelineErrors.Add(insertTaskResult.Error);
                    }

                    item.data.Dispose();
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            logger.Information("CSV data consumption completed: {TotalFiles} files created, {TotalRows:N0} rows processed in {Duration}ms",
                totalFilesCreated, totalRowsProcessed, duration.TotalMilliseconds);
        }
    }

    public async Task ConsumeDataToDB(Channel<(DataTable, Extraction)> channel, DateTime requestTime, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(channel);

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

                var fetchedData = new List<(DataTable data, Extraction metadata)>(Settings.ConsumerFetchMax);

                for (ushort i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (DataTable data, Extraction metadata) item); i++)
                {
                    fetchedData.Add(item);
                }

                logger.Debug("Processing batch of {BatchSize} extractions for database output", fetchedData.Count);

                await Parallel.ForEachAsync(fetchedData, options, async (item, cancellationToken) =>
                {
                    var extractionLogger = logger.ForContext("ExtractionId", item.metadata.Id)
                                            .ForContext("ExtractionName", item.metadata.Name);

                    if (cancellationToken.IsCancellationRequested) return;
                    if (item.metadata.Destination is null) return;

                    Result insertTaskResult = Result.Ok();
                    var inserter = DBExchangeFactory.Create(item.metadata.Destination.DbType);
                    DbConnection? connection = null;

                    try
                    {
                        connection = inserter.CreateConnection(item.metadata.Destination.ConnectionString);
                        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                        string tableKey = item.metadata.Alias ?? item.metadata.Name;

                        extractionLogger.Debug("Processing {RowCount} rows for table {TableKey} in {DbType}",
                            item.data.Rows.Count, tableKey, item.metadata.Destination.DbType);

                        bool operationSuccessful = false;

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
                                    var createResult = await inserter.CreateTable(item.data, item.metadata, connection).ConfigureAwait(false);
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

                                bool isBulkInsertContinuousExecution = tablesExecutionState.GetOrAdd(tableKey, _ => count.Value == 0);

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
                                    loadResult = await inserter.BulkLoad(item.data, item.metadata, connection).ConfigureAwait(false);
                                }
                                else
                                {
                                    extractionLogger.Debug("Performing merge load for table {TableKey}", tableKey);
                                    loadResult = await inserter.MergeLoad(item.data, item.metadata, requestTime, connection).ConfigureAwait(false);
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
                                    item.data.Rows.Count, tableKey);

                                Interlocked.Add(ref totalRowsProcessed, item.data.Rows.Count);
                                Interlocked.Increment(ref totalTablesProcessed);
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
                        }
                    }
                    catch (Exception ex)
                    {
                        extractionLogger.Error(ex, "Unexpected error processing extraction {ExtractionId}", item.metadata.Id);
                        pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
                    }
                    finally
                    {
                        if (connection is not null)
                        {
                            try
                            {
                                await connection.CloseAsync().ConfigureAwait(false);
                                await connection.DisposeAsync().ConfigureAwait(false);
                            }
                            catch (Exception disposeEx)
                            {
                                extractionLogger.Warning(disposeEx, "Error disposing connection for extraction {ExtractionId}", item.metadata.Id);
                            }
                        }

                        item.data.Dispose();
                    }
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
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

        await CleanupConnectionsAsync().ConfigureAwait(false);

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

        var disposeTasks = connectionPool.Values.Select(pooledConnection =>
        {
            try
            {
                pooledConnection.Dispose();
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Error disposing database connection during sync cleanup");
            }
            return Task.CompletedTask;
        });

        try
        {
            Task.WaitAll([.. disposeTasks], TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Timeout or error during synchronous disposal");
        }

        connectionPool.Clear();
        GC.SuppressFinalize(this);
        logger.Debug("Extraction pipeline disposed successfully");
    }
}
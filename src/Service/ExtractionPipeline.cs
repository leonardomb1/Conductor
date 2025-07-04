using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading.Channels;
using Conductor.Model;
using Conductor.Service.Database;
using Conductor.Service.Http;
using Conductor.Service.Script;
using Conductor.Shared;
using Conductor.Types;
using CsvHelper;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Conductor.Service;

public class ExtractionPipeline(DateTime requestTime, IHttpClientFactory factory, Int32? overrideFilter, IScriptEngine? scriptEngine = null) : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentBag<Error> pipelineErrors = [];
    private readonly ConcurrentDictionary<string, DbConnection> connectionPool = new();
    private readonly ILogger logger = Log.ForContext<ExtractionPipeline>();
    private bool disposed = false;

    private static bool ReturnOnCancellation(CancellationToken t, out Error? error)
    {
        if (t.IsCancellationRequested)
        {
            error = new Error("Cancellation Requested");
            return true;
        }
        error = null;
        return false;
    }
    
    private static bool TryProcessOperation(Result operation, DbConnection con, CancellationTokenSource cts, out Error? err)
    {
        if (!operation.IsSuccessful)
        {
            con.Close();
            cts.Cancel();
            err = operation.Error;
            return false;
        }

        err = null;
        return true;
    }
    
    private static bool TryProcessOperation<T>(Result<T> operation, DbConnection con, CancellationTokenSource cts, out Error? err)
    {
        if (!operation.IsSuccessful)
        {
            con.Close();
            cts.Cancel();
            err = operation.Error;
            return false;
        }

        err = null;
        return true;
    }

    private DbConnection GetOrCreateConnection(string connectionString, string dbType)
    {
        var key = $"{connectionString} {dbType}";
        return connectionPool.GetOrAdd(key, _ =>
        {
            logger.Debug("Creating new database connection for {DbType}", dbType);
            
            if (DBExchange.SupportsMARS(dbType))
            {
                connectionString += ";MultipleActiveResultSets=True";
                logger.Debug("Enabled MARS for {DbType}", dbType);
            }
            
            var dbFactory = DBExchangeFactory.Create(dbType);
            var connection = dbFactory.CreateConnection(connectionString);
            connection.Open();
            
            logger.Information("Database connection established for {DbType}", dbType);
            return connection;
        });
    }

    private static Result WriteToCsvMemory(DataTable data, Stream memory, bool writeHeader = true)
    {
        var logger = Log.ForContext<ExtractionPipeline>();
        logger.Debug("Writing {RowCount} rows to CSV memory stream, WriteHeader: {WriteHeader}", data.Rows.Count, writeHeader);
        
        using var writer = new StreamWriter(memory);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        try
        {
            if (writeHeader)
            {
                foreach (DataColumn column in data.Columns)
                {
                    csv.WriteField(column.ColumnName);
                }
                csv.NextRecord();
            }

            foreach (DataRow row in data.Rows)
            {
                foreach (DataColumn column in data.Columns)
                {
                    csv.WriteField(row[column]);
                }
                csv.NextRecord();
            }

            logger.Debug("Successfully wrote {RowCount} rows to CSV memory stream", data.Rows.Count);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to write CSV data to memory stream");
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    private static Result WriteMemoryStreamToFile(MemoryStream memory, string filePath)
    {
        var logger = Log.ForContext<ExtractionPipeline>();
        logger.Debug("Writing memory stream to file: {FilePath}", filePath);
        
        try
        {
            using FileStream fileStream = new(filePath, FileMode.OpenOrCreate, FileAccess.Write);
            memory.CopyTo(fileStream);
            
            logger.Information("Successfully wrote CSV file: {FilePath} ({Size:N0} bytes)", filePath, memory.Length);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to write memory stream to file: {FilePath}", filePath);
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<MResult> ChannelParallelize(
        List<Extraction> extractions,
        Func<List<Extraction>, Channel<(DataTable, Extraction)>, DateTime, CancellationToken, bool?, Task> produceData,
        Func<Channel<(DataTable, Extraction)>, DateTime, CancellationToken, Task> consumeData,
        CancellationToken token,
        bool? shouldCheckUp = null
    )
    {
        logger.Information("Starting extraction pipeline with {ExtractionCount} extractions at {RequestTime}", 
            extractions.Count, requestTime);

        Channel<(DataTable, Extraction)> channel = Channel.CreateUnbounded<(DataTable, Extraction)>();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Task producer = Task.Run(
            async () => await produceData(extractions, channel, requestTime, cts.Token, shouldCheckUp),
            cts.Token
        );

        Task consumer = Task.Run(
            async () => await consumeData(channel, requestTime, cts.Token),
            cts.Token
        );

        try
        {
            await Task.WhenAll(producer, consumer);
            stopwatch.Stop();
            
            logger.Information("Extraction pipeline completed successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.Error(ex, "Extraction pipeline failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            logger.Debug("Cleaning up {ConnectionCount} database connections", connectionPool.Count);
            
            foreach (var connection in connectionPool.Values)
            {
                try
                {
                    await connection.CloseAsync();
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Error closing database connection during cleanup");
                    pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
                }
                
                try
                {
                    await connection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Error disposing database connection during cleanup");
                    pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
                }
            }
        }

        var result = pipelineErrors.IsEmpty ? MResult.Ok() : pipelineErrors.ToList();
        if (!pipelineErrors.IsEmpty)
        {
            logger.Warning("Pipeline completed with {ErrorCount} errors", pipelineErrors.Count);
        }
        
        return result;
    }

    private static async Task<Result<UInt64>> ProduceDataCheck(Extraction e, CancellationToken t)
    {
        var logger = Log.ForContext<ExtractionPipeline>();
        logger.Debug("Performing data check for extraction {ExtractionId} ({ExtractionName})", e.Id, e.Name);
        
        if (ReturnOnCancellation(t, out Error? err)) return err!;
        using var cts = new CancellationTokenSource();

        var metadata = DBExchangeFactory.Create(e.Destination!.DbType);
        using var con = metadata.CreateConnection(e.Destination.ConnectionString);

        await con.OpenAsync(t);

        var exists = await metadata.Exists(e, con);
        if (!TryProcessOperation(exists, con, cts, out err)) 
        {
            logger.Error("Failed to check if table exists for extraction {ExtractionId}: {Error}", e.Id, err!.ExceptionMessage);
            return err!;
        }

        UInt64 destinationRowCount = 0;
        if (exists.Value)
        {
            logger.Debug("Table exists for extraction {ExtractionId}", e.Id);
            
            if (!e.IsIncremental)
            {
                logger.Information("Truncating table for non-incremental extraction {ExtractionId}", e.Id);
                var truncate = await metadata.TruncateTable(e, con);
                if (!TryProcessOperation(truncate, con, cts, out err)) 
                {
                    logger.Error("Failed to truncate table for extraction {ExtractionId}: {Error}", e.Id, err!.ExceptionMessage);
                    return err!;
                }
            }

            var count = await metadata.CountTableRows(e, con);
            if (!TryProcessOperation(count, con, cts, out err)) 
            {
                logger.Error("Failed to count table rows for extraction {ExtractionId}: {Error}", e.Id, err!.ExceptionMessage);
                return err!;
            }

            destinationRowCount = count.Value;
            logger.Debug("Destination table has {RowCount} rows for extraction {ExtractionId}", destinationRowCount, e.Id);
        }
        else
        {
            logger.Debug("Table does not exist for extraction {ExtractionId}", e.Id);
        }

        await con.CloseAsync();
        return destinationRowCount;
    }

    public async Task ProduceHttpData(
        List<Extraction> extractions,
        Channel<(DataTable, Extraction)> channel,
        CancellationToken token
    )
    {
        logger.Information("Starting HTTP data production for {ExtractionCount} extractions", extractions.Count);
        
        ParallelOptions options = Settings.ParallelRule.Value;
        options.CancellationToken = token;

        if (token.IsCancellationRequested) return;

        var completedCount = 0;
        var startTime = DateTime.UtcNow;

        try
        {
            await Parallel.ForEachAsync(extractions, options, async (extraction, t) =>
            {
                var extractionLogger = logger.ForContext("ExtractionId", extraction.Id)
                                           .ForContext("ExtractionName", extraction.Name);
                
                if (t.IsCancellationRequested) return;

                extractionLogger.Debug("Starting HTTP data fetch for extraction {ExtractionId}", extraction.Id);
                
                var (exchange, httpMethod) = HTTPExchangeFactory.Create(factory!, extraction.PaginationType, extraction.HttpMethod);

                var fetchResult = await exchange.FetchEndpointData(extraction, httpMethod);
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

                await channel.Writer.WriteAsync((dataTableResult.Value, extraction), t);
                
                Interlocked.Increment(ref completedCount);
            });
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
        logger.Information("Starting script data production for {ExtractionCount} extractions", extractions.Count);
        
        if (scriptEngine == null)
        {
            logger.Error("Script engine not configured for script-based extractions");
            pipelineErrors.Add(new Error("Script engine not configured"));
            channel.Writer.Complete();
            return;
        }

        ParallelOptions options = Settings.ParallelRule.Value;
        options.CancellationToken = token;

        if (token.IsCancellationRequested) return;

        var completedCount = 0;
        var startTime = DateTime.UtcNow;

        try
        {
            await Parallel.ForEachAsync(extractions, options, async (extraction, t) =>
            {
                var extractionLogger = logger.ForContext("ExtractionId", extraction.Id)
                                           .ForContext("ExtractionName", extraction.Name);
                
                if (t.IsCancellationRequested || !extraction.IsScriptBased) return;

                extractionLogger.Debug("Starting script execution for extraction {ExtractionId}", extraction.Id);

                var scriptContext = new ScriptContext
                {
                    Extraction = extraction,
                    RequestTime = requestTime,
                    OverrideFilter = overrideFilter,
                };

                var result = await scriptEngine.ExecuteAsync(extraction.Script!, scriptContext, t);
                if (!result.IsSuccessful)
                {
                    extractionLogger.Error("Script execution failed for extraction {ExtractionId}: {Error}", extraction.Id, result.Error.ExceptionMessage);
                    pipelineErrors.Add(result.Error!);
                    return;
                }

                extractionLogger.Information("Script execution completed for extraction {ExtractionId}: {RowCount} rows", 
                    extraction.Id, result.Value.Rows.Count);

                await channel.Writer.WriteAsync((result.Value, extraction), t);
                
                Interlocked.Increment(ref completedCount);
            });
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
        logger.Information("Starting database data production for {ExtractionCount} extractions", extractions.Count);
        
        if (token.IsCancellationRequested) return;

        ParallelOptions options = Settings.ParallelRule.Value;
        options.CancellationToken = token;

        var completedCount = 0;
        var totalRowsProduced = 0L;
        var startTime = DateTime.UtcNow;

        try
        {
            await Parallel.ForEachAsync(extractions, options, async (e, t) =>
            {
                var extractionLogger = logger.ForContext("ExtractionId", e.Id)
                                           .ForContext("ExtractionName", e.Name);
                
                if (t.IsCancellationRequested) return;
                if (e.Origin is null) return;
                if (e.Origin.DbType is null || e.Origin.ConnectionString is null) return;

                extractionLogger.Debug("Starting database data fetch for extraction {ExtractionId}", e.Id);

                bool shouldPartition = false;

                if (hasCheckUp is not null && hasCheckUp.Value)
                {
                    var res = await ProduceDataCheck(e, t);
                    if (!res.IsSuccessful) 
                    {
                        extractionLogger.Error("Data check failed for extraction {ExtractionId}", e.Id);
                        return;
                    }
                    shouldPartition = res.Value > 0;
                    
                    if (shouldPartition)
                    {
                        extractionLogger.Information("Using partitioned fetch for extraction {ExtractionId} (existing rows: {ExistingRows})", 
                            e.Id, res.Value);
                    }
                }

                var fetcher = DBExchangeFactory.Create(e.Origin.DbType);
                var extractionRowCount = 0;

                UInt64 currentLineCount = Settings.ProducerLineMax;
                while (!t.IsCancellationRequested)
                {
                    var attempt =
                        DBExchange.SupportsMARS(e.Origin.DbType)
                        ? await fetcher.FetchDataTable(
                            e,
                            requestTime,
                            shouldPartition,
                            currentLineCount,
                            GetOrCreateConnection(e.Origin.ConnectionString, e.Origin.DbType),
                            t,
                            overrideFilter
                        )
                        : await fetcher.FetchDataTable(e, requestTime, shouldPartition, currentLineCount, t, overrideFilter);

                    if (!attempt.IsSuccessful)
                    {
                        extractionLogger.Error("Database fetch failed for extraction {ExtractionId}: {Error}", e.Id, attempt.Error.ExceptionMessage);
                        pipelineErrors.Add(attempt.Error);
                        break;
                    }

                    if (attempt.Value.Rows.Count == 0) 
                    {
                        extractionLogger.Debug("No more data to fetch for extraction {ExtractionId}", e.Id);
                        break;
                    }

                    extractionRowCount += attempt.Value.Rows.Count;
                    Interlocked.Add(ref totalRowsProduced, attempt.Value.Rows.Count);

                    extractionLogger.Debug("Fetched {RowCount} rows for extraction {ExtractionId} (batch)", 
                        attempt.Value.Rows.Count, e.Id);

                    Helper.GetAndSetByteUsageForExtraction(attempt.Value, e.Id);

                    await channel.Writer.WriteAsync((attempt.Value, e), t);
                }

                if (extractionRowCount > 0)
                {
                    extractionLogger.Information("Database data fetch completed for extraction {ExtractionId}: {TotalRows} rows", 
                        e.Id, extractionRowCount);
                }

                Interlocked.Increment(ref completedCount);
            });
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
        logger.Information("Starting CSV data consumption");
        
        if (token.IsCancellationRequested) return;

        ParallelOptions options = Settings.ParallelRule.Value;
        options.CancellationToken = token;

        var totalRowsProcessed = 0L;
        var totalFilesCreated = 0;
        var startTime = DateTime.UtcNow;

        try
        {
            while (await channel.Reader.WaitToReadAsync(token))
            {
                if (token.IsCancellationRequested) return;

                var fetchedData = new List<(DataTable data, Extraction metadata)>(Settings.ConsumerFetchMax);

                for (UInt16 i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (DataTable data, Extraction metadata) item); i++)
                {
                    fetchedData.Add(item);
                }

                logger.Debug("Processing batch of {BatchSize} extractions for CSV output", fetchedData.Count);

                await Parallel.ForEachAsync(fetchedData, options, async (e, t) =>
                {
                    var extractionLogger = logger.ForContext("ExtractionId", e.metadata.Id)
                                               .ForContext("ExtractionName", e.metadata.Name);
                    
                    if (options.CancellationToken.IsCancellationRequested) return;

                    Result insertTaskResult = Result.Ok();

                    string tableKey = e.metadata.Alias ?? e.metadata.Name;
                    string filePath = Path.Combine(Settings.CsvOutputPath, $"{tableKey}_{requestTime:yyyyMMddHH}.csv");

                    bool writeHeader = !File.Exists(filePath);
                    
                    extractionLogger.Debug("Writing {RowCount} rows to CSV file: {FilePath} (WriteHeader: {WriteHeader})", 
                        e.data.Rows.Count, filePath, writeHeader);

                    for (byte attempt = 0; attempt < Settings.PipelineAttemptMax; attempt++)
                    {
                        if (attempt > 0)
                        {
                            extractionLogger.Warning("Retrying CSV write for extraction {ExtractionId}, attempt {Attempt}", 
                                e.metadata.Id, attempt + 1);
                        }

                        using MemoryStream memory = new();
                        var writeToMemory = WriteToCsvMemory(e.data, memory, writeHeader);

                        if (!writeToMemory.IsSuccessful)
                        {
                            insertTaskResult = Result.Err(writeToMemory.Error);
                            await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), t);
                            continue;
                        }

                        var writeToFile = WriteMemoryStreamToFile(memory, filePath);

                        if (!writeToFile.IsSuccessful)
                        {
                            insertTaskResult = Result.Err(writeToFile.Error);
                            await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), t);
                            continue;
                        }

                        extractionLogger.Information("Successfully wrote CSV file for extraction {ExtractionId}: {FilePath} ({RowCount} rows)", 
                            e.metadata.Id, filePath, e.data.Rows.Count);

                        Interlocked.Add(ref totalRowsProcessed, e.data.Rows.Count);
                        Interlocked.Increment(ref totalFilesCreated);

                        if (insertTaskResult.IsSuccessful) break;
                    }

                    if (!insertTaskResult.IsSuccessful)
                    {
                        extractionLogger.Error("Failed to write CSV file for extraction {ExtractionId} after {MaxAttempts} attempts", 
                            e.metadata.Id, Settings.PipelineAttemptMax);
                        pipelineErrors.Add(insertTaskResult.Error);
                    }

                    e.data.Dispose();
                });
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
        logger.Information("Starting database data consumption");
        
        if (token.IsCancellationRequested) return;

        ParallelOptions options = Settings.ParallelRule.Value;
        options.CancellationToken = token;
        Dictionary<string, bool> tablesExecutionState = [];

        var totalRowsProcessed = 0L;
        var totalTablesProcessed = 0;
        var startTime = DateTime.UtcNow;

        try
        {
            while (await channel.Reader.WaitToReadAsync(token))
            {
                if (token.IsCancellationRequested) return;

                var fetchedData = new List<(DataTable data, Extraction metadata)>(Settings.ConsumerFetchMax);

                for (UInt16 i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (DataTable data, Extraction metadata) item); i++)
                {
                    fetchedData.Add(item);
                }

                logger.Debug("Processing batch of {BatchSize} extractions for database output", fetchedData.Count);

                await Parallel.ForEachAsync(fetchedData, options, async (e, t) =>
                {
                    var extractionLogger = logger.ForContext("ExtractionId", e.metadata.Id)
                                               .ForContext("ExtractionName", e.metadata.Name);
                    
                    if (t.IsCancellationRequested) return;
                    if (e.metadata.Destination is null) return;

                    Result insertTaskResult = Result.Ok();
                    var inserter = DBExchangeFactory.Create(e.metadata.Destination.DbType);
                    using var con = inserter.CreateConnection(e.metadata.Destination.ConnectionString);

                    try
                    {
                        await con.OpenAsync(t);
                        string tableKey = e.metadata.Alias ?? e.metadata.Name;

                        extractionLogger.Debug("Processing {RowCount} rows for table {TableKey} in {DbType}", 
                            e.data.Rows.Count, tableKey, e.metadata.Destination.DbType);

                        for (byte attempt = 0; attempt < Settings.PipelineAttemptMax; attempt++)
                        {
                            if (attempt > 0)
                            {
                                extractionLogger.Warning("Retrying database operation for extraction {ExtractionId}, attempt {Attempt}", 
                                    e.metadata.Id, attempt + 1);
                            }

                            var exists = await inserter.Exists(e.metadata, con);
                            if (!exists.IsSuccessful)
                            {
                                insertTaskResult = Result.Err(exists.Error);
                                await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), t);
                                continue;
                            }

                            if (!exists.Value) 
                            {
                                extractionLogger.Information("Creating table {TableKey} for extraction {ExtractionId}", 
                                    tableKey, e.metadata.Id);
                                await inserter.CreateTable(e.data, e.metadata, con);
                            }

                            var count = await inserter.CountTableRows(e.metadata, con);
                            if (!count.IsSuccessful)
                            {
                                insertTaskResult = Result.Err(count.Error);
                                await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), t);
                                continue;
                            }

                            if (!tablesExecutionState.TryGetValue(tableKey, out bool isBulkInsertContinuousExecution))
                            {
                                isBulkInsertContinuousExecution = count.Value == 0;
                                tablesExecutionState[tableKey] = isBulkInsertContinuousExecution;
                                
                                extractionLogger.Information("Table {TableKey} load strategy: {Strategy} (existing rows: {ExistingRows})", 
                                    tableKey, isBulkInsertContinuousExecution ? "BulkLoad" : "MergeLoad", count.Value);
                            }

                            if (isBulkInsertContinuousExecution)
                            {
                                extractionLogger.Debug("Performing bulk load for table {TableKey}", tableKey);
                                await inserter.BulkLoad(e.data, e.metadata, con);
                            }
                            else
                            {
                                extractionLogger.Debug("Performing merge load for table {TableKey}", tableKey);
                                await inserter.MergeLoad(e.data, e.metadata, requestTime, con);
                            }

                            extractionLogger.Information("Successfully processed {RowCount} rows for table {TableKey}", 
                                e.data.Rows.Count, tableKey);

                            Interlocked.Add(ref totalRowsProcessed, e.data.Rows.Count);
                            Interlocked.Increment(ref totalTablesProcessed);

                            if (insertTaskResult.IsSuccessful) break;
                        }

                        if (!insertTaskResult.IsSuccessful)
                        {
                            extractionLogger.Error("Failed to process table {TableKey} for extraction {ExtractionId} after {MaxAttempts} attempts", 
                                tableKey, e.metadata.Id, Settings.PipelineAttemptMax);
                            pipelineErrors.Add(insertTaskResult.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        extractionLogger.Error(ex, "Unexpected error processing extraction {ExtractionId}", e.metadata.Id);
                        pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
                    }
                    finally
                    {
                        await con.CloseAsync();
                        e.data.Dispose();
                    }
                });
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
        
        logger.Debug("Disposing extraction pipeline (async)");
        
        foreach (var connection in connectionPool.Values)
        {
            try
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Error disposing database connection during async cleanup");
            }
        }
        
        connectionPool.Clear();
        disposed = true;
        GC.SuppressFinalize(this);
        
        logger.Debug("Extraction pipeline disposed successfully");
    }

    public void Dispose()
    {
        if (disposed) return;
        
        logger.Debug("Disposing extraction pipeline (sync)");
        
        foreach (var connection in connectionPool.Values)
        {
            try
            {
                connection.Close();
                connection.Dispose();
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Error disposing database connection during sync cleanup");
            }
        }
        
        connectionPool.Clear();
        disposed = true;
        GC.SuppressFinalize(this);
        
        logger.Debug("Extraction pipeline disposed successfully");
    }
}
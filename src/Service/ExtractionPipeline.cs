using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading.Channels;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Service.Database;
using Conductor.Service.Http;
using Conductor.Shared;
using Conductor.Types;
using CsvHelper;
using Serilog;

namespace Conductor.Service;

public class ExtractionPipeline(DateTime requestTime, IHttpClientFactory factory, Int32? overrideFilter) : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentBag<Error> pipelineErrors = [];

    private readonly ConcurrentDictionary<string, DbConnection> connectionPool = new();

    private bool disposed = false;

    private DbConnection GetOrCreateConnection(string connectionString, string dbType)
    {
        var key = $"{connectionString} {dbType}";
        return connectionPool.GetOrAdd(key, _ =>
        {
            if (DBExchange.SupportsMARS(dbType))
            {
                connectionString += ";MultipleActiveResultSets=True";
            }
            var dbFactory = DBExchangeFactory.Create(dbType);
            var connection = dbFactory.CreateConnection(connectionString);
            connection.Open();
            return connection;
        });
    }

    private bool HandleError<T>(Result<T> result, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;

        if (!result.IsSuccessful)
        {
            pipelineErrors.Add(result.Error);
            return false;
        }
        return true;
    }

    private async Task ProcessFetchAsync(
        Extraction e,
        DBExchange fetcher,
        bool shouldPartition,
        byte offsetMultiplier,
        Channel<(DataTable, Extraction)> channel,
        DateTime requestTime,
        CancellationToken t
    )
    {
        for (UInt64 curr = offsetMultiplier * Settings.ProducerLineMax;
            !t.IsCancellationRequested;
            curr += Settings.ProducerConcurrentFetches * Settings.ProducerLineMax)
        {

            var attempt = DBExchange.SupportsMARS(e.Origin!.DbType!)
                ? await fetcher.FetchDataTable(
                    e,
                    requestTime,
                    shouldPartition,
                    curr,
                    GetOrCreateConnection(e.Origin.ConnectionString!, e.Origin.DbType!),
                    t,
                    overrideFilter
                )
                : await fetcher.FetchDataTable(e, requestTime, shouldPartition, curr, t, overrideFilter);

            if (!HandleError(attempt, t)) break;
            if (attempt.Value.Rows.Count == 0) break;

            Int64 byteSize = Helper.CalculateBytesUsed(attempt.Value);
            var job = JobTracker.GetJobByExtractionId(e.Id);
            if (job is not null) JobTracker.UpdateTransferedBytes(job.JobGuid, byteSize);

            await channel.Writer.WriteAsync((attempt.Value, e), t);
        }
    }

    private bool HandleError(Result result, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;

        if (!result.IsSuccessful)
        {
            pipelineErrors.Add(result.Error);
            return false;
        }
        return true;
    }

    public async Task<MResult> ChannelParallelize(
        List<Extraction> extractions,
        Func<List<Extraction>, Channel<(DataTable, Extraction)>, DateTime, CancellationToken, bool?, Task> produceData,
        Func<Channel<(DataTable, Extraction)>, DateTime, CancellationTokenSource, Task> consumeData,
        CancellationToken token,
        bool? shouldCheckUp = null
    )
    {
        Channel<(DataTable, Extraction)> channel = Channel.CreateUnbounded<(DataTable, Extraction)>();

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        Task producer = Task.Run(async () => await produceData(extractions, channel, requestTime, cts.Token, shouldCheckUp), cts.Token);
        Task consumer = Task.Run(async () => await consumeData(channel, requestTime, cts), cts.Token);

        try
        {
            await Task.WhenAll(producer, consumer);
        }
        finally
        {
            foreach (var connection in connectionPool.Values)
            {
                try
                {
                    await connection.CloseAsync();
                }
                catch (Exception ex)
                {
                    pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
                }
                finally
                {
                    await connection.DisposeAsync();
                }
            }

            connectionPool.Clear();
        }

        return pipelineErrors.IsEmpty ? MResult.Ok() : pipelineErrors.ToList();
    }

    private async Task<Result<UInt64>> ProduceDataCheck(Extraction e, CancellationToken t)
    {
        var metadata = DBExchangeFactory.Create(e.Destination!.DbType);
        using var con = metadata.CreateConnection(e.Destination.ConnectionString);

        await con.OpenAsync(t);

        var exists = await metadata.Exists(e, con);
        if (!HandleError(exists, t)) return new Error("Failed to count rows in destination table"); ;

        UInt64 destRc = 0;
        if (exists.Value)
        {
            if (!e.IsIncremental)
            {
                var truncate = await metadata.TruncateTable(e, con);
                if (!HandleError(truncate, t)) return new Error("Failed to count rows in destination table"); ;
            }

            var count = await metadata.CountTableRows(e, con);
            if (!HandleError(count, t)) return new Error("Failed to count rows in destination table");

            destRc = count.Value;
        }

        await con.CloseAsync();
        return destRc;
    }

    public async Task ProduceHttpData(
        List<Extraction> extractions,
        Channel<(DataTable, Extraction)> channel,
        CancellationToken token
    )
    {
        ParallelOptions options = Settings.ParallelRule.Value;
        options.CancellationToken = token;

        if (token.IsCancellationRequested) return;

        try
        {
            await Parallel.ForEachAsync(extractions, options, async (extraction, t) =>
            {
                if (t.IsCancellationRequested) return;

                var (exchange, httpMethod) = HTTPExchangeFactory.Create(factory!, extraction.PaginationType, extraction.HttpMethod);

                var fetchResult = await exchange.FetchEndpointData(extraction, httpMethod);
                if (!fetchResult.IsSuccessful)
                {
                    pipelineErrors.Add(fetchResult.Error!);
                    return;
                }

                var dataTableResult = Converter.ProcessJsonDocument(fetchResult.Value);
                if (!dataTableResult.IsSuccessful)
                {
                    pipelineErrors.Add(dataTableResult.Error!);
                    return;
                }

                await channel.Writer.WriteAsync((dataTableResult.Value, extraction), t);
            });
        }
        catch (Exception ex)
        {
            pipelineErrors.Add(new Error($"HTTP producer thread error: {ex.Message}", ex.StackTrace));
        }
        finally
        {
            channel.Writer.Complete();
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
        ParallelOptions options = Settings.ParallelRule.Value;
        options.CancellationToken = token;

        if (token.IsCancellationRequested) return;
        try
        {
            await Parallel.ForEachAsync(extractions, options, async (e, t) =>
            {
                if (t.IsCancellationRequested) return;

                bool shouldPartition = false;

                if (hasCheckUp is not null && hasCheckUp.Value)
                {
                    var res = await ProduceDataCheck(e, t);
                    if (!res.IsSuccessful) return;
                    shouldPartition = res.Value > 0;
                }

                var fetcher = DBExchangeFactory.Create(e.Origin!.DbType!);
                var fetchTasks = new List<Task>();

                for (byte i = 0; i < Settings.ProducerConcurrentFetches; i++)
                {
                    byte offsetMultiplier = i;
                    fetchTasks.Add(ProcessFetchAsync(e, fetcher, shouldPartition, offsetMultiplier, channel, requestTime, t));
                }

                await Task.WhenAll(fetchTasks);
            });
        }
        catch (Exception ex)
        {
            pipelineErrors.Add(new Error(ex.Message, ex.StackTrace));
        }
        finally
        {
            channel.Writer.Complete();
        }
    }

    public async Task ConsumeDataToCsv(Channel<(DataTable, Extraction)> channel, DateTime requestTime, CancellationTokenSource cts)
    {
        ParallelOptions options = Settings.ParallelRule.Value;
        options.CancellationToken = cts.Token;

        while (await channel.Reader.WaitToReadAsync(cts.Token))
        {
            if (cts.Token.IsCancellationRequested) return;

            var fetchedData = new List<(DataTable data, Extraction metadata)>(Settings.ConsumerFetchMax);

            for (UInt16 i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (DataTable data, Extraction metadata) item); i++)
            {
                fetchedData.Add(item);
            }

            Result insertRes = Result.Ok();

            for (byte attempt = 0; attempt < Settings.PipelineAttemptMax; attempt++)
            {
                insertRes = Result.Ok();

                Parallel.ForEach(fetchedData, options, (e) =>
                {
                    if (options.CancellationToken.IsCancellationRequested) return;

                    string tableKey = e.metadata.Alias ?? e.metadata.Name;
                    string filePath = Path.Combine(Settings.CsvOutputPath, $"{tableKey}_{requestTime:yyyyMMddHH}.csv");

                    bool writeHeader = !File.Exists(filePath);

                    using var writer = new StreamWriter(filePath, true);
                    using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

                    if (writeHeader)
                    {
                        foreach (DataColumn column in e.data.Columns)
                        {
                            csv.WriteField(column.ColumnName);
                        }
                        csv.NextRecord();
                    }

                    foreach (DataRow row in e.data.Rows)
                    {
                        foreach (DataColumn column in e.data.Columns)
                        {
                            csv.WriteField(row[column]);
                        }
                        csv.NextRecord();
                    }
                });

                if (insertRes.IsSuccessful)
                    break;

                await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), cts.Token);
            }

            if (!insertRes.IsSuccessful)
            {
                pipelineErrors.Add(insertRes.Error!);
                Log.Error("Maximum attempt count has been reached, cancelling...");
                cts.Cancel();
            }

            foreach (var (data, metadata) in fetchedData)
            {
                data.Dispose();
            }
        }
    }

    public async Task ConsumeDataToDB(Channel<(DataTable, Extraction)> channel, DateTime requestTime, CancellationTokenSource cts)
    {
        ParallelOptions options = Settings.ParallelRule.Value;
        options.CancellationToken = cts.Token;

        Dictionary<string, bool> tableFirstExecutionState = [];

        while (await channel.Reader.WaitToReadAsync(cts.Token))
        {
            if (cts.Token.IsCancellationRequested) return;

            var fetchedData = new List<(DataTable data, Extraction metadata)>(Settings.ConsumerFetchMax);

            for (UInt16 i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (DataTable data, Extraction metadata) item); i++)
            {
                fetchedData.Add(item);
            }

            Result insertRes = Result.Ok();

            for (byte attempt = 0; attempt < Settings.PipelineAttemptMax; attempt++)
            {
                insertRes = Result.Ok();

                await Parallel.ForEachAsync(fetchedData, options, async (e, t) =>
                {
                    if (t.IsCancellationRequested) return;

                    var inserter = DBExchangeFactory.Create(e.metadata.Destination!.DbType);
                    using var con = inserter.CreateConnection(e.metadata.Destination.ConnectionString);

                    await con.OpenAsync(t);

                    string tableKey = e.metadata.Alias ?? e.metadata.Name;

                    var exists = await inserter.Exists(e.metadata, con);
                    if (!HandleError(exists, t))
                    {
                        insertRes = Result.Err(exists.Error);
                        return;
                    }

                    if (!exists.Value)
                    {
                        await inserter.CreateTable(e.data, e.metadata, con);
                    }

                    var count = await inserter.CountTableRows(e.metadata, con);
                    if (!HandleError(count, t))
                    {
                        insertRes = Result.Err(count.Error);
                        return;
                    }

                    if (!tableFirstExecutionState.TryGetValue(tableKey, out bool value))
                    {
                        value = count.Value == 0;
                        tableFirstExecutionState[tableKey] = value;
                    }

                    if (value)
                    {
                        await inserter.BulkLoad(e.data, e.metadata, con);
                    }
                    else
                    {
                        await inserter.MergeLoad(e.data, e.metadata, requestTime, con);
                    }

                    await con.CloseAsync();
                    e.data.Dispose();
                });

                if (insertRes.IsSuccessful)
                    break;

                await Task.Delay(TimeSpan.FromMilliseconds(Settings.PipelineBackoff * Math.Pow(2, attempt)), cts.Token);
            }

            if (!insertRes.IsSuccessful)
            {
                pipelineErrors.Add(insertRes.Error!);
                Log.Error("Maximum attempt count has been reached, cancelling...");
                cts.Cancel();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        foreach (var connection in connectionPool.Values)
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }
        connectionPool.Clear();
        disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (disposed) return;
        foreach (var connection in connectionPool.Values)
        {
            connection.Close();
            connection.Dispose();
        }
        connectionPool.Clear();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}

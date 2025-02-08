using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Threading.Channels;
using Conductor.App.Database;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Shared.Config;
using Conductor.Shared.Types;

namespace Conductor.App;

public class ExtractionPipeline : IAsyncDisposable, IDisposable
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
        UInt64 destRc,
        byte offsetMultiplier,
        Channel<(DataTable, Extraction)> channel,
        CancellationToken t
    )
    {
        for (UInt64 curr = offsetMultiplier * Settings.ProducerLineMax;
            !t.IsCancellationRequested;
            curr += Settings.ConsumerConcurrentFetches * Settings.ProducerLineMax)
        {
            bool shouldPartition = destRc > 0;

            var attempt = DBExchange.SupportsMARS(e.Origin!.DbType)
                ? await fetcher.FetchDataTable(
                    e,
                    shouldPartition,
                    curr,
                    GetOrCreateConnection(e.Origin.ConnectionString, e.Origin.DbType),
                    t
                )
                : await fetcher.FetchDataTable(e, shouldPartition, curr, t);

            if (!HandleError(attempt, t)) break;
            if (attempt.Value.Rows.Count == 0) break;

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
        Func<List<Extraction>, Channel<(DataTable, Extraction)>, CancellationToken, Task> produceData,
        CancellationToken token
    )
    {
        Channel<(DataTable, Extraction)> channel = Channel.CreateUnbounded<(DataTable, Extraction)>();

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        Task producer = Task.Run(async () => await produceData(extractions, channel, cts.Token), cts.Token);
        Task consumer = Task.Run(async () => await ConsumeData(channel, cts), cts.Token);

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

    public async Task ProduceDBData(
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
            await Parallel.ForEachAsync(extractions, options, async (e, t) =>
            {
                if (t.IsCancellationRequested) return;

                var metadata = DBExchangeFactory.Create(e.Destination!.DbType);
                var con = metadata.CreateConnection(e.Destination.ConnectionString);

                await con.OpenAsync(t);

                var exists = await metadata.Exists(e, con);
                if (!HandleError(exists, t)) return;

                UInt64 destRc = 0;
                if (exists.Value)
                {
                    if (!e.IsIncremental)
                    {
                        var truncate = await metadata.TruncateTable(e, con);
                        if (!HandleError(truncate, t)) return;
                    }

                    var count = await metadata.CountTableRows(e, con);
                    if (!HandleError(count, t)) return;

                    destRc = count.Value;
                }

                await con.CloseAsync();
                await con.DisposeAsync();

                var fetcher = DBExchangeFactory.Create(e.Origin!.DbType);
                var fetchTasks = new List<Task>();

                for (byte i = 0; i < Settings.ConsumerConcurrentFetches; i++)
                {
                    byte offsetMultiplier = i;
                    fetchTasks.Add(ProcessFetchAsync(e, fetcher, destRc, offsetMultiplier, channel, t));
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

    public async Task ConsumeData(Channel<(DataTable, Extraction)> channel, CancellationTokenSource cts)
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

            for (byte attempt = 0; attempt < Settings.ConsumerAttemptMax; attempt++)
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
                        await inserter.MergeLoad(e.data, e.metadata, con);
                    }

                    await con.CloseAsync();
                    e.data.Dispose();
                });

                if (insertRes.IsSuccessful)
                    break;

                await Task.Delay(TimeSpan.FromMilliseconds(Settings.ConsumerBackoff * Math.Pow(2, attempt)), cts.Token);
            }

            if (!insertRes.IsSuccessful)
            {
                pipelineErrors.Add(insertRes.Error!);
                Log.Out("Maximum attempt count has been reached, cancelling...");
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

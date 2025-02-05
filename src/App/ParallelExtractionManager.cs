using System.Data;
using System.Data.Common;
using System.Threading.Channels;
using Conductor.App.Database;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Shared.Config;
using Conductor.Shared.Types;

namespace Conductor.App;

public static class ParallelExtractionManager
{
    public static async Task<Result> ChannelParallelize(
        List<Extraction> extractions,
        Func<List<Extraction>, Channel<(DataTable, Extraction)>, UInt16, Task> produceData,
        Func<Channel<(DataTable, Extraction)>, UInt16, Task> consumeData
    )
    {
        Channel<(DataTable, Extraction)> channel = Channel.CreateBounded<(DataTable, Extraction)>(Settings.MaxDegreeParallel);
        UInt16 errCount = 0;

        Task producer = Task.Run(async () => await produceData(extractions, channel, errCount));
        Task consumer = Task.Run(async () => await consumeData(channel, errCount));

        await Task.WhenAll(producer, consumer);

        if (errCount > 0)
        {
            if (errCount == extractions.Count)
            {
                return new Error("Extraction has failed.");
            }

            return new Error(
                "Some errors have occured in the extraction proccess.",
                partialSuccess: true
            );
        }

        return Result.Ok();
    }

    public static async Task ProduceDBData(
        List<Extraction> extractions,
        Channel<(DataTable, Extraction)> channel,
        UInt16 errCount
    )
    {
        await Parallel.ForEachAsync(extractions, Settings.ParallelRule.Value, async (e, t) =>
        {
            bool hasData = true;
            UInt64 curr = 0;
            UInt64 destRc = 0;

            var fetcher = DBExchangeFactory.Create(e.Origin!.DbType);

            if (!e.SingleExecution)
            {
                var metadata = DBExchangeFactory.Create(e.Destination!.DbType);
                if ((await metadata.Exists(e)).Value)
                {
                    if (e.BeforeExecutionDeletes) await metadata.TruncateTable(e);
                    destRc = (await metadata.CountTableRows(e)).Value;
                }
            }

            do
            {
                bool shouldPartition = destRc > 0;

                var attempt = await fetcher.FetchDataTable(e, shouldPartition, curr, t);
                if (!attempt.IsSuccessful)
                {
                    errCount++;
                    break;
                }

                if (attempt.Value.Rows.Count == 0) hasData = false;

                curr += Settings.ProducerLineMax;

                await channel.Writer.WriteAsync((attempt.Value, e), t);
            } while (hasData);
        });

        channel.Writer.Complete();
    }

    public static async Task ProduceHTTPData(
        List<Extraction> extractions,
        Channel<(DataTable, Extraction)> channel,
        UInt16 errCount
    )
    {
        await Parallel.ForEachAsync(extractions, Settings.ParallelRule.Value, async (e, t) =>
        {
            // We need a HTTP extraction factory to output a method we can apply (strategy pattern)
            // await channel.Writer.WriteAsync();
            await Task.Run(() => 1);
        });

        channel.Writer.Complete();
    }

    public static async Task ConsumeDBData(Channel<(DataTable, Extraction)> channel, UInt16 errCount)
    {
        HashSet<string> executeBulk = [];

        while (await channel.Reader.WaitToReadAsync())
        {
            List<(DataTable data, Extraction metadata)> fetchedData = [];
            byte attempt = 0;

            for (UInt16 i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (DataTable, Extraction) item); i++)
            {
                fetchedData.Add(item);
            }

            var groupedData = fetchedData
                .GroupBy(e => e.metadata)
                .Select(group =>
                {
                    using var mergedTable = Converter.MergeDataTables([.. group.Select(x => x.data)]);
                    return (data: mergedTable, metadata: group.Key);
                }).ToList();

            Result insertRes = new();

            do
            {
                attempt++;
                await Parallel.ForEachAsync(groupedData, Settings.ParallelRule.Value, async (e, t) =>
                {
                    var inserter = DBExchangeFactory.Create(e.metadata.Destination!.DbType);
                    using DbConnection con = inserter.CreateConnection(e.metadata.Destination.ConnectionString);

                    try
                    {
                        await con.OpenAsync(t);

                        UInt64 rowCount = (await inserter.Exists(e.metadata, con)).Value ? (await inserter.CountTableRows(e.metadata, con)).Value : 0;

                        await inserter.CreateTable(e.data, e.metadata, con);

                        if ((!e.metadata.IsIncremental || rowCount == 0) ^ executeBulk.Contains(e.metadata.Alias ?? e.metadata.Name))
                        {
                            executeBulk.Add(e.metadata.Alias ?? e.metadata.Name);
                            insertRes = await inserter.BulkLoad(e.data, e.metadata, con);
                        }
                        else
                        {
                            insertRes = await inserter.MergeLoad(e.data, e.metadata, con);
                        }
                    }
                    finally
                    {
                        e.data.Dispose();
                        await con.CloseAsync();
                    }
                });
            } while ((!insertRes.IsSuccessful) && attempt < Settings.ConsumerAttemptMax);
        }
    }
}
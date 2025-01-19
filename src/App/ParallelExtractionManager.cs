using System.Data;
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

            var metadataClient = DBExchangeFactory.Create(e.Destination!.DbType);
            var tableExists = await metadataClient.Exists(e);
            if (!tableExists.IsSuccessful)
            {
                errCount++;
            }

            if (tableExists.Value)
            {
                var rc = await metadataClient.CountTableRows(e);
                if (!rc.IsSuccessful)
                {
                    errCount++;
                }

                destRc = rc.Value;

                if (e.BeforeExecutionDeletes)
                {
                    await metadataClient.ClearTable(e);
                }
            }

            do
            {

                var attempt = await fetcher.FetchDataTable(e, destRc, curr, t);
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
        Result insertRes = new();
        Result createRes = new();

        while (await channel.Reader.WaitToReadAsync())
        {
            byte attempt = 0;
            List<(DataTable, Extraction)> fetchedData = [];

            for (UInt16 i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (DataTable, Extraction) item); i++)
            {
                fetchedData.Add(item);
            }

            var groupedData = fetchedData
                .GroupBy(e => e.Item2)
                .Select(group =>
                {
                    using var mergedTable = Converter.MergeDataTables([.. group.Select(x => x.Item1)]);
                    return (MergedTable: mergedTable, Extraction: group.Key);
                }).ToList();

            do
            {
                attempt++;
                await Parallel.ForEachAsync(groupedData, Settings.ParallelRule.Value, async (e, t) =>
                {
                    try
                    {
                        var inserter = DBExchangeFactory.Create(e.Extraction.Destination!.DbType);
                        createRes = await inserter.CreateTable(e.MergedTable, e.Extraction);
                        insertRes = await inserter.WriteDataTable(e.MergedTable, e.Extraction);
                    }
                    finally
                    {
                        e.MergedTable.Dispose();
                    }
                });
            } while ((!insertRes.IsSuccessful || !createRes.IsSuccessful) && attempt < Settings.ConsumerAttemptMax);

            if (attempt > Settings.ConsumerAttemptMax) errCount++;
        }
    }
}
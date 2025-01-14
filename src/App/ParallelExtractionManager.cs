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
        Func<List<Extraction>, Channel<(DataTable, Extraction)>, List<Error>, Task> produceData,
        Func<Channel<(DataTable, Extraction)>, List<Error>, Task> consumeData
    )
    {
        Channel<(DataTable, Extraction)> channel = Channel.CreateBounded<(DataTable, Extraction)>(Settings.MaxDegreeParallel);
        List<Error> errors = [];

        Task producer = Task.Run(async () => await produceData(extractions, channel, errors));
        Task consumer = Task.Run(async () => await consumeData(channel, errors));

        await Task.WhenAll(producer, consumer);

        if (errors.Count > 0)
        {
            if (errors.Count == extractions.Count)
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
        List<Error> errors
    )
    {
        await Parallel.ForEachAsync(extractions, Settings.ParallelRule.Value, async (e, t) =>
        {
            bool hasData = true;
            UInt64 curr = 0;

            var fetcher = DBExchangeFactory.Create(e.Origin!.DbType);

            do
            {
                var attempt = await fetcher.FetchDataTable(e, curr, t);
                if (!attempt.IsSuccessful)
                {
                    errors.Add(attempt.Error);
                    break;
                }

                if (attempt.Value.Rows.Count == 0) hasData = false;

                curr += Settings.ProducerLineMax;

                await channel.Writer.WriteAsync((attempt.Value, e), t);
            } while (hasData);
        });

        channel.Writer.Complete();
    }

    public static async Task ConsumeDBData(Channel<(DataTable, Extraction)> channel, List<Error> errors)
    {
        Result insertRes = new();
        Result createRes = new();

        while (await channel.Reader.WaitToReadAsync())
        {
            byte attempt = 0;
            List<(DataTable, Extraction)> fetchedData = [];

            for (Int64 i = 0; i < Settings.ConsumerFetchMax && channel.Reader.TryRead(out (DataTable, Extraction) item); i++)
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

            if (attempt > Settings.ConsumerAttemptMax)
            {
                List<Error> foundErrors = [insertRes.Error, createRes.Error];
                errors.AddRange(foundErrors);
            }
        }
    }
}
using System.Data;
using System.Security.Policy;
using Conductor.App;
using Conductor.App.Database;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Service;
using Conductor.Shared;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using Microsoft.AspNetCore.Http.HttpResults;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class ExtractionController(ExtractionService service) : ControllerBase<Extraction>(service)
{
    public override async Task<Results<Ok<Message<Extraction>>, InternalServerError<Message<Error>>, BadRequest<Message>>> Get(IQueryCollection? filters)
    {
        if (filters?.Count > Settings.MaxQueryParams)
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "Query limit has been hit.", true)
            );
        }

        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId") &&
            !UInt32.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var result = await service.Search(filters);

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to fetch data from Db.", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message<Extraction>(Status200OK, "Data fetch successful.", result.Value)
        );
    }

    public async Task<Results<Ok<Message>, BadRequest<Message>, InternalServerError<Message<Error>>, Accepted<Message>>> ExecuteExtraction(IQueryCollection? filters, CancellationToken token)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId") &&
            !UInt32.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var fetch = await service.Search(filters);

        if (!fetch.IsSuccessful)
        {
            return TypedResults.InternalServerError(ErrorMessage(
                fetch.Error.ExceptionMessage)
            );
        }

        if (fetch.Value.Any(e => e.DestinationId == null))
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "Any of the extractions used need to have a destination defined.", true)
            );
        }

        var executingId = JobTracker.Jobs.Value
            .Where(j => j.Value.Status == JobStatus.Running)
            .SelectMany(
                l => l.Value.JobExtractions.Select(
                    je => je.ExtractionId
                )
            );

        var extractions = fetch.Value.Where(
            e => !executingId.Any(l => l == e.Id)
        ).ToList();

        if (fetch.Value.Count - extractions.Count == 1)
        {
            return TypedResults.Accepted("", new Message(Status202Accepted, "The request is already running."));
        }

        var extractionIds = extractions.Select(x => x.Id);
        var job = JobTracker.StartJob(extractionIds, JobType.Transfer);

        try
        {
            extractions
                .ForEach(x =>
                {
                    x.Origin!.ConnectionString = Encryption.SymmetricDecryptAES256(x.Origin!.ConnectionString, Settings.EncryptionKey);
                    x.Destination!.ConnectionString = Encryption.SymmetricDecryptAES256(x.Destination!.ConnectionString, Settings.EncryptionKey);
                });

            await using var pipeline = new ExtractionPipeline();
            var result = await pipeline.ChannelParallelize(
                extractions,
                pipeline.ProduceDBData,
                token
            );

            if (!result.IsSuccessful)
            {
                JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                return TypedResults.InternalServerError(
                    ErrorMessage("Extraction failed.", result.Error));
            }

            JobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
            return TypedResults.Ok(new Message(Status200OK, "Extraction Successful."));
        }
        catch (Exception ex)
        {
            JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
            return TypedResults.InternalServerError(
                ErrorMessage("Extraction failed.", [new Error(ex.Message, ex.StackTrace)]));
        }
    }

    public async Task<Results<Ok<Message>, InternalServerError<Message<Error>>, Ok<Message<Dictionary<string, object>>>>> FetchData(IQueryCollection? filters, CancellationToken token)
    {
        var fetch = await service.Search(filters);

        if (!fetch.IsSuccessful)
        {
            return TypedResults.InternalServerError(ErrorMessage(
                fetch.Error.ExceptionMessage)
            );
        }

        var res = fetch.Value.FirstOrDefault();

        if (res == null)
        {
            return TypedResults.Ok(new Message(Status200OK, "No such table.", false));
        }

        res.Origin!.ConnectionString = Encryption.SymmetricDecryptAES256(
            res.Origin!.ConnectionString,
            Settings.EncryptionKey
        );

        var extractions = fetch.Value;
        var extractionIds = extractions.Select(x => x.Id);
        var job = JobTracker.StartJob(extractionIds, JobType.Fetch);

        UInt64 current = 0;

        if (UInt16.TryParse(filters?["page"] ?? "0", out UInt16 page))
        {
            current = page == 1 ? 0 : page * Settings.FetcherLineMax;
        }

        var engine = DBExchangeFactory.Create(res.Origin.DbType);
        var query = await engine.FetchDataTable(res, false, current, token, shouldPaginate: true);
        if (!query.IsSuccessful)
        {
            JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
            return TypedResults.InternalServerError(ErrorMessage(
                fetch.Error.ExceptionMessage)
            );
        }

        using var dataTable = query.Value;
        List<Dictionary<string, object>> rows = [.. dataTable.Rows.Cast<DataRow>().Select(row =>
            dataTable.Columns.Cast<DataColumn>().ToDictionary(
                col => col.ColumnName,
                col => row[col]
            )
        )];

        JobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
        return TypedResults.Ok(
            new Message<Dictionary<string, object>>(Status200OK, "Result fetch was successful.", rows, page: page == 0 ? 1 : page)
        );
    }
}
using System.Data;
using System.Threading.Channels;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Repository;
using Conductor.Service;
using Conductor.Service.Database;
using Conductor.Service.Http;
using Conductor.Shared;
using Conductor.Types;
using Google.Protobuf.WellKnownTypes;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class ExtractionController(ExtractionRepository repository, IHttpClientFactory factory) : ControllerBase<Extraction>(repository)
{
    public override async Task<IResult> Get(IQueryCollection? filters)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "take") &&
            !UInt32.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var result = await repository.Search(filters);

        var extractions = result.Value;

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.Ok(
            new Message<Extraction>(Status200OK, "OK", result.Value)
        );
    }

    public async Task<IResult> ExecuteTrasfer(IQueryCollection? filters, CancellationToken token)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "overrideTime") &&
            !UInt32.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var fetch = await repository.Search(filters);

        if (!fetch.IsSuccessful)
        {
            return Results.InternalServerError(ErrorMessage(fetch.Error));
        }

        if (fetch.Value.Any(e => e.DestinationId is null))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Any of the extractions used need to have a destination defined.", true)
            );
        }

        if (fetch.Value.Any(e => e.Origin!.ConnectionString is null || e.Origin.DbType is null))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "All used origins need to have a Connection String and DbType.", true)
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
            return Results.Accepted("", new Message(Status202Accepted, "The request is already running."));
        }

        var extractionIds = extractions.Select(x => x.Id);
        var job = JobTracker.StartJob(extractionIds, JobType.Transfer);

        Int32? overrideFilter = filters is not null && filters.ContainsKey("overrideTime") ? Int32.Parse(filters["overrideTime"]!) : null;

        try
        {
            if (extractions.Any(e => e.SourceType == "db"))
            {
                extractions.ForEach(x =>
                {
                    x.Origin!.ConnectionString = Encryption.SymmetricDecryptAES256(x.Origin!.ConnectionString!, Settings.EncryptionKey);
                    if (x.Destination is not null)
                    {
                        x.Destination!.ConnectionString = Encryption.SymmetricDecryptAES256(x.Destination!.ConnectionString, Settings.EncryptionKey);
                    }
                    x.FilterTime = overrideFilter ?? x.FilterTime;
                });
            }


            var useHttp = extractions.Any(e => (e.SourceType?.ToLowerInvariant() ?? "db") == "http");
            await using var pipeline = new ExtractionPipeline(DateTime.UtcNow, factory, overrideFilter);

            Func<List<Extraction>, Channel<(DataTable, Extraction)>, DateTime, CancellationToken, bool?, Task> producer =
                useHttp
                    ? (ex, ch, rt, ct, sc) => pipeline.ProduceHttpData(ex, ch, ct)
                    : pipeline.ProduceDBData;
            Func<Channel<(DataTable, Extraction)>, DateTime, CancellationTokenSource, Task> consumer = pipeline.ConsumeDataToDB;

            var result = await pipeline.ChannelParallelize(
                extractions,
                producer,
                consumer,
                token,
                true
            );

            if (!result.IsSuccessful)
            {
                JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                return Results.InternalServerError(
                    ErrorMessage(result.Error));
            }

            JobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
            return Results.Ok(new Message(Status200OK, "OK"));
        }
        catch (Exception ex)
        {
            JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
            return Results.InternalServerError(
                ErrorMessage([new Error(ex.Message, ex.StackTrace)]));
        }
    }

    public async Task<IResult> ExecutePull(IQueryCollection? filters, CancellationToken token)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "overrideTime") &&
            !UInt32.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var fetch = await repository.Search(filters);

        if (!fetch.IsSuccessful)
        {
            return Results.InternalServerError(ErrorMessage(fetch.Error));
        }

        if (fetch.Value.Any(e => e.Origin!.ConnectionString is null || e.Origin.DbType is null))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "All used origins need to have a Connection String and DbType.", true)
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
            return Results.Accepted("", new Message(Status202Accepted, "The request is already running."));
        }

        var extractionIds = extractions.Select(x => x.Id);
        var job = JobTracker.StartJob(extractionIds, JobType.Transfer);

        Int32? overrideFilter = filters is not null && filters.ContainsKey("overrideTime") ? Int32.Parse(filters["overrideTime"]!) : null;

        try
        {
            if (extractions.Any(e => e.SourceType == "db"))
            {
                extractions.ForEach(x =>
                {
                    x.Origin!.ConnectionString = Encryption.SymmetricDecryptAES256(x.Origin!.ConnectionString!, Settings.EncryptionKey);
                    if (x.Destination is not null)
                    {
                        x.Destination!.ConnectionString = Encryption.SymmetricDecryptAES256(x.Destination!.ConnectionString, Settings.EncryptionKey);
                    }
                    x.FilterTime = overrideFilter ?? x.FilterTime;
                });
            }
            await using var pipeline = new ExtractionPipeline(DateTime.UtcNow, factory, overrideFilter);

            var useHttp = extractions.Any(e => (e.SourceType?.ToLowerInvariant() ?? "db") == "http");
            Func<List<Extraction>, Channel<(DataTable, Extraction)>, DateTime, CancellationToken, bool?, Task> producer =
                useHttp
                    ? (ex, ch, rt, ct, sc) => pipeline.ProduceHttpData(ex, ch, ct)
                    : pipeline.ProduceDBData;
            Func<Channel<(DataTable, Extraction)>, DateTime, CancellationTokenSource, Task> consumer = pipeline.ConsumeDataToCsv;

            var result = await pipeline.ChannelParallelize(
                extractions,
                producer,
                consumer,
                token,
                false
            );

            if (!result.IsSuccessful)
            {
                JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                return Results.InternalServerError(ErrorMessage(result.Error));
            }

            JobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
            return Results.Ok(new Message(Status200OK, "OK"));
        }
        catch (Exception ex)
        {
            JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
            return Results.InternalServerError(
                ErrorMessage([new Error(ex.Message, ex.StackTrace)]));
        }
    }

    public async Task<IResult> FetchData(IQueryCollection? filters, CancellationToken token)
    {
        var fetch = await repository.Search(filters);

        if (!fetch.IsSuccessful)
        {
            return Results.InternalServerError(ErrorMessage(fetch.Error));
        }

        if (fetch.Value.Any(e => e.Origin!.ConnectionString is null || e.Origin.DbType is null))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "All used origins need to have a Connection String and DbType.", true)
            );
        }

        var res = fetch.Value.FirstOrDefault();

        if (res is null)
        {
            return Results.Ok(new Message(Status200OK, "Requested resource was not found.", false));
        }

        var extractions = fetch.Value;
        var extractionIds = extractions.Select(x => x.Id);
        var job = JobTracker.StartJob(extractionIds, JobType.Fetch);

        UInt64 current = 0;
        if (UInt16.TryParse(filters?["page"] ?? "0", out UInt16 page))
        {
            current = page == 1 ? 0 : page * Settings.FetcherLineMax;
        }

        if ((res.SourceType?.ToLowerInvariant() ?? "db") == "http")
        {
            await using var pipeline = new ExtractionPipeline(DateTime.UtcNow, factory, null);
            var (exchange, httpMethod) = HTTPExchangeFactory.Create(factory!, res.PaginationType, res.HttpMethod);

            var fetchResult = await exchange.FetchEndpointData(res, httpMethod);
            if (!fetchResult.IsSuccessful)
            {
                JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                return Results.InternalServerError(ErrorMessage(fetchResult.Error));
            }

            var dataTableResult = Converter.ProcessJsonDocument(fetchResult.Value);
            if (!dataTableResult.IsSuccessful)
            {
                JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                return Results.InternalServerError(ErrorMessage(dataTableResult.Error));
            }

            List<Dictionary<string, object>> rows = [.. dataTableResult.Value.Rows.Cast<DataRow>().Select(row =>
                dataTableResult.Value.Columns.Cast<DataColumn>().ToDictionary(
                    col => col.ColumnName,
                    col => row[col]
                )
            )];

            JobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
            return Results.Ok(new Message<Dictionary<string, object>>(Status200OK, "OK", rows, page: page == 0 ? 1 : page));
        }
        else
        {
            res.Origin!.ConnectionString = Encryption.SymmetricDecryptAES256(
                res.Origin!.ConnectionString!,
                Settings.EncryptionKey
            );
            var engine = DBExchangeFactory.Create(res.Origin!.DbType!);
            var query = await engine.FetchDataTable(res, DateTime.UtcNow, false, current, token, shouldPaginate: true);
            if (!query.IsSuccessful)
            {
                JobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                return Results.InternalServerError(ErrorMessage(fetch.Error));
            }
            using var dataTable = query.Value;
            List<Dictionary<string, object>> rows = [.. dataTable.Rows.Cast<DataRow>().Select(row =>
                dataTable.Columns.Cast<DataColumn>().ToDictionary(
                    col => col.ColumnName,
                    col => row[col]
                )
            )];
            JobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
            return Results.Ok(
                new Message<Dictionary<string, object>>(Status200OK, "OK", rows, page: page == 0 ? 1 : page)
            );
        }
    }

    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/extractions")
            .WithTags("Extractions");

        group.MapGet("/", async (ExtractionController controller, HttpRequest request) => await controller.Get(request.Query))
            .WithName("GetExtractions");

        group.MapGet("/{id}", async (ExtractionController controller, string id) => await controller.GetById(id))
            .WithName("GetExtractionById");

        group.MapPost("/", async (ExtractionController controller, HttpRequest request) => await controller.Post(request.Body))
            .Accepts<Extraction>("application/json")
            .WithName("PostExtraction");

        group.MapPut("/{id}", async (ExtractionController controller, HttpRequest request, string id) => await controller.Put(id, request.Body))
            .Accepts<Extraction>("application/json")
            .WithName("PutExtraction");

        group.MapPost("/transfer", async (ExtractionController controller, HttpRequest request, CancellationToken token) =>
        {
            return await controller.ExecuteTrasfer(request.Query, token);
        }).WithName("ExecuteTransfer");

        group.MapPost("/pull", async (ExtractionController controller, HttpRequest request, CancellationToken token) =>
        {
            return await controller.ExecutePull(request.Query, token);
        }).WithName("ExecutePull");

        group.MapGet("/fetch", async (ExtractionController controller, HttpRequest request, CancellationToken token) =>
        {
            return await controller.FetchData(request.Query, token);
        }).WithName("FetchData");

        group.MapDelete("/{id}", async (ExtractionController controller, string id) => await controller.Delete(id))
            .WithName("DeleteExtraction");

        return group;
    }
}
using System.Data;
using System.Data.Common;
using System.Threading.Channels;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Repository;
using Conductor.Service;
using Conductor.Service.Database;
using Conductor.Service.Http;
using Conductor.Shared;
using Conductor.Types;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class ExtractionController(IHttpClientFactory factory, IJobTracker tracker, IConnectionPoolManager poolManager, IDataTableMemoryManager memManager, ExtractionRepository repository) : ControllerBase<Extraction>(repository)
{
    private readonly IHttpClientFactory httpFactory = factory;
    private readonly IJobTracker jobTracker = tracker;
    private readonly IConnectionPoolManager connectionPoolManager = poolManager;
    private readonly IDataTableMemoryManager memoryManager = memManager;
    private readonly ExtractionRepository extractionRepository = repository;

    public override async Task<IResult> Get(IQueryCollection? filters)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "take" || f.Key == "originId") &&
            !uint.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var result = await extractionRepository.Search(filters);

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
            (f.Key == "scheduleId" || f.Key == "take" || f.Key == "originId") &&
            !uint.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var fetch = await extractionRepository.Search(filters);

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

        var executingId = jobTracker.GetActiveJobs()
            .Where(j => j.Status == JobStatus.Running)
            .SelectMany(
                l => l.JobExtractions.Select(
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
        var job = jobTracker.StartJob(extractionIds, JobType.Transfer);

        int? overrideFilter = filters is not null && filters.ContainsKey("overrideTime") ? int.Parse(filters["overrideTime"]!) : null;
        extractions.ForEach(x => x.FilterTime = overrideFilter ?? x.FilterTime);

        try
        {
            Helper.DecryptConnectionStrings(extractions);

            var useHttp = extractions.Any(e => (e.SourceType?.ToLowerInvariant() ?? "db") == "http");

            await using var pipeline = new ExtractionPipeline(
                DateTime.UtcNow,
                httpFactory,
                jobTracker,
                overrideFilter,
                connectionPoolManager,
                memoryManager);

            Func<List<Extraction>, Channel<(ManagedDataTable, Extraction)>, DateTime, CancellationToken, bool?, Task> producer =
                useHttp
                    ? (ex, ch, rt, ct, sc) => pipeline.ProduceHttpData(ex, ch, ct)
                    : pipeline.ProduceDBData;
            Func<Channel<(ManagedDataTable, Extraction)>, DateTime, CancellationToken, Task> consumer = pipeline.ConsumeDataToDB;

            var result = await pipeline.ChannelParallelize(
                extractions,
                producer,
                consumer,
                token,
                true
            );

            if (!result.IsSuccessful)
            {
                await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                return Results.InternalServerError(
                    ErrorMessage(result.Error));
            }

            await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
            return Results.Ok(new Message(Status200OK, "OK"));
        }
        catch (Exception ex)
        {
            await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
            return Results.InternalServerError(
                ErrorMessage([new Error(ex.Message, ex.StackTrace)]));
        }
    }

    public async Task<IResult> ExecutePull(IQueryCollection? filters, CancellationToken token)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "take" || f.Key == "originId") &&
            !uint.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var fetch = await extractionRepository.Search(filters);

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

        var executingId = jobTracker.GetActiveJobs()
            .Where(j => j.Status == JobStatus.Running)
            .SelectMany(
                l => l.JobExtractions.Select(
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
        var job = jobTracker.StartJob(extractionIds, JobType.Transfer);

        int? overrideFilter = filters is not null && filters.ContainsKey("overrideTime") ? int.Parse(filters["overrideTime"]!) : null;
        extractions.ForEach(x => x.FilterTime = overrideFilter ?? x.FilterTime);

        try
        {
            Helper.DecryptConnectionStrings(extractions);

            await using var pipeline = new ExtractionPipeline(
                DateTime.UtcNow,
                httpFactory,
                jobTracker,
                overrideFilter,
                connectionPoolManager,
                memoryManager);

            var useHttp = extractions.Any(e => (e.SourceType?.ToLowerInvariant() ?? "db") == "http");
            Func<List<Extraction>, Channel<(ManagedDataTable, Extraction)>, DateTime, CancellationToken, bool?, Task> producer =
                useHttp
                    ? (ex, ch, rt, ct, sc) => pipeline.ProduceHttpData(ex, ch, ct)
                    : pipeline.ProduceDBData;
            Func<Channel<(ManagedDataTable, Extraction)>, DateTime, CancellationToken, Task> consumer = pipeline.ConsumeDataToCsv;

            var result = await pipeline.ChannelParallelize(
                extractions,
                producer,
                consumer,
                token,
                false
            );

            if (!result.IsSuccessful)
            {
                await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                return Results.InternalServerError(ErrorMessage(result.Error));
            }

            await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
            return Results.Ok(new Message(Status200OK, "OK"));
        }
        catch (Exception ex)
        {
            await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
            return Results.InternalServerError(
                ErrorMessage([new Error(ex.Message, ex.StackTrace)]));
        }
    }

    public async Task<IResult> FetchData(IQueryCollection? filters, CancellationToken token)
    {
        var fetch = await extractionRepository.Search(filters);

        if (!fetch.IsSuccessful)
        {
            return Results.InternalServerError(ErrorMessage(fetch.Error));
        }

        if (fetch.Value.Any(e => e.Origin is null || e.Origin!.ConnectionString is null || e.Origin.DbType is null))
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
        var job = jobTracker.StartJob(extractionIds, JobType.Fetch);

        ulong currentRowCount = 0;
        if (ushort.TryParse(filters?["page"] ?? "0", out ushort page))
        {
            currentRowCount = page == 1 ? 0 : page * Settings.FetcherLineMax;
        }

        try
        {
            if ((res.SourceType?.ToLowerInvariant() ?? "db") == "http")
            {
                var (exchange, httpMethod) = HTTPExchangeFactory.Create(httpFactory!, res.PaginationType, res.HttpMethod);

                var fetchResult = await exchange.FetchEndpointData(res, httpMethod);
                if (!fetchResult.IsSuccessful)
                {
                    await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                    return Results.InternalServerError(ErrorMessage(fetchResult.Error));
                }

                var dataTableResult = Converter.ProcessJsonDocument(fetchResult.Value);
                if (!dataTableResult.IsSuccessful)
                {
                    await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                    return Results.InternalServerError(ErrorMessage(dataTableResult.Error));
                }

                Helper.GetAndSetByteUsageForExtraction(dataTableResult.Value, res.Id, jobTracker);

                List<Dictionary<string, object>> rows = [.. dataTableResult.Value.Rows.Cast<DataRow>().Select(row =>
                    dataTableResult.Value.Columns.Cast<DataColumn>().ToDictionary(
                        col => col.ColumnName,
                        col => row[col]
                    )
                )];

                dataTableResult.Value.Dispose();

                await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
                return Results.Ok(new Message<Dictionary<string, object>>(Status200OK, "OK", rows, page: page == 0 ? 1 : page));
            }
            else
            {
                string conStr = Encryption.SymmetricDecryptAES256(
                    res.Origin!.ConnectionString!,
                    Settings.EncryptionKey
                );

                var engine = DBExchangeFactory.Create(res.Origin!.DbType!);

                DbConnection? connection = null;
                try
                {
                    string finalConnectionString = DBExchange.SupportsMARS(res.Origin.DbType!)
                        ? DBExchange.EnsureMARSEnabled(res.Origin.ConnectionString!, res.Origin.DbType!)
                        : res.Origin.ConnectionString!;

                    connection = await connectionPoolManager.GetConnectionAsync(finalConnectionString, res.Origin.DbType!, token);

                    var query = await engine.FetchDataTable(res, DateTime.UtcNow, false, currentRowCount, connection, token, limit: Settings.FetcherLineMax, shouldPaginate: true);
                    if (!query.IsSuccessful)
                    {
                        await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                        return Results.InternalServerError(ErrorMessage(query.Error));
                    }

                    Helper.GetAndSetByteUsageForExtraction(query.Value, res.Id, jobTracker);

                    List<Dictionary<string, object>> rows = [.. query.Value.Rows.Cast<DataRow>().Select(row =>
                        query.Value.Columns.Cast<DataColumn>().ToDictionary(
                            col => col.ColumnName,
                            col => row[col]
                        )
                    )];

                    query.Value.Dispose();

                    await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);
                    return Results.Ok(
                        new Message<Dictionary<string, object>>(Status200OK, "OK", rows, page: page == 0 ? 1 : page)
                    );
                }
                finally
                {
                    if (connection is not null)
                    {
                        var connectionKey = $"{res.Origin.DbType}:{conStr.GetHashCode()}";
                        connectionPoolManager.ReturnConnection(connectionKey, connection);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
            return Results.InternalServerError(
                ErrorMessage(new Error(ex.Message, ex.StackTrace)));
        }
    }

    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/extractions")
            .WithTags("Extractions");

        group.MapGet("/", async (ExtractionController controller, HttpRequest request) =>
            await controller.Get(request.Query))
            .WithName("GetExtractions")
            .WithSummary("Fetches a list of extractions.")
            .WithDescription("""
                Retrieves a list of extraction records with comprehensive filtering options.
                
                Supported query parameters:
                - `name` (string): Filter by exact extraction name
                - `contains` (string): Filter by extractions containing any of the specified values (comma-separated)
                - `schedule` (string): Filter by exact schedule name
                - `scheduleId` (uint): Filter by schedule ID
                - `origin` (string): Filter by exact origin name
                - `destination` (string): Filter by exact destination name
                - `take` (uint): Limit the number of results returned
                
                Results are ordered by ID in descending order and include related Schedule, Origin, and Destination entities.
                Returns 400 if `scheduleId` or `take` parameters are not valid unsigned integers.
                """)
            .Produces<Message<Extraction>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/{id}", async (ExtractionController controller, string id) =>
            await controller.GetById(id))
            .WithName("GetExtractionById")
            .WithSummary("Fetches an extraction by ID.")
            .WithDescription("Retrieves a single extraction record by numeric ID, including related Schedule, Origin, and Destination entities.")
            .Produces<Message<Extraction>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPost("/", async (ExtractionController controller, HttpRequest request) =>
            await controller.Post(request.Body))
            .Accepts<Extraction>("application/json")
            .WithName("PostExtraction")
            .WithSummary("Creates a new extraction.")
            .WithDescription("Creates a new extraction entry from the provided JSON body.")
            .Produces<Message>(Status201Created, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPut("/{id}", async (ExtractionController controller, HttpRequest request, string id) =>
            await controller.Put(id, request.Body))
            .Accepts<Extraction>("application/json")
            .WithName("PutExtraction")
            .WithSummary("Updates an extraction.")
            .WithDescription("Updates an existing extraction identified by the given ID.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapDelete("/{id}", async (ExtractionController controller, string id) =>
            await controller.Delete(id))
            .WithName("DeleteExtraction")
            .WithSummary("Deletes an extraction.")
            .WithDescription("Deletes the extraction with the specified ID.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPost("/transfer", async (ExtractionController controller, HttpRequest request, CancellationToken token) =>
            await controller.ExecuteTrasfer(request.Query, token))
            .WithName("ExecuteTransfer")
            .WithSummary("Executes a transfer extraction job.")
            .WithDescription("""
                Starts a transfer job for one or more extractions based on the same filtering criteria as the GET endpoint.
                
                Supported query parameters for filtering:
                - `name` (string): Filter by exact extraction name
                - `contains` (string): Filter by extractions containing any of the specified values (comma-separated)
                - `schedule` (string): Filter by exact schedule name  
                - `scheduleId` (uint): Filter by schedule ID
                - `origin` (string): Filter by exact origin name
                - `destination` (string): Filter by exact destination name
                - `take` (uint): Limit the number of extractions to process
                - `overrideTime` (uint): Override the default filter time for the extraction
                
                Requirements:
                - All selected extractions must have a destination defined
                - All origins must have a valid connection string and database type
                - Automatically skips extractions that are already running
                - Connection strings are automatically decrypted during processing
                
                Returns 202 if the request is already running, 200 on successful completion.
                """)
            .Produces<Message>(Status200OK, "application/json")
            .Produces<Message>(Status202Accepted, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPost("/pull", async (ExtractionController controller, HttpRequest request, CancellationToken token) =>
            await controller.ExecutePull(request.Query, token))
            .WithName("ExecutePull")
            .WithSummary("Executes a pull extraction job.")
            .WithDescription("""
                Starts a pull job to export data to CSV from the origin system based on the same filtering criteria as the GET endpoint.
                
                Supported query parameters for filtering:
                - `name` (string): Filter by exact extraction name
                - `contains` (string): Filter by extractions containing any of the specified values (comma-separated)
                - `schedule` (string): Filter by exact schedule name
                - `scheduleId` (uint): Filter by schedule ID
                - `origin` (string): Filter by exact origin name
                - `destination` (string): Filter by exact destination name
                - `take` (uint): Limit the number of extractions to process
                - `overrideTime` (uint): Override the default filter time for the extraction
                
                Requirements:
                - All origins must have a valid connection string and database type
                - Automatically skips extractions that are already running
                - Connection strings are automatically decrypted during processing
                
                Returns 202 if the request is already running, 200 on successful completion.
                """)
            .Produces<Message>(Status200OK, "application/json")
            .Produces<Message>(Status202Accepted, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/fetch", async (ExtractionController controller, HttpRequest request, CancellationToken token) =>
            await controller.FetchData(request.Query, token))
            .WithName("FetchData")
            .WithSummary("Fetches preview data from an origin.")
            .WithDescription("""
                Fetches a preview of the data from the specified origin for extractions based on the same filtering criteria as the GET endpoint.
                
                Supported query parameters for filtering:
                - `name` (string): Filter by exact extraction name
                - `contains` (string): Filter by extractions containing any of the specified values (comma-separated)
                - `schedule` (string): Filter by exact schedule name
                - `scheduleId` (uint): Filter by schedule ID
                - `origin` (string): Filter by exact origin name
                - `destination` (string): Filter by exact destination name
                - `take` (uint): Limit the number of extractions to consider
                - `page` (uint): Page number for pagination of results
                
                Functionality:
                - Uses the first extraction found after applying filters
                - Automatically decrypts connection strings
                - Supports both HTTP and database origins
                - Returns paginated results as dictionaries with column names and values
                - Tracks byte usage for the extraction
                
                Returns 200 with data rows or a message if no resource found.
                """)
            .Produces<Message<Dictionary<string, object>>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        return group;
    }
}
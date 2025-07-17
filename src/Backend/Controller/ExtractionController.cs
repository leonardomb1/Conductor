using System.Data;
using System.Data.Common;
using System.Diagnostics;
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
            (f.Key == "scheduleId" || f.Key == "take" || f.Key == "skip" || f.Key == "originId" || f.Key == "destinationId") &&
            !uint.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var invalidBoolFilters = filters?.Where(f =>
            (f.Key == "isIncremental" || f.Key == "isVirtual") &&
            !bool.TryParse(f.Value, out _)).ToList();

        if (invalidBoolFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid boolean query parameters.", true)
            );
        }

        var sortDirection = filters?["sortDirection"].FirstOrDefault();
        if (sortDirection is not null && sortDirection != "asc" && sortDirection != "desc")
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "sortDirection must be 'asc' or 'desc'.", true)
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

    public async Task<IResult> GetCount(IQueryCollection? filters)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "originId" || f.Key == "destinationId") &&
            !uint.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var invalidBoolFilters = filters?.Where(f =>
            (f.Key == "isIncremental" || f.Key == "isVirtual") &&
            !bool.TryParse(f.Value, out _)).ToList();

        if (invalidBoolFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid boolean query parameters.", true)
            );
        }

        var result = await extractionRepository.GetCount(filters);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.Ok(
            new Message<int>(Status200OK, "OK", [result.Value])
        );
    }

    public async Task<IResult> GetNames(IQueryCollection? filters)
    {
        List<uint>? ids = null;

        if (filters?.ContainsKey("ids") == true)
        {
            var idsParam = filters["ids"].FirstOrDefault();
            if (!string.IsNullOrEmpty(idsParam))
            {
                var idStrings = idsParam.Split(',');
                ids = new List<uint>();

                foreach (var idString in idStrings)
                {
                    if (uint.TryParse(idString.Trim(), out uint id))
                    {
                        ids.Add(id);
                    }
                    else
                    {
                        return Results.BadRequest(
                            new Message(Status400BadRequest, "Invalid ID format in ids parameter.", true)
                        );
                    }
                }
            }
        }

        var result = await extractionRepository.GetNames(ids);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.Ok(
            new Message<SimpleExtractionDto>(Status200OK, "OK", result.Value)
        );
    }

    public async Task<IResult> GetDependencies(string id)
    {
        if (!uint.TryParse(id, out uint extractionId))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid extraction ID.", true)
            );
        }

        var extractionResult = await extractionRepository.Search(extractionId);
        if (!extractionResult.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(extractionResult.Error)
            );
        }

        if (extractionResult.Value is null)
        {
            return Results.NotFound(
                new Message(Status404NotFound, $"Extraction with ID {extractionId} not found.", true)
            );
        }

        var dependenciesResult = await ExtractionRepository.GetDependencies(extractionResult.Value);
        if (!dependenciesResult.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(dependenciesResult.Error)
            );
        }

        return Results.Ok(
            new Message<Extraction>(Status200OK, "OK", dependenciesResult.Value)
        );
    }

    public async Task<IResult> ExecuteTrasfer(IQueryCollection? filters, CancellationToken token)
    {
        var requestTime = DateTime.UtcNow;

        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "take" || f.Key == "skip" || f.Key == "originId" || f.Key == "destinationId") &&
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
                new Message(Status400BadRequest, "All extractions need to have a destination defined.", true)
            );
        }

        if (fetch.Value.Any(e => e.Origin?.ConnectionString is null || e.Origin?.DbType is null))
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

        if (fetch.Value.Count - extractions.Count >= 1)
        {
            return Results.Accepted("", new Message(Status202Accepted, "One or more extractions are already running."));
        }

        int? overrideFilter = filters is not null && filters.ContainsKey("overrideTime") ? int.Parse(filters["overrideTime"]!) : null;

        var extractionIds = extractions.Select(x => x.Id);
        Job job = jobTracker.StartJob(extractionIds, JobType.Transfer)!;

        var extractionResult = await ExtractionPipeline.ExecuteTransferJob(
            jobTracker,
            extractions,
            httpFactory,
            overrideFilter,
            connectionPoolManager,
            memoryManager,
            job,
            requestTime,
            token
        );

        if (!extractionResult.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage([.. extractionResult.Errors])
            );
        }

        return Results.Ok(new Message(Status200OK, "OK"));
    }

    public async Task<IResult> ExecutePull(IQueryCollection? filters, CancellationToken token)
    {
        var requestTime = DateTime.UtcNow;

        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "take" || f.Key == "skip" || f.Key == "originId" || f.Key == "destinationId") &&
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

        if (fetch.Value.Any(e => e.Origin?.ConnectionString is null || e.Origin?.DbType is null))
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

        if (fetch.Value.Count - extractions.Count >= 1)
        {
            return Results.Accepted("", new Message(Status202Accepted, "One or more extractions are already running."));
        }

        var extractionIds = extractions.Select(x => x.Id);
        Job job = jobTracker.StartJob(extractionIds, JobType.Transfer)!;

        int? overrideFilter = filters is not null && filters.ContainsKey("overrideTime") ? int.Parse(filters["overrideTime"]!) : null;

        var extractionResult = await ExtractionPipeline.ExecutePullJob(
            jobTracker,
            extractions,
            httpFactory,
            overrideFilter,
            connectionPoolManager,
            memoryManager,
            job,
            requestTime,
            token
        );

        if (!extractionResult.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage([.. extractionResult.Errors])
            );
        }

        return Results.Ok(new Message(Status200OK, "OK"));
    }

    public async Task<IResult> ExecuteTrasferNoWait(IQueryCollection? filters, CancellationToken token)
    {
        var requestTime = DateTime.UtcNow;

        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "take" || f.Key == "skip" || f.Key == "originId" || f.Key == "destinationId") &&
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
                new Message(Status400BadRequest, "All extractions need to have a destination defined.", true)
            );
        }

        if (fetch.Value.Any(e => e.Origin?.ConnectionString is null || e.Origin?.DbType is null))
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

        if (fetch.Value.Count - extractions.Count >= 1)
        {
            return Results.Accepted("", new Message(Status202Accepted, "One or more extractions are already running."));
        }

        int? overrideFilter = filters is not null && filters.ContainsKey("overrideTime") ? int.Parse(filters["overrideTime"]!) : null;

        var extractionIds = extractions.Select(x => x.Id);
        Job job = jobTracker.StartJob(extractionIds, JobType.Transfer)!;

        using var jobCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);

        var backgroundTask = Task.Run(async () => 
        {
            try
            {
                var extractionResult = await ExtractionPipeline.ExecuteTransferJob(
                    jobTracker,
                    extractions,
                    httpFactory,
                    overrideFilter,
                    connectionPoolManager,
                    memoryManager,
                    job,
                    requestTime, 
                    jobCancellationSource.Token
                );

                if (!extractionResult.IsSuccessful)
                {
                    await jobTracker.UpdateJob(job.JobGuid, JobStatus.Failed);
                }
                else
                {
                    await jobTracker.UpdateJob(job.JobGuid, JobStatus.Completed);
                }
            }
            catch (OperationCanceledException)
            {
                await jobTracker.UpdateJob(job.JobGuid, JobStatus.Cancelled);
            }
            catch (Exception)
            {
                await jobTracker.UpdateJob(job.JobGuid, JobStatus.Failed);
            }
        }, jobCancellationSource.Token);

        jobTracker.AttachTask(job.JobGuid, backgroundTask, jobCancellationSource);

        return Results.Ok(
            new Message(Status200OK, $"{job.JobGuid}")
        );
    }

    public async Task<IResult> ExecutePullNoWait(IQueryCollection? filters, CancellationToken token)
    {
        var requestTime = DateTime.UtcNow;

        var invalidFilters = filters?.Where(f =>
            (f.Key == "scheduleId" || f.Key == "take" || f.Key == "skip" || f.Key == "originId" || f.Key == "destinationId") &&
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

        if (fetch.Value.Any(e => e.Origin?.ConnectionString is null || e.Origin?.DbType is null))
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

        if (fetch.Value.Count - extractions.Count >= 1)
        {
            return Results.Accepted("", new Message(Status202Accepted, "One or more extractions are already running."));
        }

        var extractionIds = extractions.Select(x => x.Id);
        Job job = jobTracker.StartJob(extractionIds, JobType.Transfer)!;

        int? overrideFilter = filters is not null && filters.ContainsKey("overrideTime") ? int.Parse(filters["overrideTime"]!) : null;
        using var jobCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);

        var backgroundTask = Task.Run(async () => 
        {
            try
            {
                var extractionResult = await ExtractionPipeline.ExecutePullJob(
                    jobTracker,
                    extractions,
                    httpFactory,
                    overrideFilter,
                    connectionPoolManager,
                    memoryManager,
                    job,
                    requestTime,
                    jobCancellationSource.Token
                );

                if (!extractionResult.IsSuccessful)
                {
                    await jobTracker.UpdateJob(job.JobGuid, JobStatus.Failed);
                }
                else
                {
                    await jobTracker.UpdateJob(job.JobGuid, JobStatus.Completed);
                }
            }
            catch (OperationCanceledException)
            {
                await jobTracker.UpdateJob(job.JobGuid, JobStatus.Cancelled);
            }
            catch (Exception)
            {
                await jobTracker.UpdateJob(job.JobGuid, JobStatus.Failed);
            }
        }, jobCancellationSource.Token);

        jobTracker.AttachTask(job.JobGuid, backgroundTask, jobCancellationSource);

        return Results.Ok(
            new Message(Status200OK, $"{job.JobGuid}")
        );
    }

    public async Task<IResult> FetchData(IQueryCollection? filters, CancellationToken token)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "limit") &&
            !ulong.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters. 'limit' must be a valid positive number.", true)
            );
        }

        string? limitString = filters?.Where(f => f.Key == "limit").FirstOrDefault().Value;
        ulong limit;
        
        if (string.IsNullOrEmpty(limitString))
        {
            limit = Settings.FetcherLineMax; 
        }
        else
        {
            if (!ulong.TryParse(limitString, out limit))
            {
                limit = Settings.FetcherLineMax;
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var requestTime = DateTime.UtcNow;

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
            return Results.Ok(Message<Dictionary<string, object>>.FetchNotFound());
        }

        var extractions = fetch.Value;
        var extractionIds = extractions.Select(x => x.Id);
        var job = jobTracker.StartJob(extractionIds, JobType.Fetch);

        var nestingConfig = JsonNestingConfig.FromQueryParameters(filters);
        bool enableNesting = !bool.Parse(filters?["disableNesting"].FirstOrDefault() ?? "false");

        ulong currentRowCount = 0;
        if (ushort.TryParse(filters?["page"] ?? "0", out ushort page))
        {
            currentRowCount = page == 1 ? 0 : page * Settings.FetcherLineMax;
        }

        try
        {
            DataTable dataTable;

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

                dataTable = dataTableResult.Value;
            }
            else
            {
                Helper.DecryptConnectionStrings(fetch.Value);

                var engine = DBExchangeFactory.Create(res.Origin!.DbType!);

                var query = await engine.FetchDataTable(
                    extraction: res,
                    requestTime: requestTime,
                    shouldPartition: false,
                    currentRowCount: currentRowCount,
                    connectionPoolManager: connectionPoolManager,
                    token: token,
                    overrideFilter: null,
                    limit: limit,
                    shouldPaginate: true
                );

                if (!query.IsSuccessful)
                {
                    await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                    return Results.InternalServerError(ErrorMessage(query.Error));
                }

                dataTable = query.Value;
            }

            Helper.GetAndSetByteUsageForExtraction(dataTable, res.Id, jobTracker);
            stopwatch.Stop();

            List<Dictionary<string, object>> result;
            List<string>? nestedProperties = null;

            if (enableNesting)
            {
                var nestedResult = Converter.ProcessDataTableToNestedJson(dataTable, nestingConfig);
                if (!nestedResult.IsSuccessful)
                {
                    await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Failed);
                    return Results.InternalServerError(ErrorMessage(nestedResult.Error));
                }

                result = nestedResult.Value;
                nestedProperties = [.. result
                    .SelectMany(row => row.Keys)
                    .Where(nestingConfig.ShouldNestProperty)
                    .Distinct()];
            }
            else
            {
                result = [.. dataTable.Rows.Cast<DataRow>().Select(row =>
                    dataTable.Columns.Cast<DataColumn>().ToDictionary(
                        col => col.ColumnName,
                        col => row[col]
                    )
                )];
            }

            var metadata = new FetchMetadata
            {
                ExtractionName = res.Name,
                ExtractionId = res.Id,
                RequestTime = requestTime,
                ProcessingTime = stopwatch.Elapsed,
                DataSizeBytes = Helper.CalculateBytesUsed(dataTable),
                NestedProperties = nestedProperties
            };

            dataTable.Dispose();
            await jobTracker.UpdateJob(job!.JobGuid, JobStatus.Completed);

            return Results.Ok(Message<Dictionary<string, object>>.FetchSuccess(
                result,
                page: page == 0 ? 1 : page,
                hasNestedData: enableNesting,
                metadata: metadata
            ));
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
                Retrieves a list of extraction records with comprehensive filtering and sorting options.
                
                Supported query parameters:
                - `name` (string): Filter by exact extraction name
                - `contains` (string): Filter by extractions containing any of the specified values
                - `schedule` (string): Filter by exact schedule name
                - `scheduleId` (uint): Filter by schedule ID
                - `originId` (uint): Filter by origin ID
                - `destinationId` (uint): Filter by destination ID
                - `origin` (string): Filter by exact origin name
                - `destination` (string): Filter by exact destination name
                - `sourceType` (string): Filter by source type
                - `isIncremental` (bool): Filter by incremental flag
                - `isVirtual` (bool): Filter by virtual flag
                - `search` (string): Search across name, alias, and index name
                - `skip` (uint): Number of records to skip for pagination
                - `take` (uint): Limit the number of results returned
                - `sortBy` (string): Sort by field (name, sourcetype, origin, destination, schedule, isincremental, or id)
                - `sortDirection` (string): Sort direction (asc or desc)
                
                Results include related Schedule, Origin, and Destination entities.
                Returns 400 if numeric or boolean parameters are invalid.
                """)
            .Produces<Message<Extraction>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/count", async (ExtractionController controller, HttpRequest request) =>
            await controller.GetCount(request.Query))
            .WithName("GetExtractionsCount")
            .WithSummary("Gets the count of extractions matching the filter criteria.")
            .WithDescription("""
                Returns the total count of extractions that match the specified filter criteria.
                
                Supported query parameters (same as GET /extractions but without skip, take, sortBy, sortDirection):
                - `name` (string): Filter by exact extraction name
                - `contains` (string): Filter by extractions containing any of the specified values
                - `scheduleId` (uint): Filter by schedule ID
                - `originId` (uint): Filter by origin ID
                - `destinationId` (uint): Filter by destination ID
                - `sourceType` (string): Filter by source type
                - `isIncremental` (bool): Filter by incremental flag
                - `isVirtual` (bool): Filter by virtual flag
                - `search` (string): Search across name, alias, and index name
                """)
            .Produces<Message<int>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/names", async (ExtractionController controller, HttpRequest request) =>
            await controller.GetNames(request.Query))
            .WithName("GetExtractionNames")
            .WithSummary("Gets simple extraction name/ID pairs.")
            .WithDescription("""
                Returns a list of simple extraction DTOs containing only ID and Name.
                
                Supported query parameters:
                - `ids` (string): Comma-separated list of extraction IDs to filter by
                
                If no IDs are provided, returns all extraction names.
                """)
            .Produces<Message<SimpleExtractionDto>>(Status200OK, "application/json")
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

        group.MapGet("/{id}/dependencies", async (ExtractionController controller, string id) =>
            await controller.GetDependencies(id))
            .WithName("GetExtractionDependencies")
            .WithSummary("Gets the dependencies for an extraction.")
            .WithDescription("Retrieves all extractions that the specified extraction depends on, with connection strings decrypted.")
            .Produces<Message<Extraction>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message>(Status404NotFound, "application/json")
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
                - `contains` (string): Filter by extractions containing any of the specified values
                - `schedule` (string): Filter by exact schedule name  
                - `scheduleId` (uint): Filter by schedule ID
                - `originId` (uint): Filter by origin ID
                - `destinationId` (uint): Filter by destination ID
                - `origin` (string): Filter by exact origin name
                - `destination` (string): Filter by exact destination name
                - `sourceType` (string): Filter by source type
                - `isIncremental` (bool): Filter by incremental flag
                - `isVirtual` (bool): Filter by virtual flag
                - `search` (string): Search across name, alias, and index name
                - `skip` (uint): Number of records to skip
                - `take` (uint): Limit the number of extractions to process
                - `overrideTime` (uint): Override the default filter time for the extraction
                
                Requirements:
                - All selected extractions must have a destination defined
                - All origins must have a valid connection string and database type
                - Automatically skips extractions that are already running
                - Connection strings are automatically decrypted during processing
                
                Returns 202 if extractions are already running, 200 on successful completion.
                """)
            .Produces<Message>(Status200OK, "application/json")
            .Produces<Message>(Status202Accepted, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPut("/programTransfer", async (ExtractionController controller, HttpRequest request, CancellationToken token) =>
            await controller.ExecuteTrasferNoWait(request.Query, token))
            .WithName("ExecuteProgramedTransfer")
            .WithSummary("Executes a a no waited transfer extraction job.")
            .WithDescription("""
                Starts a transfer job for one or more extractions based on the same filtering criteria as the GET endpoint.
                
                Supported query parameters for filtering:
                - `name` (string): Filter by exact extraction name
                - `contains` (string): Filter by extractions containing any of the specified values
                - `schedule` (string): Filter by exact schedule name  
                - `scheduleId` (uint): Filter by schedule ID
                - `originId` (uint): Filter by origin ID
                - `destinationId` (uint): Filter by destination ID
                - `origin` (string): Filter by exact origin name
                - `destination` (string): Filter by exact destination name
                - `sourceType` (string): Filter by source type
                - `isIncremental` (bool): Filter by incremental flag
                - `isVirtual` (bool): Filter by virtual flag
                - `search` (string): Search across name, alias, and index name
                - `skip` (uint): Number of records to skip
                - `take` (uint): Limit the number of extractions to process
                - `overrideTime` (uint): Override the default filter time for the extraction
                
                Requirements:
                - All selected extractions must have a destination defined
                - All origins must have a valid connection string and database type
                - Automatically skips extractions that are already running
                - Connection strings are automatically decrypted during processing
                
                Returns 202 if extractions are already running, 200 on successful completion.
                """)
            .Produces<Message>(Status200OK, "application/json")
            .Produces<Message>(Status202Accepted, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPost("/programPull", async (ExtractionController controller, HttpRequest request, CancellationToken token) =>
            await controller.ExecutePullNoWait(request.Query, token))
            .WithName("ExecuteProgramedPull")
            .WithSummary("Executes a no waited pull extraction job.")
            .WithDescription("""
                Starts a pull job to export data to CSV from the origin system based on the same filtering criteria as the GET endpoint.
                
                Supported query parameters for filtering:
                - `name` (string): Filter by exact extraction name
                - `contains` (string): Filter by extractions containing any of the specified values
                - `schedule` (string): Filter by exact schedule name
                - `scheduleId` (uint): Filter by schedule ID
                - `originId` (uint): Filter by origin ID
                - `destinationId` (uint): Filter by destination ID
                - `origin` (string): Filter by exact origin name
                - `destination` (string): Filter by exact destination name
                - `sourceType` (string): Filter by source type
                - `isIncremental` (bool): Filter by incremental flag
                - `isVirtual` (bool): Filter by virtual flag
                - `search` (string): Search across name, alias, and index name
                - `skip` (uint): Number of records to skip
                - `take` (uint): Limit the number of extractions to process
                - `overrideTime` (uint): Override the default filter time for the extraction
                
                Requirements:
                - All origins must have a valid connection string and database type
                - Automatically skips extractions that are already running
                - Connection strings are automatically decrypted during processing
                
                Returns 202 if extractions are already running, 200 on successful completion.
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
                - `contains` (string): Filter by extractions containing any of the specified values
                - `schedule` (string): Filter by exact schedule name
                - `scheduleId` (uint): Filter by schedule ID
                - `originId` (uint): Filter by origin ID
                - `destinationId` (uint): Filter by destination ID
                - `origin` (string): Filter by exact origin name
                - `destination` (string): Filter by exact destination name
                - `sourceType` (string): Filter by source type
                - `isIncremental` (bool): Filter by incremental flag
                - `isVirtual` (bool): Filter by virtual flag
                - `search` (string): Search across name, alias, and index name
                - `skip` (uint): Number of records to skip
                - `take` (uint): Limit the number of extractions to process
                - `overrideTime` (uint): Override the default filter time for the extraction
                
                Requirements:
                - All origins must have a valid connection string and database type
                - Automatically skips extractions that are already running
                - Connection strings are automatically decrypted during processing
                
                Returns 202 if extractions are already running, 200 on successful completion.
                """)
            .Produces<Message>(Status200OK, "application/json")
            .Produces<Message>(Status202Accepted, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/fetch", async (ExtractionController controller, HttpRequest request, CancellationToken token) =>
    await controller.FetchData(request.Query, token))
    .WithName("FetchData")
    .WithSummary("Fetches preview data from an origin with intelligent JSON nesting.")
    .WithDescription("""
        Fetches a preview of the data from the specified origin for extractions with automatic JSON parsing and nesting capabilities.
        
        **Extraction Filtering Parameters:**
        - `name` (string): Filter by exact extraction name
        - `contains` (string): Filter by extractions containing any of the specified values
        - `schedule` (string): Filter by exact schedule name
        - `scheduleId` (uint): Filter by schedule ID
        - `originId` (uint): Filter by origin ID
        - `destinationId` (uint): Filter by destination ID
        - `origin` (string): Filter by exact origin name
        - `destination` (string): Filter by exact destination name
        - `sourceType` (string): Filter by source type (db, http)
        - `isIncremental` (bool): Filter by incremental flag
        - `isVirtual` (bool): Filter by virtual flag
        - `search` (string): Search across name, alias, and index name
        
        **Pagination Parameters:**
        - `page` (uint): Page number for pagination (default: 1)
        - `skip` (uint): Number of records to skip
        - `take` (uint): Limit the number of extractions to process
        
        **Data Processing Parameters:**
        - `overrideTime` (uint): Override the default filter time for the extraction
        - `disableNesting` (bool): Disable automatic JSON nesting (default: false)
        
        **JSON Nesting Configuration:**
        - `nestProperties` (string): Comma-separated list of properties to nest (e.g., "addresses,storekeeper,items")
        - `nestPatterns` (string): Comma-separated regex patterns for auto-nesting (e.g., ".*List$,.*Array$")
        - `excludeProperties` (string): Comma-separated list of properties to exclude from nesting
        
        **Default Nesting Behavior:**
        The endpoint automatically detects and nests JSON strings in properties named:
        - addresses, storekeeper, items, details
        - Properties ending with "List" or "Array"
        - Any property containing valid JSON array or object strings
        
        **Response Format:**
        Returns an enhanced response with metadata including processing time, data size, and nesting information.
        
        **Requirements:**
        - All origins must have a valid connection string and database type
        - Connection strings are automatically decrypted during processing
        - Supports both database and HTTP-based extractions
        
        **Examples:**
        - Basic fetch: `/fetch?name=warehouse_data&page=1`
        - Custom nesting: `/fetch?name=warehouse_data&nestProperties=addresses,storekeeper`
        - Disable nesting: `/fetch?name=warehouse_data&disableNesting=true`
        - Pattern-based: `/fetch?name=warehouse_data&nestPatterns=.*List$`
        """)
        .Produces<Message<Dictionary<string, object>>>(Status200OK, "application/json")
        .Produces<Message>(Status400BadRequest, "application/json")
        .Produces<Message<Error>>(Status500InternalServerError, "application/json")
        .WithOpenApi(operation =>
        {
            operation.Parameters.Add(new Microsoft.OpenApi.Models.OpenApiParameter
            {
                Name = "name",
                In = Microsoft.OpenApi.Models.ParameterLocation.Query,
                Description = "Filter by exact extraction name",
                Required = false,
                Schema = new Microsoft.OpenApi.Models.OpenApiSchema { Type = "string" }
            });

            operation.Parameters.Add(new Microsoft.OpenApi.Models.OpenApiParameter
            {
                Name = "page",
                In = Microsoft.OpenApi.Models.ParameterLocation.Query,
                Description = "Page number for pagination",
                Required = false,
                Schema = new Microsoft.OpenApi.Models.OpenApiSchema { Type = "integer", Minimum = 1, Default = new Microsoft.OpenApi.Any.OpenApiInteger(1) }
            });

            operation.Parameters.Add(new Microsoft.OpenApi.Models.OpenApiParameter
            {
                Name = "disableNesting",
                In = Microsoft.OpenApi.Models.ParameterLocation.Query,
                Description = "Disable automatic JSON nesting",
                Required = false,
                Schema = new Microsoft.OpenApi.Models.OpenApiSchema { Type = "boolean", Default = new Microsoft.OpenApi.Any.OpenApiBoolean(false) }
            });

            operation.Parameters.Add(new Microsoft.OpenApi.Models.OpenApiParameter
            {
                Name = "nestProperties",
                In = Microsoft.OpenApi.Models.ParameterLocation.Query,
                Description = "Comma-separated list of properties to nest (e.g., 'addresses,storekeeper')",
                Required = false,
                Schema = new Microsoft.OpenApi.Models.OpenApiSchema { Type = "string" },
                Example = new Microsoft.OpenApi.Any.OpenApiString("addresses,storekeeper,items")
            });

            operation.Parameters.Add(new Microsoft.OpenApi.Models.OpenApiParameter
            {
                Name = "nestPatterns",
                In = Microsoft.OpenApi.Models.ParameterLocation.Query,
                Description = "Comma-separated regex patterns for auto-nesting (e.g., '.*List$,.*Array$')",
                Required = false,
                Schema = new Microsoft.OpenApi.Models.OpenApiSchema { Type = "string" },
                Example = new Microsoft.OpenApi.Any.OpenApiString(".*List$,.*Array$")
            });

            operation.Parameters.Add(new Microsoft.OpenApi.Models.OpenApiParameter
            {
                Name = "excludeProperties",
                In = Microsoft.OpenApi.Models.ParameterLocation.Query,
                Description = "Comma-separated list of properties to exclude from nesting",
                Required = false,
                Schema = new Microsoft.OpenApi.Models.OpenApiSchema { Type = "string" }
            });

            return operation;
        });

        return group;
    }
}
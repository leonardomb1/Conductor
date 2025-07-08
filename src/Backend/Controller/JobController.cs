using Conductor.Logging;
using Conductor.Model;
using Conductor.Repository;
using Conductor.Types;
using Microsoft.IdentityModel.Tokens;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class JobController(JobRepository jobRepository, JobExtractionRepository jobExtractionRepository, ExtractionRepository extractionRepository, IJobTracker jobTracker) : ControllerBase<Job>(jobRepository)
{
    public async Task<IResult> GetJobs(IQueryCollection? filters)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "relativeStart" || f.Key == "relativeEnd" || f.Key == "take" || f.Key == "mbs") &&
                (
                    !int.TryParse(f.Value, out _) ||
                    !uint.TryParse(f.Value, out _) ||
                    !float.TryParse(f.Value, out _)
                )
            ).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var result = await jobRepository.SearchJob(filters);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.Ok(
            new Message<JobDto>(Status200OK, "OK", result.Value)
        );
    }

    public async Task<IResult> GetAggreggatedView(IQueryCollection? filters)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "relativeStart" || f.Key == "relativeEnd" || f.Key == "take" || f.Key == "mbs") &&
                (
                    !int.TryParse(f.Value, out _) ||
                    !uint.TryParse(f.Value, out _) ||
                    !float.TryParse(f.Value, out _)
                )
            ).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var result = await jobRepository.GetExtractionAggregatedView(filters);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.Ok(
            new Message<ExtractionAggregatedDto>(Status200OK, "OK", result.Value)
        );
    }

    public async Task<IResult> GetActiveJobs()
    {
        try
        {
            if (jobTracker.GetActiveJobs().IsNullOrEmpty())
            {
                return Results.Ok(
                    new Message(Status200OK, "No active jobs.")
                );
            }

            var activeJobs = jobTracker.GetActiveJobs();

            var extractionIds = activeJobs
                .SelectMany(j => j.JobExtractions.Select(je => je.ExtractionId))
                .Distinct()
                .ToList();

            var extractions = await extractionRepository.GetNames(extractionIds);
            if (!extractions.IsSuccessful)
            {
                return Results.InternalServerError(
                    ErrorMessage(extractions.Error)
                );
            }

            var extractionDict = extractions.Value.ToDictionary(e => e.Id, e => e.Name);

            var result = activeJobs
                .SelectMany(job => job.JobExtractions
                    .Where(je => extractionDict.ContainsKey(je.ExtractionId))
                    .Select(je => new JobDto(
                        extractionDict[je.ExtractionId],
                        job.JobGuid,
                        job.JobType.ToString(),
                        job.Status.ToString(),
                        job.StartTime,
                        job.EndTime,
                        ((job.EndTime ?? DateTime.Now) - job.StartTime).TotalMilliseconds,
                        job.JobExtractions.Sum(x => x.BytesAccumulated) / (1024f * 1024f)
                    ))
                )
                .OrderByDescending(j => j.StartTime)
                .ToList();

            return Results.Ok(
                new Message<JobDto>(Status200OK, "OK", result)
            );
        }

        catch (Exception ex)
        {
            return Results.InternalServerError(
                ErrorMessage(new Error(ex.Message, ex.StackTrace))
            );
        }
    }

    public async Task<IResult> Clear()
    {
        var relatedTable = await jobExtractionRepository.Clear();
        if (!relatedTable.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(relatedTable.Error)
            );
        }

        var mainTable = await jobRepository.Clear();
        if (!mainTable.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(mainTable.Error)
            );
        }

        return Results.NoContent();
    }

    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/jobs")
            .WithTags("Extractions");

        group.MapGet("/active", (JobController controller) => controller.GetActiveJobs())
            .WithName("GetActiveJobs")
            .WithSummary("Retrieves active in-memory jobs.")
            .WithDescription("""
                Returns currently running in-memory jobs, including their associated extraction names, durations, status, and accumulated data.
                If no jobs are active, returns an empty response message.
                """)
            .Produces<Message<JobDto>>(Status200OK, "application/json")
            .Produces<Message>(Status200OK, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/search", (JobController controller, HttpRequest request) => controller.GetJobs(request.Query))
            .WithName("SearchJobs")
            .WithSummary("Searches past jobs using query parameters.")
            .WithDescription("""
                Retrieves a filtered list of past job records.
                Accepts the following optional query parameters:
                - `relativeStart` (int)
                - `relativeEnd` (int)
                - `take` (uint)
                - `mbs` (float)

                If any of these parameters are present with invalid formats, a 400 Bad Request is returned.
                """)
            .Produces<Message<JobDto>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/total", (JobController controller, HttpRequest request) => controller.GetAggreggatedView(request.Query))
            .WithName("GetAggregatedJobs")
            .WithSummary("Gets an aggreagted view of past jobs using query parameters.")
            .WithDescription("""
                Retrieves a filtered aggregated view of job records.
                Accepts the following optional query parameters:
                - `relativeStart` (int)
                - `relativeEnd` (int)
                - `take` (uint)
                - `mbs` (float)

                If any of these parameters are present with invalid formats, a 400 Bad Request is returned.
                """)
            .Produces<Message<ExtractionAggregatedDto>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapDelete("/", async (JobController controller, HttpRequest request) => await controller.Clear())
            .WithName("ClearJobs")
            .WithSummary("Clears all job and related job-extraction records.")
            .WithDescription("""
                Deletes all jobs from both the primary jobs table and related job extraction records.
                Useful for resetting the system's job state in bulk.
                """)
            .Produces(Status204NoContent)
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        return group;
    }
}
using Conductor.Model;
using Conductor.Repository;
using Conductor.Types;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class JobController(JobRepository jobRepository, JobExtractionRepository jobExtractionRepository) : ControllerBase<Job>(jobRepository)
{
    public async Task<IResult> GetJobs(IQueryCollection? filters)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "relativeStart" || f.Key == "relativeEnd" || f.Key == "take" || f.Key == "mbs") &&
                (
                    !Int32.TryParse(f.Value, out _) ||
                    !UInt32.TryParse(f.Value, out _) ||
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

    public async Task<IResult> GetActiveJobs()
    {
        var activeJobs = await jobRepository.GetActiveJobs();

        if (!activeJobs.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(activeJobs.Error)
            );
        }

        if (activeJobs.Value is null || activeJobs.Value.Count == 0)
        {
            return Results.Ok(
                new Message(Status200OK, "No active jobs.")
            );
        }

        return Results.Ok(
            new Message<JobDto>(Status200OK, "OK", activeJobs.Value)
        );
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
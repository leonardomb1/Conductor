using Conductor.Logging;
using Conductor.Model;
using Conductor.Repository;
using Conductor.Types;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class JobController(JobRepository jobRepository, JobExtractionRepository jobExtractionRepository, ExtractionRepository extractionRepository) : ControllerBase<Job>(jobRepository)
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
        if (JobTracker.Jobs.Value.IsEmpty)
        {
            return Results.Ok(
                new Message(Status200OK, "No active jobs.")
            );
        }

        var extractionFetch = await extractionRepository.GetNames();
        if (!extractionFetch.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(extractionFetch.Error)
            );
        }

        var activeJobs = JobTracker.GetActiveJobs();
        var jobsWithExtractions = activeJobs
        .SelectMany(j => j.JobExtractions.Select(je => new { Job = j, je.ExtractionId }));

        var result = jobsWithExtractions
            .Join(extractionFetch.Value,
                je => je.ExtractionId,
                e => e.Id,
                (je, e) => new JobDto(
                    e.Name,
                    je.Job.JobGuid,
                    je.Job.JobType.ToString(),
                    je.Job.Status.ToString(),
                    je.Job.StartTime,
                    je.Job.EndTime,
                    ((je.Job.EndTime ?? DateTime.Now) - je.Job.StartTime)!.TotalMilliseconds,
                    je.Job.BytesAccumulated
                )
            )
            .OrderByDescending(j => j.StartTime)
            .ToList();

        return Results.Ok(
            new Message<JobDto>(Status200OK, "OK", result)
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
            .WithName("GetActiveJobs");

        group.MapGet("/search", (JobController controller, HttpRequest request) => controller.GetJobs(request.Query))
            .WithName("SearchJobs");

        group.MapDelete("/", async (JobController controller, HttpRequest request) => await controller.Clear())
            .WithName("ClearJobs");

        return group;
    }
}
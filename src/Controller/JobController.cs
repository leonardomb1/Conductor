using Conductor.Logging;
using Conductor.Model;
using Conductor.Service;
using Conductor.Shared.Types;
using Microsoft.AspNetCore.Http.HttpResults;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class JobController(JobService jobService, JobExtractionService jobExtractionService, ExtractionService extractionService) : ControllerBase<Job>(jobService)
{
    public async Task<Results<Ok<Message<JobDto>>, InternalServerError<Message<Error>>, BadRequest<Message>>> GetJobs(IQueryCollection? filters)
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
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var result = await jobService.SearchJob(filters);

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to fetch data from Db.", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message<JobDto>(Status200OK, "Data fetch successful.", result.Value)
        );
    }

    public async Task<Results<Ok<Message<JobDto>>, Ok<Message>, InternalServerError<Message<Error>>>> GetActiveJobs()
    {
        if (JobTracker.Jobs.Value.IsEmpty)
        {
            return TypedResults.Ok(
                new Message(Status200OK, "No active jobs.")
            );
        }

        var extractionFetch = await extractionService.GetNames();
        if (!extractionFetch.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to fetch data from Db.", extractionFetch.Error)
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
                    je.Job.BytesAccumulated / 1_000_000f
                )
            )
            .OrderByDescending(j => j.StartTime)
            .ToList();

        return TypedResults.Ok(
            new Message<JobDto>(Status200OK, "Active jobs retrieved.", result)
        );
    }

    public async Task<Results<Ok<Message>, InternalServerError<Message<Error>>>> Clear()
    {
        var relatedTable = await jobExtractionService.Clear();
        if (!relatedTable.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to clear data from Db.", relatedTable.Error)
            );
        }

        var mainTable = await jobService.Clear();
        if (!mainTable.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to clear data from Db.", mainTable.Error)
            );
        }
        return TypedResults.Ok(
            new Message(Status200OK, "Table has been cleared."));
    }
}
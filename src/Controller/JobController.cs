using System.Threading.Tasks;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Service;
using Conductor.Shared.Types;
using Microsoft.AspNetCore.Http.HttpResults;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class JobController(JobService jobService, JobExtractionService jobExtractionService, ExtractionService extractionService) : ControllerBase<Job>(jobService)
{
    public async Task<Results<Ok<Message<object>>, InternalServerError<Message<Error>>, BadRequest<Message>>> GetJobs(IQueryCollection? filters)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "relative" || f.Key == "take" || f.Key == "extractionId") &&
                (
                    !Int32.TryParse(f.Value, out _) ||
                    !UInt32.TryParse(f.Value, out _)
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
            new Message<object>(Status200OK, "Data fetch successful.", result.Value)
        );
    }

    public async Task<Results<Ok<Message<object>>, InternalServerError<Message<Error>>>> GetActiveJobs()
    {
        var extractionFetch = await extractionService.GetNames();
        if (!extractionFetch.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to fetch data from Db.", extractionFetch.Error)
            );
        }

        List<object> extractions = extractionFetch.Value;
        IEnumerable<Job> activeJobs = JobTracker.GetActiveJobs();

        var typedExtractions = extractions
            .Select(e => (dynamic)e)
            .ToList();

        var jobWithExtractions = activeJobs
            .SelectMany(j => j.JobExtractions.Select(je => new { Job = j, je.ExtractionId }));

        var linq = from je in jobWithExtractions
                   join e in typedExtractions on je.ExtractionId equals (UInt32)e.Id
                   orderby je.Job.StartTime descending
                   select new
                   {
                       e.Name,
                       je.Job.JobGuid,
                       JobType = $"{je.Job.JobType}",
                       Status = $"{je.Job.Status}",
                       je.Job.StartTime,
                       je.Job.EndTime,
                       TimeSpentSec = (je.Job.EndTime - je.Job.StartTime)!.Value.TotalSeconds,
                       TotalMbTransfered = je.Job.BytesAccumulated > 0 ? (float)je.Job.BytesAccumulated / 1_000_000 : 0
                   };

        return TypedResults.Ok(
            new Message<object>(Status200OK, "Active jobs retrieved.", linq.Cast<object>().ToList()));
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
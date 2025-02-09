using System.Threading.Tasks;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Service;
using Conductor.Shared.Types;
using Microsoft.AspNetCore.Http.HttpResults;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class JobController(JobService service, JobExtractionService relatedService) : ControllerBase<Job>(service)
{
    public async Task<Results<Ok<Message<Job>>, InternalServerError<Message<Error>>, BadRequest<Message>>> GetJobs(IQueryCollection? filters)
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

        var result = await service.Search(filters);

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to fetch data from Db.", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message<Job>(Status200OK, "Data fetch successful.", result.Value)
        );
    }

    public Results<Ok<Message<Job>>, BadRequest<Message>> GetActiveJobs()
    {
        var activeJobs = JobTracker.GetActiveJobs().ToList();
        return TypedResults.Ok(
            new Message<Job>(Status200OK, "Active jobs retrieved.", activeJobs));
    }

    public async Task<Results<Ok<Message>, InternalServerError<Message<Error>>>> Clear()
    {
        var relatedTable = await relatedService.Clear();
        if (!relatedTable.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to clear data from Db.", relatedTable.Error)
            );
        }

        var mainTable = await service.Clear();
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
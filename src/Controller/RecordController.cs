using Conductor.Model;
using Conductor.Service;
using Conductor.Shared.Types;
using Microsoft.AspNetCore.Http.HttpResults;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class RecordController(RecordService service) : ControllerBase<Record>(service)
{
    public override async Task<Results<Ok<Message<Record>>, InternalServerError<Message<Error>>, BadRequest<Message>>> Get(IQueryCollection? filters)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "relative" || f.Key == "take") &&
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
            new Message<Record>(Status200OK, "Data fetch successful.", result.Value)
        );
    }

    public async Task<Results<Ok<Message>, InternalServerError<Message<Error>>>> GetCount()
    {
        var result = await service.Count();

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to count result set.", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message(Status200OK, $"Current result set count is {result.Value}.")
        );
    }

    public async Task<Results<Ok<Message>, InternalServerError<Message<Error>>>> Clear()
    {
        var result = await service.Clear();

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to clear table.", result.Error)
            );
        }

        return TypedResults.Ok(new Message(Status200OK, "Table has been cleared."));
    }
}
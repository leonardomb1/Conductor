using Conductor.Model;
using Conductor.Repository;
using Conductor.Types;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class ScheduleController(ScheduleRepository repository) : ControllerBase<Schedule>(repository)
{
    public override async Task<IResult> Get(IQueryCollection? filters)
    {
        var invalidFilters = filters?.Where(f =>
            (f.Key == "value") &&
            !Int32.TryParse(f.Value, out _)).ToList();

        if (invalidFilters?.Count > 0)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "Invalid query parameters.", true)
            );
        }

        var result = await repository.Search(filters);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.Ok(
            new Message<Schedule>(Status200OK, "OK", result.Value)
        );
    }

    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/schedules")
            .WithTags("Schedules");

        group.MapGet("/", async (ScheduleController controller, HttpRequest request) => await controller.Get(request.Query))
            .WithName("GetSchedules");

        group.MapGet("/{id}", async (ScheduleController controller, string id) => await controller.GetById(id))
            .WithName("GetScheduleById");

        group.MapPost("/", async (ScheduleController controller, HttpRequest request) => await controller.Post(request.Body))
            .Accepts<Schedule>("application/json")
            .WithName("PostSchedule");

        group.MapPut("/{id}", async (ScheduleController controller, HttpRequest request, string id) => await controller.Put(id, request.Body))
            .Accepts<Schedule>("application/json")
            .WithName("PutSchedule");

        group.MapDelete("/{id}", async (ScheduleController controller, string id) => await controller.Delete(id))
            .WithName("DeleteSchedule");

        return group;
    }
}
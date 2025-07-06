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

        group.MapGet("/", async (ScheduleController controller, HttpRequest request) =>
            await controller.Get(request.Query))
            .WithName("GetSchedules")
            .WithSummary("Fetches a list of schedules.")
            .WithDescription("""
                Retrieves a list of schedules filtered by optional query parameters.
                For example, `value=<int>` is a valid filter. Invalid filters will result in a 400 Bad Request.
                """)
            .Produces<Message<Schedule>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/{id}", async (ScheduleController controller, string id) =>
            await controller.GetById(id))
            .WithName("GetScheduleById")
            .WithSummary("Fetches a schedule by ID.")
            .WithDescription("Returns a schedule by its numeric ID. If no schedule is found, returns a 200 OK with not-found message.")
            .Produces<Message<Schedule>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPost("/", async (ScheduleController controller, HttpRequest request) =>
            await controller.Post(request.Body))
            .WithName("PostSchedule")
            .Accepts<Schedule>("application/json")
            .WithSummary("Creates a new schedule.")
            .WithDescription("Creates a schedule from a JSON body. Returns 201 Created with the URI of the new schedule.")
            .Produces<Message>(Status201Created, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPut("/{id}", async (ScheduleController controller, HttpRequest request, string id) =>
            await controller.Put(id, request.Body))
            .WithName("PutSchedule")
            .Accepts<Schedule>("application/json")
            .WithSummary("Updates an existing schedule.")
            .WithDescription("Updates a schedule identified by the ID with the provided JSON. Returns 204 No Content on success.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapDelete("/{id}", async (ScheduleController controller, string id) =>
            await controller.Delete(id))
            .WithName("DeleteSchedule")
            .WithSummary("Deletes a schedule.")
            .WithDescription("Deletes a schedule identified by the given ID. Returns 204 No Content if successful.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        return group;
    }
}
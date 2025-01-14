using Conductor.Controller;
using Conductor.Model;

namespace Conductor.Router;

public static class ScheduleRoute
{
    public static RouteGroupBuilder Add(RouteGroupBuilder api)
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
using Conductor.Model;
using Conductor.Repository;

namespace Conductor.Controller;

public sealed class OriginController(OriginRepository repository) : ControllerBase<Origin>(repository)
{
    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/origins")
            .WithTags("Origins");

        group.MapGet("/", async (OriginController controller, HttpRequest request) => await controller.Get(request.Query))
            .WithName("GetOrigins");

        group.MapGet("/{id}", async (OriginController controller, string id) => await controller.GetById(id))
            .WithName("GetOriginById");

        group.MapPost("/", async (OriginController controller, HttpRequest request) => await controller.Post(request.Body))
            .Accepts<Origin>("application/json")
            .WithName("PostOrigin");

        group.MapPut("/{id}", async (OriginController controller, HttpRequest request, string id) => await controller.Put(id, request.Body))
            .Accepts<Origin>("application/json")
            .WithName("PutOrigin");

        group.MapDelete("/{id}", async (OriginController controller, string id) => await controller.Delete(id))
            .WithName("DeleteOrigin");

        return group;
    }
}
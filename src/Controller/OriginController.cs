using Conductor.Model;
using Conductor.Repository;
using Conductor.Types;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class OriginController(OriginRepository repository) : ControllerBase<Origin>(repository)
{
    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/origins")
            .WithTags("Origins");

        group.MapGet("/", async (OriginController controller, HttpRequest request) =>
            await controller.Get(request.Query))
            .WithName("GetOrigins")
            .WithSummary("Fetches a list of origins.")
            .WithDescription("Returns a list of origins based on optional query parameters. If no parameters are provided, all available origins may be returned.")
            .Produces<Message<Origin>>(Status200OK, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/{id}", async (OriginController controller, string id) =>
            await controller.GetById(id))
            .WithName("GetOriginById")
            .WithSummary("Fetches an origin by ID.")
            .WithDescription("Returns the origin associated with the specified numeric ID. If the ID is invalid or no origin is found, an appropriate message is returned.")
            .Produces<Message<Origin>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPost("/", async (OriginController controller, HttpRequest request) =>
            await controller.Post(request.Body))
            .WithName("PostOrigin")
            .Accepts<Origin>("application/json")
            .WithSummary("Creates a new origin.")
            .WithDescription("Creates a new origin using the provided JSON body. On success, returns the URI of the newly created resource.")
            .Produces<Message>(Status201Created, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPut("/{id}", async (OriginController controller, HttpRequest request, string id) =>
            await controller.Put(id, request.Body))
            .WithName("PutOrigin")
            .Accepts<Origin>("application/json")
            .WithSummary("Updates an existing origin.")
            .WithDescription("Updates an origin identified by the specified ID using the provided JSON body. Returns 204 No Content if the update is successful.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapDelete("/{id}", async (OriginController controller, string id) =>
            await controller.Delete(id))
            .WithName("DeleteOrigin")
            .WithSummary("Deletes an origin.")
            .WithDescription("Deletes the origin associated with the specified ID. Returns 204 No Content on success.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        return group;
    }
}
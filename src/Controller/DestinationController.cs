using Conductor.Model;
using Conductor.Repository;
using Conductor.Types;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class DestinationController(DestinationRepository repository) : ControllerBase<Destination>(repository)
{
    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/destinations")
            .WithTags("Destinations");

        group.MapGet("/", async (DestinationController controller, HttpRequest request) =>
            await controller.Get(request.Query))
            .WithName("GetDestinations")
            .WithSummary("Fetches a list of destinations.")
            .WithDescription("Returns a paginated or filtered list of destinations based on optional query parameters. If no filters are provided, all records may be returned.")
            .Produces<Message<Destination>>(Status200OK, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/{id}", async (DestinationController controller, string id) =>
            await controller.GetById(id))
            .WithName("GetDestinationById")
            .WithSummary("Fetches a destination by ID.")
            .WithDescription("Returns a destination for a given numeric ID. If the destination does not exist, returns a 200 OK with a not-found message. If ID is not a number, returns 400.")
            .Produces<Message<Destination>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPost("/", async (DestinationController controller, HttpRequest request) =>
            await controller.Post(request.Body))
            .WithName("PostDestination")
            .Accepts<Destination>("application/json")
            .WithSummary("Creates a new destination.")
            .WithDescription("Creates a new destination entity based on the provided JSON body. On success, returns the URI of the new resource.")
            .Produces<Message>(Status201Created, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPut("/{id}", async (DestinationController controller, HttpRequest request, string id) =>
            await controller.Put(id, request.Body))
            .WithName("PutDestination")
            .Accepts<Destination>("application/json")
            .WithSummary("Updates an existing destination.")
            .WithDescription("Updates the destination identified by the given ID. Returns 204 No Content on success. Returns 400 if the ID is invalid or JSON is malformed.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapDelete("/{id}", async (DestinationController controller, string id) =>
            await controller.Delete(id))
            .WithName("DeleteDestination")
            .WithSummary("Deletes a destination.")
            .WithDescription("Deletes the destination record identified by the given ID. Returns 204 No Content if successful.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        return group;
    }

}
using Conductor.Model;
using Conductor.Repository;

namespace Conductor.Controller;

public sealed class DestinationController(DestinationRepository repository) : ControllerBase<Destination>(repository)
{
    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/destinations")
            .WithTags("Destinations");

        group.MapGet("/", async (DestinationController controller, HttpRequest request) => await controller.Get(request.Query))
            .WithName("GetDestinations")
            .WithSummary("Fetches a list of destinations")
            .WithDescription("Returns a list of destinations based on the provided query parameters. If no results are found, it will return an empty list.");

        group.MapGet("/{id}", async (DestinationController controller, string id) => await controller.GetById(id))
            .WithName("GetDestinationById")
            .WithSummary("Fetches a destination by ID")
            .WithDescription("Returns a destinations for a given ID. If no destination is found, a 404 Not Found will be returned.");

        group.MapPost("/", async (DestinationController controller, HttpRequest request) => await controller.Post(request.Body))
            .WithName("PostDestination")
            .Accepts<Destination>("application/json")
            .WithSummary("Create a new destination")
            .WithDescription("Creates a new destination based on the provided JSON body.");

        group.MapPut("/{id}", async (DestinationController controller, HttpRequest request, string id) => await controller.Put(id, request.Body))
            .WithName("PutDestination")
            .Accepts<Destination>("application/json")
            .WithSummary("Update a destination")
            .WithDescription("Updates an existing destination identified by the provided ID.");

        group.MapDelete("/{id}", async (DestinationController controller, string id) => await controller.Delete(id))
            .WithName("DeleteDestination")
            .WithSummary("Delete a destination")
            .WithDescription("Deletes a destination identified by the provided ID.");

        return group;
    }
}
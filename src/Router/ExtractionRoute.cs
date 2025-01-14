using Conductor.Controller;
using Conductor.Model;

namespace Conductor.Router;

public static class ExtractionRoute
{
    public static RouteGroupBuilder Add(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/extractions")
            .WithTags("Extractions");

        group.MapGet("/", async (ExtractionController controller, HttpRequest request) => await controller.Get(request.Query))
            .WithName("GetExtractions");

        group.MapGet("/{id}", async (ExtractionController controller, string id) => await controller.GetById(id))
            .WithName("GetExtractionById");

        group.MapPost("/", async (ExtractionController controller, HttpRequest request) => await controller.Post(request.Body))
            .Accepts<Origin>("application/json")
            .WithName("PostExtraction");

        group.MapPut("/{id}", async (ExtractionController controller, HttpRequest request, string id) => await controller.Put(id, request.Body))
            .Accepts<Origin>("application/json")
            .WithName("PutExtraction");

        group.MapPost("/", async (ExtractionController controller, HttpRequest request) => await controller.ExecuteExtraction(request.Query))
            .WithName("ExecuteExtraction");

        group.MapDelete("/{id}", async (ExtractionController controller, string id) => await controller.Delete(id))
            .WithName("DeleteExtraction");

        return group;
    }
}
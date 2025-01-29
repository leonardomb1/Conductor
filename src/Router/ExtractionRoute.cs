using Conductor.App;
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
            .Accepts<Extraction>("application/json")
            .WithName("PostExtraction");

        group.MapPut("/{id}", async (ExtractionController controller, HttpRequest request, string id) => await controller.Put(id, request.Body))
            .Accepts<Extraction>("application/json")
            .WithName("PutExtraction");

        group.MapPost("/execute", async (ExtractionController controller, HttpRequest request) => await controller.ExecuteExtraction(request.Query))
            .WithName("ExecuteExtraction");

        group.MapDelete("/{id}", async (ExtractionController controller, string id) => await controller.Delete(id))
            .WithName("DeleteExtraction");

        group.MapDelete("/physical/{id}", async (ExtractionController controller, string id) => await controller.DropPhysicalTable(id))
            .WithName("DropPhysicalTable");

        return group;
    }
}
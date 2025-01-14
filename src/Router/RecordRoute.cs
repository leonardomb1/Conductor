using Conductor.Controller;

namespace Conductor.Router;

public static class RecordRoute
{
    public static RouteGroupBuilder Add(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/records")
            .WithTags("Records");

        group.MapGet("/", async (RecordController controller, HttpRequest request) => await controller.Get(request.Query))
            .WithName("GetRecords");

        group.MapGet("/{id}", async (RecordController controller, string id) => await controller.GetById(id))
            .WithName("GetRecordsById");

        group.MapGet("/count", async (RecordController controller) => await controller.GetCount())
            .WithName("GetRecordCount");

        group.MapDelete("/", async (RecordController controller) => await controller.Clear())
            .WithName("ClearRecords");

        return group;
    }
}
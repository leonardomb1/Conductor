using Conductor.Controller;

namespace Conductor.Router;

public static class JobRoute
{
    public static RouteGroupBuilder Add(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/jobs")
            .WithTags("Extractions");

        group.MapGet("/active", (JobController controller) => controller.GetActiveJobs())
            .WithName("GetActiveJobs");

        group.MapGet("/search", (JobController controller, HttpRequest request) => controller.Get(request.Query))
            .WithName("SearchJobs");

        group.MapDelete("/", async (JobController controller, HttpRequest request) => await controller.Clear())
            .WithName("ClearJobs");

        return group;
    }
}
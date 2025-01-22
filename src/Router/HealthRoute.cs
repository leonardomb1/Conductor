namespace Conductor.Router;

public static class HealthRoute
{
    public static RouteGroupBuilder Add(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/health").WithTags("Health");

        group.MapGet("/", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.Now })).WithName("HealthCheck");

        return group;
    }
}
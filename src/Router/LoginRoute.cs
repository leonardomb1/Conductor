using Conductor.Controller;
using Conductor.Model;

namespace Conductor.Router;

public static class LoginRoute
{
    public static RouteGroupBuilder Add(RouteGroupBuilder api)
    {
        var group = api;
        group.MapPost("/login", async (UserController controller, HttpContext ctx) => await controller.Login(ctx.Request.Body, GetClientIp(ctx)))
            .Accepts<User>("application/json")
            .WithName("LoginRoute")
            .WithDescription("Login using local db for authentication.")
            .WithTags("Login");

        group.MapPost("/ssologin", (UserController controller, HttpContext ctx) => controller.LoginWithLdap(ctx.Request.Body, GetClientIp(ctx)))
            .Accepts<User>("application/json")
            .WithName("LoginRouteWithLdap")
            .WithDescription("Login using LDAP provider for authentication.")
            .WithTags("Login");

        return group;
    }

    private static string GetClientIp(HttpContext ctx)
    {
        if (!ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            return ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        }
        return forwarded.ToString();
    }
}
using Conductor.Logging;
using Conductor.Shared.Config;
using static System.Net.HttpStatusCode;

namespace Conductor.Middleware;

public sealed class WhiteListMiddleware(RequestDelegate req)
{
    private readonly RequestDelegate next = req;

    public async Task InvokeAsync(HttpContext ctx)
    {
        string client;
        if (!ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            client = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
        }
        else
        {
            client = forwarded.ToString();
        }

        if (!Settings.HttpAllowedIpsSet.Value.Contains(client!.ToString()))
        {
            Log.Out($"Blocking Request for {ctx.Request.Path} by {client}.", callerMethod: "Server");
            ctx.Response.StatusCode = (Int32)Forbidden;
            await ctx.Response.WriteAsync("Access denied.");
            return;
        }

        await next(ctx);
    }
}
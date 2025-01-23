using System.Net;
using Conductor.Logging;
using Conductor.Shared.Config;
using static System.Net.HttpStatusCode;

namespace Conductor.Middleware;

public sealed class WhiteListMiddleware(RequestDelegate req)
{
    private readonly RequestDelegate next = req;

    public async Task InvokeAsync(HttpContext ctx)
    {
        IPAddress client = ctx.Connection.RemoteIpAddress!;

        if (ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedString) &&
           !string.IsNullOrEmpty(forwardedString))
        {
            if (IPAddress.TryParse(forwardedString, out var forwardedIp))
            {
                client = forwardedIp;
            }
        }

        if (!Settings.AllowedIpsRange.Value.Contains(client))
        {
            Log.Out($"Blocking Request for {ctx.Request.Path} by {client}.", callerMethod: "Server");
            ctx.Response.StatusCode = (Int32)Forbidden;
            await ctx.Response.WriteAsync("Access denied.");
            return;
        }

        await next(ctx);
    }
}
using System.Net;
using Conductor.Shared;

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

        Helper.VerifyIpAddress(client, ctx);

        await next(ctx);
    }
}
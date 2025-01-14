using System.Diagnostics;
using Conductor.Logging;
using Conductor.Shared;

namespace Conductor.Middleware;

public class LoggingMiddleware(RequestDelegate req)
{
    private readonly RequestDelegate next = req;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var stopwatch = Stopwatch.StartNew();
        string client;
        if (ctx.Request.Headers.TryGetValue("X-ForwardedBy", out var forwardedBy))
        {
            client = forwardedBy.ToString();
        }
        else
        {
            client = ctx.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
        }

        Log.Out(
            $"{ctx.Request.Protocol} {ctx.Request.Method} - Received request for resource {ctx.Request.Path}, from {client}.",
            Constants.MessageRequest,
            callerMethod: "Server"
        );

        await next(ctx);

        stopwatch.Stop();

        string statusCode = ctx.Response.StatusCode switch
        {
            >= 200 and < 300 => $"{Constants.BOLD}{Constants.GREEN}{ctx.Response.StatusCode}{Constants.NORMAL}{Constants.NOBOLD}",
            >= 400 and < 500 => $"{Constants.BOLD}{Constants.YELLOW}{ctx.Response.StatusCode}{Constants.NORMAL}{Constants.NOBOLD}",
            >= 500 and < 600 => $"{Constants.BOLD}{Constants.RED}{ctx.Response.StatusCode}{Constants.NORMAL}{Constants.NOBOLD}",
            _ => $"{Constants.BOLD}{Constants.GREY}{ctx.Response.StatusCode}{Constants.NORMAL}{Constants.NOBOLD}"
        };

        string raw = $"{ctx.Response.StatusCode} - Request for {ctx.Request.Path} by {client} was processed in {stopwatch.ElapsedMilliseconds} ms.";

        Log.Out(
            $"{statusCode} - Request for {ctx.Request.Path} by {client} was processed in {stopwatch.ElapsedMilliseconds} ms.",
            statusCode.StartsWith('5') ? Constants.MessageError : Constants.MessageInfo,
            callerMethod: "Server",
            raw: raw
        );
    }
}
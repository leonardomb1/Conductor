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
            client = ctx.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";
        }

        Log.Out(
            $"{ctx.Request.Protocol} {ctx.Request.Method} - Received request for resource {ctx.Request.Path}, from {client}.",
            RecordType.Request,
            callerMethod: "Server"
        );

        await next(ctx);

        stopwatch.Stop();

        string statusCode = ctx.Response.StatusCode switch
        {
            >= 200 and < 300 => $"{Colors.BOLD}{Colors.GREEN}{ctx.Response.StatusCode}{Colors.NORMAL}{Colors.NOBOLD}",
            >= 400 and < 500 => $"{Colors.BOLD}{Colors.YELLOW}{ctx.Response.StatusCode}{Colors.NORMAL}{Colors.NOBOLD}",
            >= 500 and < 600 => $"{Colors.BOLD}{Colors.RED}{ctx.Response.StatusCode}{Colors.NORMAL}{Colors.NOBOLD}",
            _ => $"{Colors.BOLD}{Colors.GREY}{ctx.Response.StatusCode}{Colors.NORMAL}{Colors.NOBOLD}"
        };

        string raw = $"{ctx.Response.StatusCode} - Request for {ctx.Request.Path} by {client} was processed in {stopwatch.ElapsedMilliseconds} ms.";

        Log.Out(
            $"{statusCode} - Request for {ctx.Request.Path} by {client} was processed in {stopwatch.ElapsedMilliseconds} ms.",
            statusCode.StartsWith('5') ? RecordType.Error : RecordType.Info,
            callerMethod: "Server",
            raw: raw
        );
    }
}
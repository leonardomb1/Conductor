using System.Diagnostics;
using Conductor.Shared;

namespace Conductor.Middleware;

public sealed class DistributionMiddleware(RequestDelegate request)
{
    private readonly RequestDelegate next = request;
    private static TimeSpan lastCpuTime = TimeSpan.Zero;
    private static DateTime lastCheck = DateTime.UtcNow;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!Settings.IsMasterNode)
        {
            await next(ctx);
            return;
        }

        string path = ctx.Request.Path.ToString();

        if (Settings.NodeType == Types.Node.MasterCluster && path.Contains("transfer", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect("/extractions/distribute");
            return;
        }

        double cpuUsage = GetCpuUsagePercent();

        if (path.Contains("transfer", StringComparison.OrdinalIgnoreCase)
            && cpuUsage > Settings.MasterNodeCpuRedirectPercentage)
        {
            ctx.Response.Redirect("/extractions/distribute");
            return;
        }

        await next(ctx);
    }

    private static double GetCpuUsagePercent()
    {
        var process = Process.GetCurrentProcess();
        var nowCpuTime = process.TotalProcessorTime;
        var now = DateTime.UtcNow;

        var cpuUsedMs = (nowCpuTime - lastCpuTime).TotalMilliseconds;
        var totalMsPassed = (now - lastCheck).TotalMilliseconds * Environment.ProcessorCount;

        lastCpuTime = nowCpuTime;
        lastCheck = now;

        if (totalMsPassed <= 0)
            return 0;

        return cpuUsedMs / totalMsPassed * 100.0;
    }
}

using System.Diagnostics;
using Conductor.Shared;

namespace Conductor.Middleware;

public sealed class DistributionMiddleware(RequestDelegate request)
{
    private readonly RequestDelegate next = request;
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

        double cpuUsage = CpuUsage.GetCpuUsagePercent();

        if (path.Contains("transfer", StringComparison.OrdinalIgnoreCase)
            && cpuUsage > Settings.MasterNodeCpuRedirectPercentage)
        {
            ctx.Response.Redirect("/extractions/distribute");
            return;
        }

        await next(ctx);
    }
}

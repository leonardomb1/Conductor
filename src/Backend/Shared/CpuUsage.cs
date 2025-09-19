using System.Diagnostics;

namespace Conductor.Shared;

public static class CpuUsage
{
    private static TimeSpan lastCpuTime = TimeSpan.Zero;
    private static DateTime lastCheck = DateTime.UtcNow;
    private static readonly Lock lockObject = new();

    public static double GetCpuUsagePercent()
    {
        lock (lockObject)
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
}
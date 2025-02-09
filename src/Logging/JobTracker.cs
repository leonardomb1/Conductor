using System.Collections.Concurrent;
using Conductor.Model;
using Conductor.Service;

namespace Conductor.Logging;

public static class JobTracker
{
    public static readonly Lazy<ConcurrentDictionary<Guid, Job>> Jobs = new();

    public static Job? StartJob(IEnumerable<UInt32> extractionIds)
    {
        var job = new Job { ExtractionIds = [.. extractionIds] };
        return Jobs.Value.TryAdd(job.JobGuid, job) ? job : null;
    }

    public static void UpdateJob(Guid JobGuid, JobStatus status)
    {
        if (Jobs.Value.TryGetValue(JobGuid, out var job))
        {
            job.Status = status;
            job.EndTime = DateTime.UtcNow;
        }
    }

    public static Job? GetJobByExtractionId(UInt32 extractionId)
    {
        return Jobs.Value.Values.FirstOrDefault(job =>
            job.ExtractionIds.Contains(extractionId) &&
            job.Status == JobStatus.Running);
    }

    public static void UpdateTransferedBytes(Guid JobGuid, Int64 bytes)
    {
        if (Jobs.Value.TryGetValue(JobGuid, out var job))
        {
            job.AddTransferedBytes(bytes);
        }
    }

    public static IEnumerable<Job> GetActiveJobs() =>
        Jobs.Value.Values.Where(j => j.Status == JobStatus.Running);

    public static async Task DumpJobs(JobService service)
    {
        if (Jobs.Value.IsEmpty) return;

        var completedJobs = Jobs.Value.Values.Where(x => x.Status != JobStatus.Running).ToList();
        if (completedJobs.Count != 0)
        {
            await service.CreateBulk(completedJobs);
            foreach (var job in completedJobs)
            {
                Jobs.Value.TryRemove(job.JobGuid, out _);
            }
        }
    }
}
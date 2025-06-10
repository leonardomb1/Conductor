using System.Collections.Concurrent;
using Conductor.Model;
using Conductor.Repository;

namespace Conductor.Logging;

public static class JobTracker
{
    public static readonly Lazy<ConcurrentDictionary<Guid, Job>> Jobs = new();

    public static Job? StartJob(IEnumerable<UInt32> extractionIds, JobType jobType)
    {
        var job = new Job()
        {
            JobType = jobType
        };

        job.JobExtractions = [.. extractionIds.Select(id => new JobExtraction
        {
            JobGuid = job.JobGuid,
            ExtractionId = id
        })];

        return Jobs.Value.TryAdd(job.JobGuid, job) ? job : null;
    }

    public static void UpdateJob(Guid JobGuid, JobStatus status)
    {
        if (Jobs.Value.TryGetValue(JobGuid, out var job))
        {
            job.Status = status;
            job.EndTime = DateTime.Now;
        }
    }

    public static Job? GetJobByExtractionId(UInt32 extractionId)
    {
        return Jobs.Value.Values.FirstOrDefault(job =>
            job.JobExtractions.Any(je => je.ExtractionId == extractionId) &&
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

    public static async Task DumpJobs(JobRepository jobRepository, JobExtractionRepository related)
    {
        if (Jobs.Value.IsEmpty) return;

        var completedJobs = Jobs.Value.Values.Where(x => x.Status != JobStatus.Running).ToList();
        if (completedJobs.Count != 0)
        {
            await jobRepository.CreateBulk(completedJobs);
            foreach (var job in completedJobs)
            {
                await related.CreateBulk(job.JobExtractions);
                Jobs.Value.TryRemove(job.JobGuid, out _);
            }
        }
    }
}

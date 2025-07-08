using System.Collections.Concurrent;
using Conductor.Model;
using Conductor.Repository;
using Serilog;

namespace Conductor.Logging;

public interface IJobTracker
{
    Job? StartJob(IEnumerable<uint> extractionIds, JobType jobType);
    Task UpdateJob(Guid jobGuid, JobStatus status);
    Job? GetJobByExtractionId(uint extractionId);
    void UpdateTransferedBytes(uint extractionId, long bytes);
    IEnumerable<Job> GetActiveJobs();
}

public class JobTracker(IServiceScopeFactory serviceScopeFactory) : IJobTracker
{
    private readonly ConcurrentDictionary<Guid, Job> jobs = new();

    public Job? StartJob(IEnumerable<uint> extractionIds, JobType jobType)
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
        return jobs.TryAdd(job.JobGuid, job) ? job : null;
    }

    public async Task UpdateJob(Guid jobGuid, JobStatus status)
    {
        if (jobs.TryGetValue(jobGuid, out var job))
        {
            job.Status = status;
            job.EndTime = DateTimeOffset.UtcNow;

            if (status == JobStatus.Completed || status == JobStatus.Failed)
            {
                await DumpSingleJob(job);
            }
        }
    }

    private async Task DumpSingleJob(Job job)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<JobRepository>();
        var relatedRepository = scope.ServiceProvider.GetRequiredService<JobExtractionRepository>();

        try
        {
            await repository.CreateJob(job);

            jobs.TryRemove(job.JobGuid, out _);
            Log.Debug("Dumped job {JobGuid} to database", job.JobGuid);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to dump job {JobGuid} to database", job.JobGuid);
        }
    }

    public Job? GetJobByExtractionId(uint extractionId)
    {
        return jobs.Values.FirstOrDefault(job =>
            job.Status == JobStatus.Running &&
            job.JobExtractions.Any(je => je.ExtractionId == extractionId));
    }

    public void UpdateTransferedBytes(uint extractionId, long bytes)
    {
        var job = GetJobByExtractionId(extractionId);
        if (job is null) return;
        var jobExtraction = job.JobExtractions.FirstOrDefault(je => je.ExtractionId == extractionId);
        if (jobExtraction is null) return;
        jobExtraction.AddTransferedBytes(bytes);
    }

    public IEnumerable<Job> GetActiveJobs() =>
        jobs.Values.Where(j => j.Status == JobStatus.Running);
}
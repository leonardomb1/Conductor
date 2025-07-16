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
    void AttachTask(Guid jobGuid, Task task, CancellationTokenSource cancellationTokenSource);
    Task<bool> CancelJob(Guid jobGuid);
    JobTaskInfo? GetJobTaskInfo(Guid jobGuid);
    IEnumerable<JobTaskInfo> GetActiveJobTasks();
}

public record JobTaskInfo(
    Guid JobGuid,
    JobType JobType,
    JobStatus Status,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    Task? AttachedTask,
    bool CanBeCancelled,
    bool IsCancellationRequested
);

public class JobTracker(IServiceScopeFactory serviceScopeFactory) : IJobTracker
{
    private readonly ConcurrentDictionary<Guid, Job> jobs = new();
    private readonly ConcurrentDictionary<Guid, (Task task, CancellationTokenSource cancellationSource)> jobTasks = new();

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

            if (status == JobStatus.Completed || status == JobStatus.Failed || status == JobStatus.Cancelled)
            {
                await DumpSingleJob(job);
                
                if (jobTasks.TryRemove(jobGuid, out var taskInfo))
                {
                    try
                    {
                        taskInfo.cancellationSource?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error disposing cancellation token source for job {JobGuid}", jobGuid);
                    }
                }
            }
        }
    }

    public void AttachTask(Guid jobGuid, Task task, CancellationTokenSource cancellationTokenSource)
    {
        if (jobs.ContainsKey(jobGuid))
        {
            jobTasks.TryAdd(jobGuid, (task, cancellationTokenSource));
            Log.Debug("Attached task to job {JobGuid} for cancellation tracking", jobGuid);
        }
        else
        {
            Log.Warning("Attempted to attach task to non-existent job {JobGuid}", jobGuid);
        }
    }

    public async Task<bool> CancelJob(Guid jobGuid)
    {
        if (!jobTasks.TryGetValue(jobGuid, out var taskInfo))
        {
            Log.Warning("Cannot cancel job {JobGuid}: no associated task found", jobGuid);
            return false;
        }

        if (!jobs.TryGetValue(jobGuid, out var job))
        {
            Log.Warning("Cannot cancel job {JobGuid}: job not found", jobGuid);
            return false;
        }

        if (job.Status != JobStatus.Running)
        {
            Log.Information("Job {JobGuid} is not running (status: {Status}), cannot cancel", jobGuid, job.Status);
            return false;
        }

        try
        {
            Log.Information("Cancelling job {JobGuid} of type {JobType}", jobGuid, job.JobType);
            
            taskInfo.cancellationSource.Cancel();
            
            job.Status = JobStatus.Cancelled;
            
            var cancellationTimeout = TimeSpan.FromSeconds(30);
            var timeoutTask = Task.Delay(cancellationTimeout);
            var completedTask = await Task.WhenAny(taskInfo.task, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                Log.Warning("Job {JobGuid} did not respond to cancellation within {Timeout} seconds", 
                    jobGuid, cancellationTimeout.TotalSeconds);
            }
            else
            {
                Log.Information("Job {JobGuid} responded to cancellation request", jobGuid);
            }

            await UpdateJob(jobGuid, JobStatus.Cancelled);
            
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cancelling job {JobGuid}", jobGuid);
            return false;
        }
    }

    public JobTaskInfo? GetJobTaskInfo(Guid jobGuid)
    {
        if (!jobs.TryGetValue(jobGuid, out var job))
            return null;

        var hasTask = jobTasks.TryGetValue(jobGuid, out var taskInfo);
        
        return new JobTaskInfo(
            JobGuid: job.JobGuid,
            JobType: job.JobType,
            Status: job.Status,
            StartTime: job.StartTime,
            EndTime: job.EndTime,
            AttachedTask: hasTask ? taskInfo.task : null,
            CanBeCancelled: hasTask && job.Status == JobStatus.Running && !taskInfo.cancellationSource.Token.IsCancellationRequested,
            IsCancellationRequested: hasTask && taskInfo.cancellationSource.Token.IsCancellationRequested
        );
    }

    public IEnumerable<JobTaskInfo> GetActiveJobTasks()
    {
        return jobs.Values
            .Where(j => j.Status == JobStatus.Running)
            .Select(j => GetJobTaskInfo(j.JobGuid))
            .Where(info => info is not null)
            .Cast<JobTaskInfo>();
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
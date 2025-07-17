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
    Job? StartBackgroundJob(IEnumerable<uint> extractionIds, JobType jobType,
        Func<Job, CancellationToken, Task> jobAction);
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

    public Job? StartBackgroundJob(IEnumerable<uint> extractionIds, JobType jobType,
        Func<Job, CancellationToken, Task> jobAction)
    {
        var job = StartJob(extractionIds, jobType);
        if (job == null)
        {
            Log.Error("Failed to create job for extractions: {ExtractionIds}", string.Join(",", extractionIds));
            return null;
        }

        var cancellationSource = new CancellationTokenSource();

        var backgroundTask = Task.Run(async () =>
        {
            try
            {
                Log.Information("Starting background job {JobGuid} of type {JobType} with {ExtractionCount} extractions",
                    job.JobGuid, job.JobType, extractionIds.Count());

                await jobAction(job, cancellationSource.Token);

                await UpdateJob(job.JobGuid, JobStatus.Completed);
                Log.Information("Background job {JobGuid} completed successfully", job.JobGuid);
            }
            catch (OperationCanceledException) when (cancellationSource.Token.IsCancellationRequested)
            {
                await UpdateJob(job.JobGuid, JobStatus.Cancelled);
                Log.Information("Background job {JobGuid} was cancelled", job.JobGuid);
            }
            catch (Exception ex)
            {
                await UpdateJob(job.JobGuid, JobStatus.Failed);
                Log.Error(ex, "Background job {JobGuid} failed with exception: {Message}", job.JobGuid, ex.Message);
            }
        }, CancellationToken.None);

        AttachTask(job.JobGuid, backgroundTask, cancellationSource);

        Log.Debug("Created and started background job {JobGuid} with {ExtractionCount} extractions",
            job.JobGuid, extractionIds.Count());

        return job;
    }

    public async Task UpdateJob(Guid jobGuid, JobStatus status)
    {
        if (jobs.TryGetValue(jobGuid, out var job))
        {
            var previousStatus = job.Status;
            job.Status = status;
            job.EndTime = DateTimeOffset.UtcNow;

            Log.Debug("Job {JobGuid} status updated from {PreviousStatus} to {NewStatus}",
                jobGuid, previousStatus, status);

            if (status == JobStatus.Completed || status == JobStatus.Failed || status == JobStatus.Cancelled)
            {
                await DumpSingleJob(job);

                if (jobTasks.TryRemove(jobGuid, out var taskInfo))
                {
                    try
                    {
                        taskInfo.cancellationSource?.Dispose();
                        Log.Debug("Disposed cancellation token source for job {JobGuid}", jobGuid);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error disposing cancellation token source for job {JobGuid}", jobGuid);
                    }
                }

                var duration = job.EndTime - job.StartTime;
                Log.Information("Job {JobGuid} of type {JobType} finished with status {Status} after {Duration}ms",
                    jobGuid, job.JobType, status, duration?.TotalMilliseconds);
            }
        }
        else
        {
            Log.Warning("Attempted to update non-existent job {JobGuid} to status {Status}", jobGuid, status);
        }
    }

    public void AttachTask(Guid jobGuid, Task task, CancellationTokenSource cancellationTokenSource)
    {
        if (jobs.ContainsKey(jobGuid))
        {
            if (jobTasks.TryAdd(jobGuid, (task, cancellationTokenSource)))
            {
                Log.Debug("Attached task to job {JobGuid} for cancellation tracking", jobGuid);
            }
            else
            {
                Log.Warning("Failed to attach task to job {JobGuid} - task already exists", jobGuid);
            }
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

            var cancellationTimeout = TimeSpan.FromSeconds(30);
            var timeoutTask = Task.Delay(cancellationTimeout);
            var completedTask = await Task.WhenAny(taskInfo.task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                Log.Warning("Job {JobGuid} did not respond to cancellation within {Timeout} seconds",
                    jobGuid, cancellationTimeout.TotalSeconds);

                await UpdateJob(jobGuid, JobStatus.Cancelled);
            }
            else
            {
                Log.Information("Job {JobGuid} responded to cancellation request", jobGuid);
            }

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

        try
        {
            await repository.CreateJob(job);
            jobs.TryRemove(job.JobGuid, out _);
            Log.Debug("Dumped job {JobGuid} to database and removed from memory", job.JobGuid);
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
        if (job is null)
        {
            Log.Debug("No running job found for extraction {ExtractionId} to update transferred bytes", extractionId);
            return;
        }

        var jobExtraction = job.JobExtractions.FirstOrDefault(je => je.ExtractionId == extractionId);
        if (jobExtraction is null)
        {
            Log.Warning("Job extraction not found for extraction {ExtractionId} in job {JobGuid}", extractionId, job.JobGuid);
            return;
        }

        jobExtraction.AddTransferedBytes(bytes);
        Log.Debug("Updated transferred bytes for extraction {ExtractionId} in job {JobGuid}: +{Bytes} bytes",
            extractionId, job.JobGuid, bytes);
    }

    public IEnumerable<Job> GetActiveJobs() =>
        jobs.Values.Where(j => j.Status == JobStatus.Running);

    public IEnumerable<Job> GetAllJobs() => jobs.Values;

    public IEnumerable<Job> GetJobsByStatus(JobStatus status) =>
        jobs.Values.Where(j => j.Status == status);

    public async Task<int> CancelAllJobsAsync()
    {
        var runningJobs = GetActiveJobs().ToList();
        var cancelledCount = 0;

        Log.Information("Attempting to cancel {JobCount} running jobs", runningJobs.Count);

        var cancellationTasks = runningJobs.Select(async job =>
        {
            try
            {
                var success = await CancelJob(job.JobGuid);
                if (success)
                {
                    Interlocked.Increment(ref cancelledCount);
                }
                return success;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error cancelling job {JobGuid} during bulk cancellation", job.JobGuid);
                return false;
            }
        });

        await Task.WhenAll(cancellationTasks);

        Log.Information("Cancelled {CancelledCount}/{TotalCount} jobs", cancelledCount, runningJobs.Count);
        return cancelledCount;
    }

    public JobStatistics GetJobStatistics()
    {
        var allJobs = jobs.Values.ToList();
        var activeTasks = jobTasks.Count;

        return new JobStatistics
        {
            TotalJobs = allJobs.Count,
            RunningJobs = allJobs.Count(j => j.Status == JobStatus.Running),
            CompletedJobs = allJobs.Count(j => j.Status == JobStatus.Completed),
            FailedJobs = allJobs.Count(j => j.Status == JobStatus.Failed),
            CancelledJobs = allJobs.Count(j => j.Status == JobStatus.Cancelled),
            ActiveTasks = activeTasks,
            OldestRunningJob = allJobs
                .Where(j => j.Status == JobStatus.Running)
                .OrderBy(j => j.StartTime)
                .FirstOrDefault()?.StartTime
        };
    }

    public int CleanupCompletedJobs()
    {
        var completedJobs = jobs.Values
            .Where(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Failed || j.Status == JobStatus.Cancelled)
            .ToList();

        var cleanedCount = 0;
        foreach (var job in completedJobs)
        {
            if (jobs.TryRemove(job.JobGuid, out _))
            {
                cleanedCount++;
                Log.Debug("Cleaned up completed job {JobGuid} from memory", job.JobGuid);
            }
        }

        if (cleanedCount > 0)
        {
            Log.Information("Cleaned up {CleanedCount} completed jobs from memory", cleanedCount);
        }

        return cleanedCount;
    }
}


public record JobStatistics
{
    public int TotalJobs { get; init; }
    public int RunningJobs { get; init; }
    public int CompletedJobs { get; init; }
    public int FailedJobs { get; init; }
    public int CancelledJobs { get; init; }
    public int ActiveTasks { get; init; }
    public DateTimeOffset? OldestRunningJob { get; init; }
}
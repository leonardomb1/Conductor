using Conductor.Logging;
using Conductor.Model;
using Conductor.Types;
using Microsoft.EntityFrameworkCore;

namespace Conductor.Repository;

public class JobRepository(EfContext context) : IRepository<Job>
{
    public Task<Result<List<Job>>> Search(IQueryCollection? filters = null)
    {
        throw new NotImplementedException();
    }

    public async Task<Result<List<JobDto>>> SearchJob(IQueryCollection? filters = null)
    {
        try
        {
            var select = (
                from j in context.Jobs
                join je in context.JobExtractions on j.JobGuid equals je.JobGuid
                join e in context.Extractions on je.ExtractionId equals e.Id
                group new { je, e } by new
                {
                    j.JobGuid,
                    j.JobType,
                    j.Status,
                    j.StartTime,
                    j.EndTime
                } into jobGroup
                let firstExtraction = jobGroup.First()
                select new JobDto(
                    firstExtraction.e.Name,
                    jobGroup.Key.JobGuid,
                    jobGroup.Key.JobType.ToString(),
                    jobGroup.Key.Status.ToString(),
                    jobGroup.Key.StartTime,
                    jobGroup.Key.EndTime,
                    jobGroup.Key.EndTime.HasValue
                        ? (jobGroup.Key.EndTime.Value - jobGroup.Key.StartTime).TotalMilliseconds
                        : 0,
                    jobGroup.Sum(x => x.je.BytesAccumulated) / (1024f * 1024f)
                )
            ).AsQueryable();


            if (filters is not null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key.ToString();
                    string value = filter.Value.ToString();

                    select = key switch
                    {
                        "relativeStart" when Int32.TryParse(value, out var time) => select.Where(
                                j => j.StartTime >= DateTime.Now.AddSeconds(-time)
                            ),
                        "relativeEnd" when Int32.TryParse(value, out var time) => select.Where(
                                j => j.EndTime >= DateTime.Now.AddSeconds(-time)
                            ),
                        "name" => select.Where(j => j.Name == value),
                        "status" => select.Where(j => j.Status == value),
                        "type" => select.Where(j => j.JobType == value),
                        "mbs" when float.TryParse(value, out var mbs) => select.Where(j => j.Bytes > mbs),
                        "take" when UInt32.TryParse(value, out UInt32 count) => select.Take((Int32)count),
                        _ => select
                    };
                }
            }

            return await select.ToListAsync();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<List<JobDto>>> GetActiveJobs()
    {
        try
        {
            if (JobTracker.Jobs.Value.IsEmpty)
            {
                return new List<JobDto>();
            }

            var activeJobs = JobTracker.GetActiveJobs();
            
            var extractionIds = activeJobs
                .SelectMany(j => j.JobExtractions.Select(je => je.ExtractionId))
                .Distinct()
                .ToList();

            var extractions = await context.Extractions
                .Where(e => extractionIds.Contains(e.Id))
                .Select(e => new { e.Id, e.Name })
                .ToListAsync();

            var result = activeJobs
                .SelectMany(j => j.JobExtractions.Select(je => new { Job = j, JobExtraction = je }))
                .Join(extractions,
                    je => je.JobExtraction.ExtractionId,
                    e => e.Id,
                    (je, e) => new JobDto(
                        e.Name,
                        je.Job.JobGuid,
                        je.Job.JobType.ToString(),
                        je.Job.Status.ToString(),
                        je.Job.StartTime,
                        je.Job.EndTime,
                        ((je.Job.EndTime ?? DateTime.Now) - je.Job.StartTime).TotalMilliseconds,
                        je.Job.JobExtractions.Sum(x => x.BytesAccumulated) / (1024f * 1024f)
                    )
                )
                .OrderByDescending(j => j.StartTime)
                .ToList();

            return result;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public Task<Result<Job?>> Search(UInt32 id)
    {
        throw new NotImplementedException();
    }

    public async Task<Result<Int32>> Count()
    {
        try
        {
            var select = from s in context.Jobs
                         select s;

            return await select.CountAsync();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> CreateBulk(List<Job> jobs)
    {
        try
        {
            await context.Jobs.AddRangeAsync(jobs);
            await context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> Clear()
    {
        try
        {
            context.Jobs.RemoveRange(context.Jobs);
            await context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public Task<Result> Delete(UInt32 id)
    {
        throw new NotImplementedException();
    }

    public Task<Result<UInt32>> Create(Job job)
    {
        throw new NotImplementedException();
    }

    public Task<Result> Update(Job job, UInt32 id)
    {
        throw new NotImplementedException();
    }
}
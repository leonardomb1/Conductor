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
            var jobGroupsQuery =
                from j in context.Jobs
                join je in context.JobExtractions on j.JobGuid equals je.JobGuid
                join e in context.Extractions on je.ExtractionId equals e.Id
                group new { j, je, e } by new
                {
                    j.JobGuid,
                    j.JobType,
                    j.Status,
                    j.StartTime,
                    j.EndTime
                } into jobGroup
                select new
                {
                    Job = jobGroup.Key,
                    SumBytes = jobGroup.Sum(x => x.je.BytesAccumulated),
                    Extractions = jobGroup.Select(x => x.e.Name).ToList()
                };

            if (filters is not null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key;
                    string value = filter.Value!;

                    jobGroupsQuery = key switch
                    {
                        "relativeStart" when Int32.TryParse(value, out var time)
                            => jobGroupsQuery.Where(g => g.Job.StartTime >= DateTime.Now.AddSeconds(-time)),
                        "relativeEnd" when Int32.TryParse(value, out var time)
                            => jobGroupsQuery.Where(g => g.Job.EndTime >= DateTime.Now.AddSeconds(-time)),
                        "status" => jobGroupsQuery.Where(g => g.Job.Status.ToString() == value),
                        "type" => jobGroupsQuery.Where(g => g.Job.JobType.ToString() == value),
                        _ => jobGroupsQuery
                    };
                }
            }

            var jobGroups = await jobGroupsQuery.ToListAsync();

            var jobDtos = jobGroups
                .Select(g => new JobDto(
                    g.Extractions.FirstOrDefault() ?? string.Empty,
                    g.Job.JobGuid,
                    g.Job.JobType.ToString(),
                    g.Job.Status.ToString(),
                    g.Job.StartTime,
                    g.Job.EndTime,
                    g.Job.EndTime.HasValue
                        ? (g.Job.EndTime.Value - g.Job.StartTime).TotalMilliseconds
                        : 0,
                    g.SumBytes / (1024f * 1024f)
                )).ToList();

            return jobDtos;
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
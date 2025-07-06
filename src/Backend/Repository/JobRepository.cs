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
            var jobExtractionsQuery =
                from j in context.Jobs
                join je in context.JobExtractions on j.JobGuid equals je.JobGuid
                join e in context.Extractions on je.ExtractionId equals e.Id
                select new { j, je, e };

            if (filters is not null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key;
                    string value = filter.Value!;
                    jobExtractionsQuery = key switch
                    {
                        "relativeStart" when Int32.TryParse(value, out var time)
                            => jobExtractionsQuery.Where(x => x.j.StartTime >= DateTime.Now.AddSeconds(-time)),
                        "relativeEnd" when Int32.TryParse(value, out var time)
                            => jobExtractionsQuery.Where(x => x.j.EndTime >= DateTime.Now.AddSeconds(-time)),
                        "status" => jobExtractionsQuery.Where(x => x.j.Status.ToString() == value),
                        "type" => jobExtractionsQuery.Where(x => x.j.JobType.ToString() == value),
                        _ => jobExtractionsQuery
                    };
                }
            }

            var results = await jobExtractionsQuery.ToListAsync();

            var jobDtos = results
                .Select(x => new JobDto(
                    x.e.Name,
                    x.j.JobGuid,
                    x.j.JobType.ToString(),
                    x.j.Status.ToString(),
                    x.j.StartTime,
                    x.j.EndTime,
                    x.j.EndTime.HasValue
                        ? (x.j.EndTime.Value - x.j.StartTime).TotalMilliseconds
                        : 0,
                    x.je.BytesAccumulated / (1024f * 1024f)
                ))
                .ToList();

            return jobDtos;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<List<ExtractionAggregatedDto>>> GetExtractionAggregatedView(IQueryCollection? filters = null)
    {
        try
        {
            var extractionQuery =
                from e in context.Extractions
                join je in context.JobExtractions on e.Id equals je.ExtractionId
                join j in context.Jobs on je.JobGuid equals j.JobGuid
                select new { e, je, j };

            if (filters is not null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key;
                    string value = filter.Value!;
                    extractionQuery = key switch
                    {
                        "relativeStart" when Int32.TryParse(value, out var time)
                            => extractionQuery.Where(x => x.j.StartTime >= DateTime.Now.AddSeconds(-time)),
                        "relativeEnd" when Int32.TryParse(value, out var time)
                            => extractionQuery.Where(x => x.j.EndTime >= DateTime.Now.AddSeconds(-time)),
                        "status" => extractionQuery.Where(x => x.j.Status.ToString() == value),
                        "type" => extractionQuery.Where(x => x.j.JobType.ToString() == value),
                        "extractionName" => extractionQuery.Where(x => x.e.Name.Contains(value)),
                        _ => extractionQuery
                    };
                }
            }

            var results = await extractionQuery.ToListAsync();

            var aggregatedExtractions = results
                .GroupBy(x => new { x.e.Id, x.e.Name })
                .Select(extractionGroup => new ExtractionAggregatedDto(
                    extractionGroup.Key.Id,
                    extractionGroup.Key.Name,
                    extractionGroup.Count(),
                    extractionGroup.Sum(x => x.je.BytesAccumulated) / (1024f * 1024f),
                    extractionGroup.Where(x => x.j.EndTime.HasValue).Max(x => x.j.EndTime),
                    extractionGroup.Where(x => x.j.Status == JobStatus.Completed).Count(),
                    extractionGroup.Where(x => x.j.Status == JobStatus.Failed).Count(),
                    extractionGroup.Where(x => x.j.Status == JobStatus.Running).Count()
                ))
                .OrderByDescending(x => x.LastEndTime)
                .ToList();

            return aggregatedExtractions;
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

    public async Task<Result> CreateJob(Job job)
    {
        try
        {
            context.Jobs.Add(job);
            await context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
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
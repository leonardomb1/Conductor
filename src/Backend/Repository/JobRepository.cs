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
                        "relativeStart" when int.TryParse(value, out var time)
                            => jobExtractionsQuery.Where(x => x.j.StartTime >= DateTime.Now.AddSeconds(-time)),
                        "relativeEnd" when int.TryParse(value, out var time)
                            => jobExtractionsQuery.Where(x => x.j.EndTime >= DateTime.Now.AddSeconds(-time)),
                        "status" => jobExtractionsQuery.Where(x => x.j.Status.ToString() == value),
                        "type" => jobExtractionsQuery.Where(x => x.j.JobType.ToString() == value),
                        "extractionName" => jobExtractionsQuery.Where(x => x.e.Name.Contains(value)),
                        "skip" => jobExtractionsQuery,
                        "take" => jobExtractionsQuery,
                        "sortBy" => jobExtractionsQuery,
                        "sortDirection" => jobExtractionsQuery,
                        _ => jobExtractionsQuery
                    };
                }
            }

            // Apply sorting
            var sortBy = filters?["sortBy"].FirstOrDefault() ?? "startTime";
            var sortDirection = filters?["sortDirection"].FirstOrDefault() ?? "desc";

            jobExtractionsQuery = sortBy.ToLowerInvariant() switch
            {
                "name" => sortDirection == "asc" ?
                    jobExtractionsQuery.OrderBy(x => x.e.Name) :
                    jobExtractionsQuery.OrderByDescending(x => x.e.Name),
                "jobtype" => sortDirection == "asc" ?
                    jobExtractionsQuery.OrderBy(x => x.j.JobType) :
                    jobExtractionsQuery.OrderByDescending(x => x.j.JobType),
                "status" => sortDirection == "asc" ?
                    jobExtractionsQuery.OrderBy(x => x.j.Status) :
                    jobExtractionsQuery.OrderByDescending(x => x.j.Status),
                "timespentms" => sortDirection == "asc" ?
                    jobExtractionsQuery.OrderBy(x => x.j.EndTime.HasValue ? (x.j.EndTime.Value - x.j.StartTime).TotalMilliseconds : 0) :
                    jobExtractionsQuery.OrderByDescending(x => x.j.EndTime.HasValue ? (x.j.EndTime.Value - x.j.StartTime).TotalMilliseconds : 0),
                "megabytes" => sortDirection == "asc" ?
                    jobExtractionsQuery.OrderBy(x => x.je.BytesAccumulated) :
                    jobExtractionsQuery.OrderByDescending(x => x.je.BytesAccumulated),
                _ => sortDirection == "asc" ?
                    jobExtractionsQuery.OrderBy(x => x.j.StartTime) :
                    jobExtractionsQuery.OrderByDescending(x => x.j.StartTime)
            };

            // Apply pagination
            if (filters != null)
            {
                if (uint.TryParse(filters["skip"], out uint skip))
                {
                    jobExtractionsQuery = jobExtractionsQuery.Skip((int)skip);
                }

                if (uint.TryParse(filters["take"], out uint take))
                {
                    jobExtractionsQuery = jobExtractionsQuery.Take((int)take);
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

    // Add method to get total count for pagination
    public async Task<Result<int>> GetJobsCount(IQueryCollection? filters = null)
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
                        "relativeStart" when int.TryParse(value, out var time)
                            => jobExtractionsQuery.Where(x => x.j.StartTime >= DateTime.Now.AddSeconds(-time)),
                        "relativeEnd" when int.TryParse(value, out var time)
                            => jobExtractionsQuery.Where(x => x.j.EndTime >= DateTime.Now.AddSeconds(-time)),
                        "status" => jobExtractionsQuery.Where(x => x.j.Status.ToString() == value),
                        "type" => jobExtractionsQuery.Where(x => x.j.JobType.ToString() == value),
                        "extractionName" => jobExtractionsQuery.Where(x => x.e.Name.Contains(value)),
                        "skip" or "take" or "sortBy" or "sortDirection" => jobExtractionsQuery,
                        _ => jobExtractionsQuery
                    };
                }
            }

            return await jobExtractionsQuery.CountAsync();
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
                        "relativeStart" when int.TryParse(value, out var time)
                            => extractionQuery.Where(x => x.j.StartTime >= DateTime.Now.AddSeconds(-time)),
                        "relativeEnd" when int.TryParse(value, out var time)
                            => extractionQuery.Where(x => x.j.EndTime >= DateTime.Now.AddSeconds(-time)),
                        "status" => extractionQuery.Where(x => x.j.Status.ToString() == value),
                        "type" => extractionQuery.Where(x => x.j.JobType.ToString() == value),
                        "extractionName" => extractionQuery.Where(x => x.e.Name.Contains(value)),
                        "skip" => extractionQuery,
                        "take" => extractionQuery,
                        "sortBy" => extractionQuery,
                        "sortDirection" => extractionQuery,
                        _ => extractionQuery
                    };
                }
            }

            var results = await extractionQuery.ToListAsync();

            IEnumerable<ExtractionAggregatedDto> aggregatedExtractions = results
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
                .AsEnumerable()
                .OrderByDescending(x => x.LastEndTime);

            var sortBy = filters?["sortBy"].FirstOrDefault() ?? "lastEndTime";
            var sortDirection = filters?["sortDirection"].FirstOrDefault() ?? "desc";

            aggregatedExtractions = sortBy.ToLowerInvariant() switch
            {
                "extractionname" => sortDirection == "asc" ?
                    aggregatedExtractions.OrderBy(x => x.ExtractionName) :
                    aggregatedExtractions.OrderByDescending(x => x.ExtractionName),
                "totaljobs" => sortDirection == "asc" ?
                    aggregatedExtractions.OrderBy(x => x.TotalJobs) :
                    aggregatedExtractions.OrderByDescending(x => x.TotalJobs),
                "totalsizemb" => sortDirection == "asc" ?
                    aggregatedExtractions.OrderBy(x => x.TotalSizeMB) :
                    aggregatedExtractions.OrderByDescending(x => x.TotalSizeMB),
                "completedjobs" => sortDirection == "asc" ?
                    aggregatedExtractions.OrderBy(x => x.CompletedJobs) :
                    aggregatedExtractions.OrderByDescending(x => x.CompletedJobs),
                "failedjobs" => sortDirection == "asc" ?
                    aggregatedExtractions.OrderBy(x => x.FailedJobs) :
                    aggregatedExtractions.OrderByDescending(x => x.FailedJobs),
                _ => sortDirection == "asc" ?
                    aggregatedExtractions.OrderBy(x => x.LastEndTime) :
                    aggregatedExtractions.OrderByDescending(x => x.LastEndTime)
            };

            if (filters != null)
            {
                if (uint.TryParse(filters["skip"], out uint skip))
                {
                    aggregatedExtractions = aggregatedExtractions.Skip((int)skip);
                }

                if (uint.TryParse(filters["take"], out uint take))
                {
                    aggregatedExtractions = aggregatedExtractions.Take((int)take);
                }
            }

            return aggregatedExtractions.ToList();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    // Add method to get aggregated count
    public async Task<Result<int>> GetAggregatedJobsCount(IQueryCollection? filters = null)
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
                        "relativeStart" when int.TryParse(value, out var time)
                            => extractionQuery.Where(x => x.j.StartTime >= DateTime.Now.AddSeconds(-time)),
                        "relativeEnd" when int.TryParse(value, out var time)
                            => extractionQuery.Where(x => x.j.EndTime >= DateTime.Now.AddSeconds(-time)),
                        "status" => extractionQuery.Where(x => x.j.Status.ToString() == value),
                        "type" => extractionQuery.Where(x => x.j.JobType.ToString() == value),
                        "extractionName" => extractionQuery.Where(x => x.e.Name.Contains(value)),
                        // Skip pagination and sorting parameters for count
                        "skip" or "take" or "sortBy" or "sortDirection" => extractionQuery,
                        _ => extractionQuery
                    };
                }
            }

            var results = await extractionQuery.ToListAsync();
            var uniqueExtractions = results.GroupBy(x => new { x.e.Id, x.e.Name }).Count();

            return uniqueExtractions;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public Task<Result<Job?>> Search(uint id)
    {
        throw new NotImplementedException();
    }

    public async Task<Result<int>> Count()
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

    public Task<Result> Delete(uint id)
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

    public Task<Result<uint>> Create(Job job)
    {
        throw new NotImplementedException();
    }

    public Task<Result> Update(Job job, uint id)
    {
        throw new NotImplementedException();
    }
}
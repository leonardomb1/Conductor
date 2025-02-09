using Conductor.Data;
using Conductor.Model;
using Conductor.Shared.Types;
using LinqToDB;
using LinqToDB.Data;

namespace Conductor.Service;

public class JobService(LdbContext context) : ServiceBase(context), IService<Job>
{
    public Task<Result<List<Job>>> Search(IQueryCollection? filters = null)
    {
        throw new NotImplementedException();
    }

    public async Task<Result<List<object>>> SearchJob(IQueryCollection? filters = null)
    {
        try
        {
            var select = (from j in Repository.Jobs
                          join je in Repository.JobExtractions on j.JobGuid equals je.JobGuid
                          join e in Repository.Extractions on je.ExtractionId equals e.Id
                          orderby j.StartTime descending
                          select new
                          {
                              e.Name,
                              j.JobGuid,
                              JobType = $"{j.JobType}",
                              Status = $"{j.Status}",
                              j.StartTime,
                              j.EndTime,
                              TimeSpentMs = (j.EndTime - j.StartTime)!.Value.TotalMilliseconds,
                              TotalMbTransfered = j.BytesAccumulated > 0 ? (float)j.BytesAccumulated / 1_000_000 : 0
                          }).AsQueryable();

            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key.ToString();
                    string value = filter.Value.ToString();

                    select = key switch
                    {
                        "relative" when Int32.TryParse(value, out var time) => select.Where(
                                j => j.StartTime >= DateTime.Now.AddSeconds(-time)
                            ),
                        "extractionName" => select.Where(j => j.Name == value),
                        "take" when Int32.TryParse(value, out Int32 count) => select.Take(count),
                        _ => select
                    };
                }
            }

            var result = await select.ToListAsync();
            return result.Cast<object>().ToList();
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
            var select = from s in Repository.Records
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
            var insert = await Repository.BulkCopyAsync(jobs);
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
            await Repository.Jobs.TruncateAsync();

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

    public Task<Result> Create(Job job)
    {
        throw new NotImplementedException();
    }

    public Task<Result> Update(Job job, UInt32 id)
    {
        throw new NotImplementedException();
    }
}
using Conductor.Data;
using Conductor.Model;
using Conductor.Shared.Types;
using LinqToDB;
using LinqToDB.Data;

namespace Conductor.Service;

public class JobService(LdbContext context) : ServiceBase(context), IService<Job>
{
    public async Task<Result<List<Job>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = (from j in Repository.Jobs
                          .LoadWith(j => j.JobExtractions)
                          orderby j.StartTime descending
                          select j).AsQueryable();

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
                        "extractionId" when UInt32.TryParse(value, out var id) => select.Where(
                            j => j.JobExtractions.Any(je => je.ExtractionId == id)
                        ),
                        "take" when Int32.TryParse(value, out Int32 count) => select.Take(count),
                        _ => select
                    };
                }
            }

            return await select.ToListAsync();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
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
            return ErrorHandler(ex);
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
            return ErrorHandler(ex);
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
            return ErrorHandler(ex);
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
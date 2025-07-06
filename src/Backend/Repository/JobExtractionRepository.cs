using Conductor.Model;
using Conductor.Types;

namespace Conductor.Repository;

public class JobExtractionRepository(EfContext context) : IRepository<JobExtraction>
{
    public Task<Result<List<JobExtraction>>> Search(IQueryCollection? filters = null)
    {
        throw new NotImplementedException();
    }

    public Task<Result<JobExtraction?>> Search(UInt32 id)
    {
        throw new NotImplementedException();
    }

    public Task<Result<Int32>> Count()
    {
        throw new NotImplementedException();
    }

    public async Task<Result> CreateBulk(List<JobExtraction> jobExtractions)
    {
        try
        {
            await context.JobExtractions.AddRangeAsync(jobExtractions);
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
            context.JobExtractions.RemoveRange(context.JobExtractions);
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

    public Task<Result<UInt32>> Create(JobExtraction jobExtraction)
    {
        throw new NotImplementedException();
    }

    public Task<Result> Update(JobExtraction jobExtraction, UInt32 id)
    {
        throw new NotImplementedException();
    }
}
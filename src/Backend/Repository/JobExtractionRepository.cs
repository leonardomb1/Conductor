using Conductor.Model;
using Conductor.Types;

namespace Conductor.Repository;

public class JobExtractionRepository(EfContext context) : IRepository<JobExtraction>
{
    public Task<Result<List<JobExtraction>>> Search(IQueryCollection? filters = null)
    {
        throw new NotImplementedException();
    }

    public Task<Result<JobExtraction?>> Search(uint id)
    {
        throw new NotImplementedException();
    }

    public Task<Result<int>> Count()
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

    public Task<Result> Delete(uint id)
    {
        throw new NotImplementedException();
    }

    public Task<Result<uint>> Create(JobExtraction jobExtraction)
    {
        throw new NotImplementedException();
    }

    public Task<Result> Update(JobExtraction jobExtraction, uint id)
    {
        throw new NotImplementedException();
    }
}
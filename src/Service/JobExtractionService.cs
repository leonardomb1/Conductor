using Conductor.Data;
using Conductor.Model;
using Conductor.Shared.Types;
using LinqToDB;
using LinqToDB.Data;

namespace Conductor.Service;

public class JobExtractionService(LdbContext context) : ServiceBase(context), IService<JobExtraction>
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
            var insert = await Repository.BulkCopyAsync(jobExtractions);
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
            await Repository.JobExtractions.TruncateAsync();

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

    public Task<Result> Create(JobExtraction jobExtraction)
    {
        throw new NotImplementedException();
    }

    public Task<Result> Update(JobExtraction jobExtraction, UInt32 id)
    {
        throw new NotImplementedException();
    }
}
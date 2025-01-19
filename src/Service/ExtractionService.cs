using Conductor.Data;
using Conductor.Model;
using Conductor.Shared.Types;
using LinqToDB;

namespace Conductor.Service;

public sealed class ExtractionService(LdbContext context) : ServiceBase(context), IService<Extraction>
{
    public async Task<Result<List<Extraction>>> Search(IQueryCollection? filters)
    {
        try
        {
            var select = from e in Repository.Extractions
                         .LoadWith(e => e.Schedule)
                         .LoadWith(e => e.Origin)
                         .LoadWith(e => e.Destination)
                         select e;

            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key.ToString();
                    string value = filter.Value.ToString();

                    select = key switch
                    {
                        "name" => select.Where(e => e.Name == value),
                        "schedule" => select.Where(e => e.Schedule!.Name == value),
                        "origin" => select.Where(e => e.Origin!.Name == value),
                        "destination" => select.Where(e => e.Destination!.Name == value),
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

    public async Task<Result<Extraction?>> Search(UInt32 id)
    {
        try
        {
            var select = from e in Repository.Extractions
                         .LoadWith(e => e.Schedule)
                         .LoadWith(e => e.Origin)
                         .LoadWith(e => e.Destination)
                         where e.Id == id
                         select e;

            return await select.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Create(Extraction extraction)
    {
        try
        {
            var insert = await Repository.InsertAsync(extraction);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Update(Extraction extraction, UInt32 id)
    {
        try
        {
            extraction.Id = id;

            await Repository.UpdateAsync(extraction);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Delete(UInt32 id)
    {
        try
        {
            await Repository.Extractions
                .Where(e => e.Id == id)
                .DeleteAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }
}
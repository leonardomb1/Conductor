using Conductor.Data;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using LinqToDB;

namespace Conductor.Service;

public class OriginService(LdbContext context) : ServiceBase(context), IService<Origin>
{
    public async Task<Result<List<Origin>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = from o in Repository.Origins
                         select o;

            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    select = filter.Key.ToLower() switch
                    {
                        "name" => select.Where(e => e.Name == filter.Value),
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

    public async Task<Result<Origin?>> Search(UInt32 id)
    {
        try
        {
            var select = from o in Repository.Origins
                         where o.Id == id
                         select o;

            return await select.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Create(Origin system)
    {
        try
        {
            system.ConnectionString = Encryption.SymmetricEncryptAES256(system.ConnectionString, Settings.EncryptionKey);

            var insert = await Repository.InsertAsync(system);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Update(Origin system, UInt32 id)
    {
        try
        {
            system.ConnectionString = Encryption.SymmetricEncryptAES256(system.ConnectionString, Settings.EncryptionKey);

            system.Id = id;
            await Repository.UpdateAsync(system);

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
            await Repository.Origins
                .Where(s => s.Id == id)
                .DeleteAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }
}
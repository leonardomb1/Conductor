using Conductor.Data;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using LinqToDB;

namespace Conductor.Service;

public class DestinationService(LdbContext context) : ServiceBase(context), IService<Destination>
{
    public async Task<Result<List<Destination>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = from db in Repository.Destinations
                         select db;

            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key.ToString();
                    string value = filter.Value.ToString();

                    select = key switch
                    {
                        "name" => select.Where(e => e.Name == value),
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

    public async Task<Result<Destination?>> Search(UInt32 id)
    {
        try
        {
            var select = from db in Repository.Destinations
                         where db.Id == id
                         select db;

            return await select.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Create(Destination destination)
    {
        try
        {
            destination.ConnectionString = Encryption.SymmetricEncryptAES256(destination.ConnectionString, Settings.EncryptionKey);
            var insert = await Repository.InsertAsync(destination);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Update(Destination destination, UInt32 id)
    {
        try
        {
            destination.ConnectionString = Encryption.SymmetricEncryptAES256(destination.ConnectionString, Settings.EncryptionKey);
            destination.Id = id;

            await Repository.UpdateAsync(destination);

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
            await Repository.Destinations
                .Where(db => db.Id == id)
                .DeleteAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }
}
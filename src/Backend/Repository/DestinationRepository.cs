using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;
using Microsoft.EntityFrameworkCore;

namespace Conductor.Repository;

public class DestinationRepository(EfContext context) : IRepository<Destination>
{
    public async Task<Result<List<Destination>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = from db in context.Destinations
                         select db;

            if (filters is not null)
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
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<Destination?>> Search(UInt32 id)
    {
        try
        {
            var select = from db in context.Destinations
                         where db.Id == id
                         select db;

            return await select.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<UInt32>> Create(Destination destination)
    {
        if (destination.ConnectionString is not null && destination.ConnectionString != "")
            destination.ConnectionString = Encryption.SymmetricEncryptAES256(destination.ConnectionString!, Settings.EncryptionKey);

        try
        {
            await context.Destinations.AddAsync(destination);
            await context.SaveChangesAsync();
            return destination.Id;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> Update(Destination destination, UInt32 id)
    {
        try
        {
            destination.Id = id;

            var existingSystem = await context.Destinations.FindAsync(id);
            if (existingSystem is null)
                return new Error($"Destination with id: {id} was not found", null);

            if (destination.ConnectionString is not null && destination.ConnectionString != "")
                destination.ConnectionString = Encryption.SymmetricEncryptAES256(destination.ConnectionString!, Settings.EncryptionKey);

            context.Entry(existingSystem).CurrentValues.SetValues(destination);
            context.Entry(existingSystem).Property(x => x.Id).IsModified = false;

            await context.SaveChangesAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> Delete(UInt32 id)
    {
        try
        {
            var system = await context.Destinations.FindAsync(id);
            if (system is null)
                return new Error($"Destination with id: {id} was not found", null);

            context.Destinations.Remove(system);
            await context.SaveChangesAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}
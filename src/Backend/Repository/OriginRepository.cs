using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;
using Microsoft.EntityFrameworkCore;

namespace Conductor.Repository;

public class OriginRepository(EfContext context) : IRepository<Origin>
{
    public async Task<Result<List<Origin>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = from o in context.Origins
                         select o;

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

    public async Task<Result<Origin?>> Search(UInt32 id)
    {
        try
        {
            var select = from o in context.Origins
                         where o.Id == id
                         select o;

            return await select.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<UInt32>> Create(Origin system)
    {
        if (system.ConnectionString is not null && system.ConnectionString != "")
            system.ConnectionString = Encryption.SymmetricEncryptAES256(system.ConnectionString!, Settings.EncryptionKey);

        try
        {
            await context.Origins.AddAsync(system);
            await context.SaveChangesAsync();
            return system.Id;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> Update(Origin system, UInt32 id)
    {
        try
        {
            system.Id = id;

            var existingSystem = await context.Origins.FindAsync(id);
            if (existingSystem is null)
                return new Error($"Origin with id: {id} was not found", null);

            if (system.ConnectionString is not null && system.ConnectionString != "")
                system.ConnectionString = Encryption.SymmetricEncryptAES256(system.ConnectionString!, Settings.EncryptionKey);

            context.Entry(existingSystem).CurrentValues.SetValues(system);

            context.Origins.Update(existingSystem);
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
            var system = await context.Origins.FindAsync(id);
            if (system is null)
                return new Error($"Origin with id: {id} was not found", null);

            context.Origins.Remove(system);
            await context.SaveChangesAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}
using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;
using Microsoft.EntityFrameworkCore;

namespace Conductor.Repository;

public sealed class UserRepository(EfContext context) : IRepository<User>
{
    public async Task<Result<List<User>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = from u in context.Users
                         select new User
                         {
                             Id = u.Id,
                             Name = u.Name
                         };

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

            var result = await select.ToListAsync();

            return result;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<User?>> Search(uint id)
    {
        try
        {
            var select = from u in context.Users
                         where u.Id == id
                         select new User
                         {
                             Id = u.Id,
                             Name = u.Name
                         };

            return await select.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<string>> SearchUserCredential(string userName)
    {
        try
        {
            var search = from u in context.Users
                         where u.Name == userName
                         select new User { Password = u.Password };

            var result = await search.FirstOrDefaultAsync();

            return result?.Password is null ? "" : Encryption.SymmetricDecryptAES256(result.Password, Settings.EncryptionKey) ?? "";
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<uint>> Create(User user)
    {
        user.Password = Encryption.SymmetricEncryptAES256(user.Password!, Settings.EncryptionKey);

        try
        {
            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();
            return user.Id;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> Update(User user, uint id)
    {
        try
        {
            var existingUser = await context.Users.FindAsync(id);
            if (existingUser is null)
                return new Error($"User with id: {id} was not found", null);

            user.Id = id;
            user.Password = Encryption.SymmetricEncryptAES256(user.Password!, Settings.EncryptionKey);

            context.Entry(existingUser).CurrentValues.SetValues(user);
            await context.SaveChangesAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> Delete(uint id)
    {
        try
        {
            var user = await context.Users.FindAsync(id);
            if (user is null)
                return new Error($"User with id: {id} was not found", null);

            context.Users.Remove(user);
            await context.SaveChangesAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}
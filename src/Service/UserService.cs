using Conductor.Data;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using LinqToDB;

namespace Conductor.Service;

public sealed class UserService(LdbContext context) : ServiceBase(context), IService<User>
{
    public async Task<Result<List<User>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = from u in Repository.Users
                         select new User
                         {
                             Id = u.Id,
                             Name = u.Name
                         };

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

            var result = await select.ToListAsync();

            return result;
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result<User?>> Search(UInt32 id)
    {
        try
        {
            var select = from u in Repository.Users
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
            return ErrorHandler(ex);
        }
    }

    public async Task<Result<string>> SearchUserCredential(string userName)
    {
        try
        {
            var search = from u in Repository.Users
                         where u.Name == userName
                         select new User { Password = u.Password };

            var result = await search.FirstOrDefaultAsync();

            return result?.Password == null ? "" : Encryption.SymmetricDecryptAES256(result.Password, Settings.EncryptionKey) ?? "";
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Create(User user)
    {
        User encryptedUser = user;
        encryptedUser.Password = Encryption.SymmetricEncryptAES256(encryptedUser.Password!, Settings.EncryptionKey);

        try
        {
            var insert = await Repository.InsertAsync(encryptedUser);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Update(User user, UInt32 id)
    {
        User encryptedUser = user;
        encryptedUser.Password = Encryption.SymmetricEncryptAES256(encryptedUser.Password!, Settings.EncryptionKey);

        try
        {
            user.Id = id;

            await Repository.UpdateAsync(user);

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
            await Repository.Users
                .Where(u => u.Id == id)
                .DeleteAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }
}
using System.Net;
using System.Threading.Tasks;
using Conductor.Model;
using Conductor.Service;
using Conductor.Shared;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using Microsoft.AspNetCore.Http.HttpResults;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class UserController(UserService service) : ControllerBase<User>(service)
{
    public async ValueTask<Results<Ok<Message<object>>, InternalServerError<Message<Error>>, BadRequest<Message>, UnauthorizedHttpResult>> Login(Stream body, string ip)
    {
        var deserialize = await Converter.TryDeserializeJson<User>(body);

        if (!deserialize.IsSuccessful || deserialize.Value.Name == "" || deserialize.Value.Password == "")
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "This is an invalid JSON format for this operation.")
            );
        }

        var userSecret = await service.SearchUserCredential(deserialize.Value.Name);

        if (!userSecret.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to fetch data from Db.", userSecret.Error)
            );
        }

        if (userSecret.Value is null || userSecret.Value == "" || userSecret.Value != deserialize.Value.Password)
        {
            return TypedResults.Unauthorized();
        }

        string jwt = Encryption.GenerateJwt(ip, deserialize.Value.Name, Settings.EncryptionKey);

        return TypedResults.Ok(
            new Message<object>(Status200OK, "JWT generated successfully.", [new { token = jwt }])
        );
    }

    public async Task<Results<Ok<Message<object>>, InternalServerError<Message<Error>>, BadRequest<Message>, UnauthorizedHttpResult>> LoginWithLdap(Stream body, string ip)
    {
        var deserialize = await Converter.TryDeserializeJson<User>(body);

        if (!deserialize.IsSuccessful || deserialize.Value.Name == "" || deserialize.Value.Password == "")
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "This is an invalid JSON format for this operation.")
            );
        }

        var ldapSearch = LdapAuth.AuthenticateUser(deserialize.Value.Name, deserialize.Value.Password!);

        if (!ldapSearch.IsSuccessful)
        {
            if (!Settings.DevelopmentMode) return TypedResults.Unauthorized();

            return TypedResults.InternalServerError(
                ErrorMessage("An error has occured while attempting to authenticate using LDAP.", ldapSearch.Error)
            );
        }

        if (!ldapSearch.Value)
        {
            return TypedResults.Unauthorized();
        }

        string jwt = Encryption.GenerateJwt(ip, deserialize.Value.Name, Settings.EncryptionKey);

        return TypedResults.Ok(
            new Message<object>(Status200OK, "Token generated successfully.", [new { token = jwt }])
        );
    }
}
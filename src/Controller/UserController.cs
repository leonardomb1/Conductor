using Conductor.Model;
using Conductor.Repository;
using Conductor.Shared;
using Conductor.Types;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class UserController(UserRepository repository) : ControllerBase<User>(repository)
{
    public async Task<IResult> Login(Stream body, string ip)
    {
        var deserialize = await Converter.TryDeserializeJson<User>(body);

        if (!deserialize.IsSuccessful || deserialize.Value.Name == "" || deserialize.Value.Password == "")
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "This is an invalid JSON format for this operation.")
            );
        }

        var userSecret = await repository.SearchUserCredential(deserialize.Value.Name);

        if (!userSecret.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(userSecret.Error)
            );
        }

        if (userSecret.Value is null || userSecret.Value == "" || userSecret.Value != deserialize.Value.Password)
        {
            return Results.Unauthorized();
        }

        string jwt = Encryption.GenerateJwt(ip, deserialize.Value.Name, Settings.EncryptionKey);

        return Results.Ok(jwt);
    }

    public async Task<IResult> LoginWithLdap(Stream body, string ip)
    {
        var deserialize = await Converter.TryDeserializeJson<User>(body);

        if (!deserialize.IsSuccessful || deserialize.Value.Name == "" || deserialize.Value.Password == "")
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "This is an invalid JSON format for this operation.")
            );
        }

        var ldapSearch = await LdapAuth.AuthenticateUser(deserialize.Value.Name, deserialize.Value.Password!);

        if (!ldapSearch.IsSuccessful)
        {
            if (!Settings.DevelopmentMode) return Results.Unauthorized();

            return Results.InternalServerError(
                ErrorMessage(ldapSearch.Error)
            );
        }

        if (!ldapSearch.Value)
        {
            return Results.Unauthorized();
        }

        string jwt = Encryption.GenerateJwt(ip, deserialize.Value.Name, Settings.EncryptionKey);

        return Results.Ok(jwt);
    }

    public static RouteGroupBuilder Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users")
            .WithTags("Users");

        group.MapGet("/", async (UserController controller, HttpRequest request) => await controller.Get(request.Query))
            .WithName("GetUsers");

        group.MapGet("/{id}", async (UserController controller, string id) => await controller.GetById(id))
            .WithName("GetUserById");

        group.MapPost("/", async (UserController controller, HttpRequest request) => await controller.Post(request.Body))
            .Accepts<User>("application/json")
            .WithName("PostUser");

        group.MapPut("/{id}", async (UserController controller, HttpRequest request, string id) => await controller.Put(id, request.Body))
            .Accepts<User>("application/json")
            .WithName("PutUser");

        group.MapDelete("/{id}", async (UserController controller, string id) => await controller.Delete(id))
            .WithName("DeleteUser");

        return group;
    }

    public static RouteGroupBuilder MapAuth(RouteGroupBuilder api)
    {
        var group = api;
        group.MapPost("/login", async (UserController controller, HttpContext ctx) => await controller.Login(ctx.Request.Body, GetClientIp(ctx)))
            .Accepts<User>("application/json")
            .WithName("LoginRoute")
            .WithDescription("Login using local db for authentication.")
            .WithTags("Login");

        group.MapPost("/ssologin", (UserController controller, HttpContext ctx) => controller.LoginWithLdap(ctx.Request.Body, GetClientIp(ctx)))
            .Accepts<User>("application/json")
            .WithName("LoginRouteWithLdap")
            .WithDescription("Login using LDAP provider for authentication.")
            .WithTags("Login");

        return group;
    }

    private static string GetClientIp(HttpContext ctx)
    {
        if (!ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            return ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        }
        return forwarded.ToString();
    }
}
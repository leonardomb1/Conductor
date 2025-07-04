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

        group.MapGet("/", async (UserController controller, HttpRequest request) =>
            await controller.Get(request.Query))
            .WithName("GetUsers")
            .WithSummary("Fetches a list of users.")
            .WithDescription("Returns a list of users filtered by optional query parameters. If no filters are provided, all users may be returned.")
            .Produces<Message<User>>(Status200OK, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapGet("/{id}", async (UserController controller, string id) =>
            await controller.GetById(id))
            .WithName("GetUserById")
            .WithSummary("Fetches a user by ID.")
            .WithDescription("Returns user information for a given numeric ID. If no user is found, a 200 OK with not-found message is returned.")
            .Produces<Message<User>>(Status200OK, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPost("/", async (UserController controller, HttpRequest request) =>
            await controller.Post(request.Body))
            .WithName("PostUser")
            .Accepts<User>("application/json")
            .WithSummary("Creates a new user.")
            .WithDescription("Creates a user based on the provided JSON body. Returns the URI of the created resource.")
            .Produces<Message>(Status201Created, "application/json")
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapPut("/{id}", async (UserController controller, HttpRequest request, string id) =>
            await controller.Put(id, request.Body))
            .WithName("PutUser")
            .Accepts<User>("application/json")
            .WithSummary("Updates an existing user.")
            .WithDescription("Updates user data identified by the given ID. Returns 204 No Content if successful.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        group.MapDelete("/{id}", async (UserController controller, string id) =>
            await controller.Delete(id))
            .WithName("DeleteUser")
            .WithSummary("Deletes a user.")
            .WithDescription("Deletes the user identified by the given ID. Returns 204 No Content if successful.")
            .Produces(Status204NoContent)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json");

        return group;
    }

    public static RouteGroupBuilder MapAuth(RouteGroupBuilder api)
    {
        var group = api;

        group.MapPost("/login", async (UserController controller, HttpContext ctx) =>
            await controller.Login(ctx.Request.Body, GetClientIp(ctx)))
            .WithName("LoginRoute")
            .WithSummary("Authenticate with local credentials.")
            .WithDescription("Authenticates the user using username/password stored in the local database. Returns a JWT token if successful.")
            .Accepts<User>("application/json")
            .Produces<string>(Status200OK, "application/json") // JWT Token
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces(Status401Unauthorized)
            .Produces<Message<Error>>(Status500InternalServerError, "application/json")
            .WithTags("Login");

        group.MapPost("/ssologin", (UserController controller, HttpContext ctx) =>
            controller.LoginWithLdap(ctx.Request.Body, GetClientIp(ctx)))
            .WithName("LoginRouteWithLdap")
            .WithSummary("Authenticate with LDAP credentials.")
            .WithDescription("Authenticates the user against an LDAP provider using the provided username and password. Returns a JWT token if authentication is successful.")
            .Accepts<User>("application/json")
            .Produces<string>(Status200OK, "application/json") // JWT Token
            .Produces(Status401Unauthorized)
            .Produces<Message>(Status400BadRequest, "application/json")
            .Produces<Message<Error>>(Status500InternalServerError, "application/json")
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
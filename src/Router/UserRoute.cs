using Conductor.Controller;
using Conductor.Model;

namespace Conductor.Router;

public static class UserRoute
{
    public static RouteGroupBuilder Add(RouteGroupBuilder api)
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
}
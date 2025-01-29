using Conductor.Shared;
using Conductor.Shared.Config;

namespace Conductor.Middleware
{
    public class AuthenticationMiddleware(RequestDelegate request)
    {
        private readonly RequestDelegate next = request;

        public async Task InvokeAsync(HttpContext ctx)
        {
            if (ctx.Request.Path.ToString().Contains("login"))
            {
                await next(ctx);
                return;
            }

            if (!ctx.Request.Headers.TryGetValue("Authorization", out var authorization))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Access denied.");
                return;
            }

            string[] keyValue = authorization.ToString().Split(" ");
            if (keyValue.Length != 2)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Access denied.");
                return;
            }

            if (keyValue[0] == "Key" && keyValue[1] == Settings.ApiKey)
            {
                await next(ctx);
                return;
            }

            if (keyValue[0] != "Bearer")
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Access denied.");
                return;
            }

            string client;
            if (!ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
            {
                client = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            }
            else
            {
                client = forwarded.ToString();
            }

            if (!Encryption.ValidateJwt(client, keyValue[1], Settings.EncryptionKey).IsSuccessful)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("");
                return;
            }

            await next(ctx);
        }
    }
}

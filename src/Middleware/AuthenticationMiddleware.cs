using System.Buffers.Text;
using System.Text;
using System.Text.Unicode;
using Conductor.Service;
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

            if (keyValue[0] == "Basic")
            {
                using UserService service = new(new Data.LdbContext());
                if (keyValue[1] == null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsync("Access denied.");
                    return;
                }

                byte[] decode = Convert.FromBase64String(keyValue[1]);
                string[] userData = Encoding.UTF8.GetString(decode).Split(":");

                var userLookup = await service.SearchUserCredential(userData[0]);
                if (!userLookup.IsSuccessful)
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsync("Access denied.");
                    return;
                }

                if (userLookup.Value != userData[1])
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsync("Access denied.");
                    return;
                }

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

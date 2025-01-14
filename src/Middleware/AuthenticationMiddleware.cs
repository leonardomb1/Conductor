using Conductor.Shared;
using Conductor.Shared.Config;
using static System.Net.HttpStatusCode;

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

            if (ctx.Request.Headers.TryGetValue("Key", out var key))
            {
                if (key == Settings.ApiKey)
                {
                    await next(ctx);
                    return;
                }
            }

            if (!ctx.Request.Headers.TryGetValue("Authorization", out var authorization))
            {
                ctx.Response.StatusCode = (Int32)Unauthorized;
                await ctx.Response.WriteAsync("");
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

            if (!Encryption.ValidateJwt(client, authorization!, Settings.EncryptionKey).IsSuccessful)
            {
                ctx.Response.StatusCode = (Int32)Unauthorized;
                await ctx.Response.WriteAsync("");
                return;
            }

            await next(ctx);
        }
    }
}

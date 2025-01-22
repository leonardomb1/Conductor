using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conductor.Controller;
using Conductor.Data;
using Conductor.Logging;
using Conductor.Middleware;
using Conductor.Service;
using Conductor.Shared;
using Conductor.Shared.Config;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.ResponseCompression;
using static Microsoft.AspNetCore.Http.HttpMethods;


namespace Conductor.Router;

public sealed class Server : IAsyncDisposable
{
    private readonly WebApplication app;

    private bool disposed = false;

    public Server()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Environment.ApplicationName = ProgramInfo.ProgramName;

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(Settings.ConnectionTimeout);
            options.Limits.MaxConcurrentConnections = Settings.MaxConcurrentConnections;
            options.Listen(IPAddress.Any, Settings.PortNumber, options =>
            {
                if (Settings.VerifyConnectionOrigin)
                {
                    options.Use(next => async ctx =>
                    {
                        try
                        {
                            var RemoteIpAddress = (ctx.RemoteEndPoint as IPEndPoint)?.Address;
                            if (RemoteIpAddress == null || !Settings.TcpAllowedIpsSet.Value.Contains(RemoteIpAddress.ToString()))
                            {
                                Log.Out(
                                    $"Blocking IP Address {RemoteIpAddress} from connecting to the server.",
                                    RecordType.Warning,
                                    callerMethod: "Kestrel"
                                );
                                ctx.Abort();
                                return;
                            }
                            await next(ctx);
                        }
                        catch (Exception ex)
                        {
                            Log.Out(
                                $"Error while attempting to block Remote IP from connecting: {ex.Message}, Stack Trace: {ex.StackTrace}",
                                RecordType.Error,
                                callerMethod: "Kestrel"
                            );
                        }
                    });
                }
                if (Settings.UseHttps)
                {
                    options.UseHttps(Settings.CertificatePath, Settings.CertificatePassword);
                }
            });
            options.AddServerHeader = false;
        });

        if (Settings.VerifyConnectionOrigin)
        {
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithOrigins([.. Settings.AllowedCorsSet.Value]);
                    policy.WithMethods([Get, Post, Put, Delete]);
                });
            });
        }

        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.WriteIndented = true;
        });

        builder.Services.AddResponseCaching(options =>
        {
            options.SizeLimit = Settings.ResponseCachingLimit;
        });

        builder.Services.AddResponseCompression(options =>
        {
            if (Settings.UseHttps) options.EnableForHttps = true;
            options.Providers.Add(
                new GzipCompressionProvider(
                    new GzipCompressionProviderOptions()
                    {
                        Level = System.IO.Compression.CompressionLevel.Fastest
                    }
                )
            );
        });

        if (Settings.DevelopmentMode)
        {
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApi();
        }

        /// Scoped Services
        builder.Services.AddScoped<LdbContext>();
        builder.Services.AddScoped<RecordService>();
        builder.Services.AddScoped<DestinationService>();
        builder.Services.AddScoped<OriginService>();
        builder.Services.AddScoped<UserService>();
        builder.Services.AddScoped<ExtractionService>();
        builder.Services.AddScoped<ScheduleService>();

        /// Controllers
        builder.Services.AddScoped<RecordController>();
        builder.Services.AddScoped<DestinationController>();
        builder.Services.AddScoped<OriginController>();
        builder.Services.AddScoped<UserController>();
        builder.Services.AddScoped<ExtractionController>();
        builder.Services.AddScoped<ScheduleController>();

        /// Custom logging
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);
        builder.Services.AddHostedService(provider =>
        {
            var ctx = new LdbContext();
            var logger = new RecordService(ctx);

            return new LoggingRoutine(logger);
        });

        Log.Out($"Starting {ProgramInfo.ProgramName} API Server...", callerMethod: "Server");
        app = builder.Build();

        /// Add Middleware and configuration
        app.UseMiddleware<LoggingMiddleware>();
        if (Settings.VerifyConnectionOrigin) app.UseMiddleware<WhiteListMiddleware>();
        if (Settings.RequireAuthentication) app.UseMiddleware<AuthenticationMiddleware>();
        if (Settings.DevelopmentMode)
        {
            app.MapOpenApi()
                .CacheOutput();
        }
        app.UseResponseCompression();

        /// Base API Route
        var api = app.MapGroup("/api");

        RecordRoute.Add(api);
        DestinationRoute.Add(api);
        OriginRoute.Add(api);
        UserRoute.Add(api);
        ScheduleRoute.Add(api);
        LoginRoute.Add(api);
        ExtractionRoute.Add(api);
        HealthRoute.Add(api);
    }

    public void Run()
    {
        Log.Out($"Server is listening at port: {Settings.PortNumber}.", callerMethod: "Server");
        app.Run();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsync(bool disposing)
    {
        if (!disposed && disposing)
        {
            await app.DisposeAsync();
            disposed = true;
        }
    }
}
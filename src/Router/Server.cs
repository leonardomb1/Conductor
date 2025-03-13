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
                if (Settings.VerifyTCP)
                {
                    options.Use(next => async ctx =>
                    {
                        IPAddress RemoteIpAddress = (ctx.RemoteEndPoint as IPEndPoint)!.Address;

                        if (
                            !Helper.VerifyIpAddress(
                                RemoteIpAddress,
                                "Socket Level Block",
                                () => ctx.Abort()
                            )
                        ) return;

                        await next(ctx);
                    });
                }

                if (Settings.UseHttps)
                {
                    options.UseHttps(Settings.CertificatePath, Settings.CertificatePassword);
                }
            });
            options.AddServerHeader = false;
        });

        if (Settings.VerifyCors)
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
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = $"{ProgramInfo.ProgramName} API",
                    Version = $"{ProgramInfo.ProgramVersion}",
                    Description = $"API Documentation for the {ProgramInfo.ProgramName} Project"
                });
            });
        }

        /// Scoped Services
        builder.Services.AddScoped<LdbContext>();
        builder.Services.AddScoped<RecordService>();
        builder.Services.AddScoped<DestinationService>();
        builder.Services.AddScoped<OriginService>();
        builder.Services.AddScoped<UserService>();
        builder.Services.AddScoped<ExtractionService>();
        builder.Services.AddScoped<ScheduleService>();
        builder.Services.AddScoped<JobService>();
        builder.Services.AddScoped<JobExtractionService>();

        /// Controllers
        builder.Services.AddScoped<RecordController>();
        builder.Services.AddScoped<DestinationController>();
        builder.Services.AddScoped<OriginController>();
        builder.Services.AddScoped<UserController>();
        builder.Services.AddScoped<ExtractionController>();
        builder.Services.AddScoped<ScheduleController>();
        builder.Services.AddScoped<JobController>();

        /// Custom logging
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);

        builder.Services.AddHostedService(provider =>
        {
            var ctx = new LdbContext();
            var logger = new RecordService(ctx);

            return new LoggingRoutine(logger);
        });

        builder.Services.AddHostedService(provider =>
        {
            var ctx = new LdbContext();
            var service = new JobService(ctx);
            var relatedService = new JobExtractionService(ctx);

            return new JobRoutine(service, relatedService);
        });

        string runningEnvironment = Environment.GetEnvironmentVariable("DOCKER_ENVIRONMENT") == null ? "CLI" : "Docker";
        string runningArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

        Log.Out(
            $"Starting {ProgramInfo.ProgramName} API Server {ProgramInfo.ProgramVersion} ({runningEnvironment} on {Environment.OSVersion} {runningArch}) at {Environment.MachineName}.",
            callerMethod: "Server"
        );

        app = builder.Build();

        /// Add Middleware and configuration
        app.UseMiddleware<LoggingMiddleware>();
        if (Settings.VerifyHttp) app.UseMiddleware<WhiteListMiddleware>();
        if (Settings.RequireAuthentication) app.UseMiddleware<AuthenticationMiddleware>();
        if (Settings.DevelopmentMode)
        {
            app.MapOpenApi()
                .CacheOutput();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "Conductor API v1");
                options.EnableFilter();
                options.RoutePrefix = "api/swagger";
                options.DocumentTitle = $"{ProgramInfo.ProgramName} API Docs";
            });
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
        JobRoute.Add(api);
    }

    public void Run()
    {
        Log.Out($"Listening on IPv4 address \"0.0.0.0\", port: {Settings.PortNumber}.", callerMethod: "Server");
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
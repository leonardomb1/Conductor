using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conductor.Controller;
using Conductor.Logging;
using Conductor.Middleware;
using Conductor.Repository;
using Conductor.Service.Script;
using Conductor.Shared;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.ResponseCompression;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Conductor.Router;

public sealed class Server : IAsyncDisposable
{
    private readonly WebApplication app;

    private bool disposed = false;

    public Server()
    {
        if (Settings.LogLevelDebug)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .Enrich.FromLogContext()
                .CreateLogger();
        }
        else
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .Enrich.FromLogContext()
                .CreateLogger();
        }

        var builder = WebApplication.CreateBuilder();
        builder.Host.UseSerilog();

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

                        byte validations = 0;

                        for (byte i = 0; i < Settings.AllowedIpsRange.Value.Length; i++)
                        {
                            if (!Settings.AllowedIpsRange.Value[i].Contains(RemoteIpAddress)) validations++;
                        }

                        if (validations == Settings.AllowedIpsRange.Value.Length)
                        {
                            Log.Warning($"Blocking IP Address {RemoteIpAddress} from connecting to the server.");
                            ctx.Abort();
                            return;
                        }

                        await next(ctx);
                    });
                }

                if (Settings.UseHttps)
                {
                    options.UseHttps(httpsOptions =>
                    {
                        httpsOptions.ServerCertificate = X509Certificate2.CreateFromPemFile(Settings.CertificatePath, Settings.CertificatePassword);
                    });
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
                    policy.WithOrigins([.. Settings.AllowedCorsSet.Value])
                        .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS", "HEAD", "PATCH")
                        .WithHeaders("Content-Type", "Authorization", "X-Requested-With", "Accept", "Origin")
                        .AllowCredentials();
                });
            });
        }
        else
        {
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader();
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

        /// Http Client
        builder.Services.AddHttpClient($"{ProgramInfo.ProgramName}Client", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", $"{ProgramInfo.ProgramName}/{ProgramInfo.ProgramVersion}");
        });

        /// Scoped Services
        builder.Services.AddScoped<EfContext>();
        builder.Services.AddScoped<DestinationRepository>();
        builder.Services.AddScoped<OriginRepository>();
        builder.Services.AddScoped<UserRepository>();
        builder.Services.AddScoped<ExtractionRepository>();
        builder.Services.AddScoped<ScheduleRepository>();
        builder.Services.AddScoped<JobRepository>();
        builder.Services.AddScoped<JobExtractionRepository>();

        /// Singleton Services
        builder.Services.AddSingleton<IScriptEngine, RoslynScriptEngine>();
        builder.Services.AddSingleton<IJobTracker, JobTracker>();

        /// Controllers
        builder.Services.AddScoped<DestinationController>();
        builder.Services.AddScoped<OriginController>();
        builder.Services.AddScoped<UserController>();
        builder.Services.AddScoped<ExtractionController>();
        builder.Services.AddScoped<ScheduleController>();
        builder.Services.AddScoped<JobController>();



        string runningEnvironment = Environment.GetEnvironmentVariable("DOCKER_ENVIRONMENT") is null ? "CLI" : "Docker";
        string runningArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

        Log.Information(
            $"Starting {ProgramInfo.ProgramName} API Server {ProgramInfo.ProgramVersion} ({runningEnvironment} on {Environment.OSVersion} {runningArch}) at {Environment.MachineName}."
        );

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracerProviderBuilder =>
            {
                tracerProviderBuilder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource($"{ProgramInfo.ProgramName}")
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ProgramInfo.ProgramName));
            })
            .WithMetrics(metricsBuilder =>
            {
                metricsBuilder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddPrometheusExporter()
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ProgramInfo.ProgramName));
            });

        app = builder.Build();

        if (Settings.VerifyCors)
        {
            app.UseCors();
        }
        else
        {
            app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        }

        /// Add Middleware and configuration
        if (Settings.RequireAuthentication) app.UseMiddleware<AuthenticationMiddleware>();
        if (Settings.DevelopmentMode)
        {
            app.MapOpenApi()
                .CacheOutput();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", $"{ProgramInfo.ProgramName} API v1");
                options.EnableFilter();
                options.EnableValidator();
                options.RoutePrefix = "api/swagger";
                options.DocumentTitle = $"{ProgramInfo.ProgramName} API Docs";
            });
        }
        app.UseResponseCompression();

        /// Base API Route
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.Now })).WithName("HealthCheck");
        api.MapPrometheusScrapingEndpoint("/metrics");

        DestinationController.Map(api);
        OriginController.Map(api);
        UserController.Map(api);
        UserController.MapAuth(api);
        ScheduleController.Map(api);
        ExtractionController.Map(api);
        JobController.Map(api);
    }

    public void Run()
    {
        try
        {
            app.Run();
        }
        finally
        {
            Log.CloseAndFlush();
        }
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
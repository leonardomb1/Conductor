using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conductor.Controller;
using Conductor.Logging;
using Conductor.Middleware;
using Conductor.Repository;
using Conductor.Service;
using Conductor.Service.Database;
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
    private readonly Meter serverMeter;
    private readonly Counter<int> requestsCounter;
    private readonly Counter<int> errorsCounter;
    private readonly Gauge<int> activeJobsGauge;
    private readonly Histogram<double> requestDurationHistogram;
    private bool disposed = false;

    public Server()
    {
        serverMeter = new Meter("Conductor.Server", "1.0.0");

        requestsCounter = serverMeter.CreateCounter<int>(
            "conductor_http_requests_total",
            description: "Total number of HTTP requests");

        errorsCounter = serverMeter.CreateCounter<int>(
            "conductor_http_errors_total",
            description: "Total number of HTTP errors");

        activeJobsGauge = serverMeter.CreateGauge<int>(
            "conductor_active_jobs",
            description: "Current number of active jobs");

        requestDurationHistogram = serverMeter.CreateHistogram<double>(
            "conductor_http_request_duration_seconds",
            "s",
            "Duration of HTTP requests in seconds");

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
        builder.Services.AddSingleton<IJobTracker, JobTracker>();
        builder.Services.AddSingleton<IConnectionPoolManager, ConnectionPoolManager>();
        builder.Services.AddSingleton<IDataTableMemoryManager, DataTableMemoryManager>();
        builder.Services.AddSingleton<IScriptEngine, RoslynScriptEngine>();

        // Register server metrics as singleton for dependency injection
        builder.Services.AddSingleton(serverMeter);
        builder.Services.AddSingleton(requestsCounter);
        builder.Services.AddSingleton(errorsCounter);
        builder.Services.AddSingleton(activeJobsGauge);
        builder.Services.AddSingleton(requestDurationHistogram);

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
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity.SetTag("http.request.body.size", request.ContentLength);
                            activity.SetTag("http.client.ip", request.HttpContext.Connection.RemoteIpAddress?.ToString());
                        };
                        options.EnrichWithHttpResponse = (activity, response) =>
                        {
                            activity.SetTag("http.response.body.size", response.ContentLength);
                        };
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            activity.SetTag("http.request.method", request.Method.Method);
                            activity.SetTag("http.request.uri", request.RequestUri?.ToString());
                        };
                        options.EnrichWithHttpResponseMessage = (activity, response) =>
                        {
                            activity.SetTag("http.response.status_code", (int)response.StatusCode);
                            activity.SetTag("http.response.body.size", response.Content.Headers.ContentLength);
                        };
                    })
                    .AddSource($"{ProgramInfo.ProgramName}")
                    .AddSource("Conductor.ExtractionPipeline")
                    .AddSource("Conductor.ConnectionPool")
                    .AddSource("Conductor.DataTableMemory")
                    .AddSource("Conductor.JobTracker")
                    .AddSource("Conductor.HTTPExchange")
                    .AddSource("Conductor.ScriptEngine")
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(ProgramInfo.ProgramName, ProgramInfo.ProgramVersion)
                        .AddAttributes(
                        [
                            new KeyValuePair<string, object>("service.instance.id", Environment.MachineName),
                            new KeyValuePair<string, object>("service.version", ProgramInfo.ProgramVersion),
                            new KeyValuePair<string, object>("deployment.environment", Settings.DevelopmentMode ? "development" : "production")
                        ]));

                if (Settings.DevelopmentMode)
                {
                    tracerProviderBuilder.AddConsoleExporter();
                }
            })
            .WithMetrics(metricsBuilder =>
            {
                metricsBuilder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddMeter("Conductor.Server")
                    .AddMeter("Conductor.DataTableMemory")
                    .AddMeter("Conductor.ConnectionPool")
                    .AddMeter("Conductor.ExtractionPipeline")
                    .AddMeter("Conductor.JobTracker")
                    .AddMeter("Conductor.HTTPExchange")
                    .AddMeter("Conductor.ScriptEngine")
                    .AddMeter("System.Net.Http")
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                    .AddPrometheusExporter()
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(ProgramInfo.ProgramName, ProgramInfo.ProgramVersion)
                        .AddAttributes(
                        [
                            new KeyValuePair<string, object>("service.instance.id", Environment.MachineName),
                            new KeyValuePair<string, object>("service.version", ProgramInfo.ProgramVersion),
                            new KeyValuePair<string, object>("deployment.environment", Settings.DevelopmentMode ? "development" : "production")
                        ]));

                if (Settings.DevelopmentMode)
                {
                    metricsBuilder.AddConsoleExporter();
                }
            });

        app = builder.Build();

        app.Use(async (context, next) =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? "unknown";

            try
            {
                await next();

                requestsCounter.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("method", method),
                    new KeyValuePair<string, object?>("path", path),
                    new KeyValuePair<string, object?>("status_code", context.Response.StatusCode.ToString())
                });
            }
            catch (Exception)
            {
                errorsCounter.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("method", method),
                    new KeyValuePair<string, object?>("path", path)
                });
                throw;
            }
            finally
            {
                stopwatch.Stop();
                requestDurationHistogram.Record(stopwatch.Elapsed.TotalSeconds, new[]
                {
                    new KeyValuePair<string, object?>("method", method),
                    new KeyValuePair<string, object?>("path", path)
                });
            }
        });

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

        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.Now })).WithName("HealthCheck");

        api.MapPrometheusScrapingEndpoint("/metrics");

        api.MapGet("/metrics/json", (
            IJobTracker jobTracker,
            IConnectionPoolManager poolManager,
            IDataTableMemoryManager memoryManager) =>
        {
            var poolStats = poolManager.GetPoolStats();
            var memoryStats = memoryManager.GetMemoryStats();
            var activeJobs = jobTracker.GetActiveJobs().Count();

            return Results.Ok(new
            {
                timestamp = DateTime.UtcNow,
                jobs = new { active = activeJobs },
                connectionPools = new
                {
                    totalPools = poolStats.TotalActivePools,
                    totalConnections = poolStats.TotalActiveConnections,
                    poolSize = poolStats.TotalPoolSize
                },
                dataTables = new
                {
                    activeTables = memoryStats.TotalActiveTables,
                    estimatedMemoryMB = memoryStats.TotalEstimatedMemoryMB
                }
            });
        }).WithName("MetricsJson");

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
            serverMeter?.Dispose();
            await app.DisposeAsync();
            disposed = true;
        }
    }
}
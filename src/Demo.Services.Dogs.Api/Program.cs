using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using Demo.Libs.Dogs;
using Demo.Services.Dogs.Api.Dto;
using Demo.Services.Dogs.Api.Extensions;
using Demo.Services.Dogs.Db;
using Hellang.Middleware.ProblemDetails;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using Refit;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using Serilog.Exceptions.Destructurers;
using Serilog.Exceptions.EntityFrameworkCore.Destructurers;
using Serilog.Exceptions.Refit.Destructurers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<DogsDbContext>(options =>
{
    options.UseSqlite("Data Source=Dogs.db");
});


builder.Services.AddRefitClient<IDogsClient>()
    .ConfigureHttpClient(httpClient => httpClient.BaseAddress = new Uri("https://dog.ceo/"))
    .AddPolicyHandler(message => {
        var delay = Backoff.DecorrelatedJitterBackoffV2(medianFirstRetryDelay: TimeSpan.FromSeconds(1), retryCount: 3);
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(delay);
    })
    .AddPolicyHandler(message => HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(10, TimeSpan.FromSeconds(30)));

// Logging
builder.Host.UseSerilog((_, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithSpan()
    .Enrich.WithExceptionDetails(new DestructuringOptionsBuilder()
        .WithDefaultDestructurers()
        .WithDestructurers(new IExceptionDestructurer[]
        {
            new DbUpdateExceptionDestructurer(),
            new ApiExceptionDestructurer()
        }))
    .Enrich.WithDemystifiedStackTraces()
    .MinimumLevel.Override("System.Net.Http.HttpClient.Refit.Implementation.Generated+DemoLibsDogsIDogsClient, Demo.Libs.Dogs, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null.LogicalHandler", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware", LogEventLevel.Debug)
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Infrastructure", LogEventLevel.Warning));

builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
});


// Traces
builder.Services.AddOpenTelemetryTracing(options =>
{
    options.ConfigureResource(resourceBuilder =>
    {
        resourceBuilder.AddService(
            builder.Environment.ApplicationName,
            builder.Environment.EnvironmentName,
            builder.Configuration["OpenTelemetry:ApplicationVersion"],
            false,
            Environment.MachineName);
        resourceBuilder.AddTelemetrySdk();
        resourceBuilder.AddEnvironmentVariableDetector();
    })
    .AddHttpClientInstrumentation(instrumentationOptions =>
    {
        instrumentationOptions.RecordException = true;
    })
    .AddAspNetCoreInstrumentation(instrumentationOptions =>
    {
        instrumentationOptions.RecordException = true;
    })
    .AddSqlClientInstrumentation(instrumentationOptions =>
    {
        instrumentationOptions.RecordException = true;
        instrumentationOptions.SetDbStatementForText = true;
    })
    .AddEntityFrameworkCoreInstrumentation(instrumentationOptions =>
    {
        instrumentationOptions.SetDbStatementForText = true;
    })
    .AddOtlpExporter(opt =>
    {
        opt.Protocol = OtlpExportProtocol.Grpc;
        opt.Endpoint = new Uri(builder.Configuration["OpenTelemetry:Exporter:Otlp:Endpoint"]);
    });
});

// Metrics
builder.Services.AddOpenTelemetryMetrics(options =>
{
    options.ConfigureResource(resourceBuilder =>
    {
        resourceBuilder.AddService(
            builder.Environment.ApplicationName,
            builder.Environment.EnvironmentName,
            builder.Configuration["OpenTelemetry:ApplicationVersion"],
            false,
            Environment.MachineName);
        resourceBuilder.AddTelemetrySdk();
        resourceBuilder.AddEnvironmentVariableDetector();
    })
    .AddHttpClientInstrumentation()
    .AddAspNetCoreInstrumentation()
    .AddRuntimeInstrumentation()
    .AddPrometheusExporter();
});


// API Error Handler
builder.Services.AddProblemDetails(options =>
{
    options.IncludeExceptionDetails = (_, _) => builder.Environment.IsDevelopment();
    options.MapToStatusCode<NotImplementedException>(StatusCodes.Status501NotImplemented);
    options.MapToStatusCode<AuthenticationException>(StatusCodes.Status401Unauthorized);
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddDiskStorageHealthCheck(_ => { }, tags: new[] { "live", "ready" })
    .AddPingHealthCheck(_ => { }, tags: new[] { "live", "ready" })
    .AddPrivateMemoryHealthCheck(512 * 1024 * 1024, tags: new[] { "live", "ready" })
    .AddDnsResolveHealthCheck(_ => { }, tags: new[] { "live", "ready" })
    .AddDbContextCheck<DogsDbContext>(tags: new[] { "ready" });




var app = builder.Build();

app.UseHttpLogging();
app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseProblemDetails();

app.MapHealthChecks("health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthChecksLogWriters.WriteResponseAsync
});

app.MapHealthChecks("health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthChecksLogWriters.WriteResponseAsync
});

app.MapHealthChecks("health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = HealthChecksLogWriters.WriteResponseAsync
});

app.UseHealthChecksPrometheusExporter("/health/prometheus", options =>
{
    options.ResultStatusCodes[HealthStatus.Unhealthy] = 200;
});

app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics/prometheus");

app.MapGet("/api/v1/dogs/new", GetRandomDogImageAsync);
app.MapGet("/api/v1/dogs/cached", GetCachedDogImagesAsync);
app.MapGet("/api/v1/fail500", FailHttp500Async);

app.Run();

static async Task<DogImage> GetRandomDogImageAsync(
    IDogsClient dogsClient,
    DogsDbContext dbContext,
    CancellationToken cancellationToken)
{
    var apiResponse = await dogsClient.GetRandomDogImageAsync(cancellationToken);
    await apiResponse.EnsureSuccessStatusCodeAsync();
    await dbContext.Dogs.AddAsync(new Demo.Services.Dogs.Db.Entities.DogImage
    {
        Url = apiResponse.Content?.Message
    }, cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);
    return apiResponse.Content;
}

static async Task<SearchResult<DogImage>> GetCachedDogImagesAsync(
    [FromQuery] int? skip,
    [FromQuery] int? take,
    DogsDbContext dbContext,
    CancellationToken cancellationToken)
{
    var query = dbContext.Dogs.AsQueryable();
    var entities = await query
        .OrderBy(i => i.Id)
        .Skip(skip ?? 0)
        .Take(take ?? 20)
        .ToListAsync(cancellationToken);
    
    var result = new SearchResult<DogImage>
    {
        TotalCount = await query.CountAsync(cancellationToken),
        Values = entities.Select(i => new DogImage
        {
            Message = i.Url
        })
    };
    return result;
}

static async Task<IResult> FailHttp500Async(
    IDogsClient dogsClient,
    CancellationToken cancellationToken)
{
    var response = await dogsClient.FailHttp500Async(cancellationToken);
    await response.EnsureSuccessStatusCodeAsync();
    return Results.Ok();
}
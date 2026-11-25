using System.Text.Json;
using System.Text.Json.Serialization;
using ALNoor.Api.Features.Dev;
using ALNoor.Api.Features.LiveKit;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Web;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Secrets come from environment variables or `dotnet user-secrets`, never from appsettings.
// Configuration is read when services are built (not here), so every configuration source applies.
builder.Services.AddOptions<DatabaseOptions>()
    .Configure<IConfiguration>((options, configuration) => options.ConnectionString = configuration.GetConnectionString("Default"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString),
        "Connection string 'ConnectionStrings:Default' is not configured. See README (First-time setup).")
    .ValidateOnStart();

builder.Services.AddDbContext<AlNoorDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AlNoorDbContext>("database", tags: ["ready"]);

builder.Services.AddAlNoorProblemDetails();
// Request validation for minimal APIs (DataAnnotations on request records); failures become 400 ProblemDetails.
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddLiveKit(builder.Configuration);
builder.Services.AddOpenApi();

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
{
    var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Unhandled errors become RFC 9457 ProblemDetails without stack traces or internals.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Malformed request bodies are client errors (400), not server errors.
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseSecurityHeaders();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Phase 2 proof of concept only: unauthenticated token endpoint. Never mapped outside Development.
    app.MapDevLiveKitEndpoints();
}

// Liveness: the process is up. No dependencies checked.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
// Readiness (used by uptime monitoring, PRD section 22.3): includes the database.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

/// <summary>Entry point marker used by integration tests (WebApplicationFactory).</summary>
public partial class Program;

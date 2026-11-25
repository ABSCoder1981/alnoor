using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Secrets come from environment variables or `dotnet user-secrets`, never from appsettings.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:Default' is not configured. See README (Backend setup).");

builder.Services.AddDbContext<AlNoorDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AlNoorDbContext>("database", tags: ["ready"]);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Unhandled errors become RFC 9457 ProblemDetails without stack traces or internals.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness: the process is up. No dependencies checked.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
// Readiness (used by uptime monitoring, PRD section 22.3): includes the database.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

/// <summary>Entry point marker used by integration tests (WebApplicationFactory).</summary>
public partial class Program;

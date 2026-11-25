using System.Text.Json.Serialization;
using ALNoor.Api.Features.Admin;
using ALNoor.Api.Features.Auth;
using ALNoor.Api.Features.Classes;
using ALNoor.Api.Features.Dev;
using ALNoor.Api.Features.LiveKit;
using ALNoor.Api.Features.Students;
using ALNoor.Api.Shared.Audit;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Security;
using ALNoor.Api.Shared.Tenancy;
using ALNoor.Api.Shared.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Secrets come from environment variables or `dotnet user-secrets`, never from appsettings.
// Configuration is read when services are built (not here), so every configuration source applies.
builder.Services.AddOptions<DatabaseOptions>()
    .Configure<IConfiguration>((options, configuration) => options.ConnectionString = configuration.GetConnectionString("Default"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString),
        "Connection string 'ConnectionStrings:Default' is not configured. See README (First-time setup).")
    .ValidateOnStart();

builder.Services.AddScoped<TenantContext>();
builder.Services.AddDbContext<AlNoorDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AlNoorDbContext>("database", tags: ["ready"]);

builder.Services.AddAlNoorProblemDetails();
// Request validation for minimal APIs (DataAnnotations on request records); failures become 400 ProblemDetails.
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Enums as names ("Student"); reading is case-insensitive. Integers are rejected.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter());
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddLiveKit(builder.Configuration);
builder.Services.AddOpenApi();

// --- Authentication (PRD 17) ---------------------------------------------------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.SigningKeyBytes().Length >= 32, "Jwt:SigningKey must be base64 of at least 32 random bytes.")
    .ValidateOnStart();
builder.Services.AddOptions<LoginPolicyOptions>()
    .Bind(builder.Configuration.GetSection(LoginPolicyOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false; // Keep "sub", "role" as issued.
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.Value.SigningKeyBytes()),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<AccessTokenService>();
builder.Services.AddSingleton<LoginRateLimiter>();
builder.Services.AddScoped<TokenIssuer>();
builder.Services.AddScoped<AuditLogger>();
builder.Services.AddScoped<CurrentUser>();

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

// Developer commands (seed-teacher, reset-teacher-password) run instead of the web server.
if (AdminCommands.IsCommand(args))
{
    return await AdminCommands.RunAsync(app.Services, args, Console.Out, AdminCommands.ReadPasswordFromEnvironmentOrConsole);
}

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
app.UseAuthentication();
app.UseAuthorization();

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

app.MapAuthEndpoints();
app.MapStudentEndpoints();
app.MapClassEndpoints();

await app.RunAsync();
return 0;

/// <summary>Entry point marker used by integration tests (WebApplicationFactory).</summary>
public partial class Program;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the API in memory. Test configuration is added last, so it overrides appsettings and any
/// developer user-secrets that load in the Development environment.
/// </summary>
public sealed class ApiFactory(string connectionString, string environment = "Testing") : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "https://app.test.example";
    public const string LiveKitUrl = "ws://livekit.test.example:7880";
    public const string LiveKitApiKey = "test-api-key";
    public const string LiveKitApiSecret = "test-secret-0123456789-abcdefghijklmnop";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Cors:AllowedOrigins:0"] = AllowedOrigin,
            ["LiveKit:Url"] = LiveKitUrl,
            ["LiveKit:ApiKey"] = LiveKitApiKey,
            ["LiveKit:ApiSecret"] = LiveKitApiSecret,
        }));
    }

    /// <summary>A Npgsql connection string to a port where nothing listens, with a short timeout.</summary>
    public static string UnreachableDatabase =>
        "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";
}

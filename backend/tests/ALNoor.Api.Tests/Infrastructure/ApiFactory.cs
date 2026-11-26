using ALNoor.Api.Features.LiveKit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the API in memory. Test configuration is added last, so it overrides appsettings and any
/// developer user-secrets that load in the Development environment.
/// </summary>
public sealed class ApiFactory(string connectionString, string environment = "Testing", IDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "https://app.test.example";
    public const string LiveKitUrl = "ws://livekit.test.example:7880";
    public const string LiveKitApiKey = "test-api-key";
    public const string LiveKitApiSecret = "test-secret-0123456789-abcdefghijklmnop";

    /// <summary>In-memory LiveKit used by this API instance (replaces the real RoomService client).</summary>
    public FakeLiveKitRoomService Rooms { get; } = new();

    /// <summary>Base64 of 48 fixed bytes; test-only signing key.</summary>
    public static readonly string JwtSigningKey = Convert.ToBase64String(Enumerable.Range(1, 48).Select(i => (byte)i).ToArray());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Cors:AllowedOrigins:0"] = AllowedOrigin,
            ["LiveKit:Url"] = LiveKitUrl,
            ["LiveKit:ApiKey"] = LiveKitApiKey,
            ["LiveKit:ApiSecret"] = LiveKitApiSecret,
            ["Jwt:SigningKey"] = JwtSigningKey,
            // Generous limits so normal tests are not throttled; rate-limit tests override these.
            ["Auth:LoginPolicy:LoginRequestsPerMinutePerIp"] = "10000",
            ["Auth:LoginPolicy:LoginRequestsPerMinutePerPhone"] = "10000",
        };
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services => services.AddSingleton<ILiveKitRoomService>(Rooms));
    }

    /// <summary>A Npgsql connection string to a port where nothing listens, with a short timeout.</summary>
    public static string UnreachableDatabase =>
        "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";
}

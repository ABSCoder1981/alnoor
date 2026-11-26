using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>
/// Starts a real LiveKit server (same version as scripts/livekit-dev.ps1) with test keys, so tests
/// can prove the server accepts tokens we sign. Uses LiveKit's /rtc/validate endpoint.
/// </summary>
public sealed class LiveKitServerFixture : IAsyncLifetime
{
    public const string Image = "livekit/livekit-server:v1.13.11";
    public const string ApiKey = "fixture-key";
    public const string ApiSecret = "fixture-secret-0123456789-abcdefghijklmnop";
    private const int HttpPort = 7880;

    private IContainer? _container;

    public Uri? HttpBaseUrl { get; private set; }

    public string? StartError { get; private set; }

    /// <summary>Optional: http URL of an already running livekit-server started with the fixture keys (no Docker needed).</summary>
    public const string ServerVariable = "ALNOOR_TEST_LIVEKIT";

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(ServerVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            HttpBaseUrl = new Uri(external);
            return;
        }

        try
        {
            _container = new ContainerBuilder(Image)
                // Rooms are created only by the API, as in production (no auto-create on join).
                .WithCommand("--dev", "--bind", "0.0.0.0", "--config-body", "room:\n  auto_create: false")
                .WithEnvironment("LIVEKIT_KEYS", $"{ApiKey}: {ApiSecret}")
                .WithPortBinding(HttpPort, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(HttpPort).ForPath("/")))
                .Build();
            await _container.StartAsync(TestContext.Current.CancellationToken);
            HttpBaseUrl = new UriBuilder("http", _container.Hostname, _container.GetMappedPublicPort(HttpPort)).Uri;
        }
#pragma warning disable CA1031 // Any start failure means "Docker unavailable"; it is reported, not swallowed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StartError = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public void SkipIfUnavailable() =>
        DockerRequirement.SkipOrFailIfUnavailable(HttpBaseUrl is not null, "LiveKit server", StartError);

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

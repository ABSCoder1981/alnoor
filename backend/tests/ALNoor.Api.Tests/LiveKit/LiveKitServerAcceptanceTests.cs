using System.Net;
using ALNoor.Api.Features.LiveKit;
using ALNoor.Api.Tests.Infrastructure;

namespace ALNoor.Api.Tests.LiveKit;

/// <summary>Proves a real LiveKit server accepts our tokens and rejects bad ones.</summary>
public sealed class LiveKitServerAcceptanceTests(LiveKitServerFixture server) : IClassFixture<LiveKitServerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpStatusCode> ValidateAsync(string token)
    {
        using var http = new HttpClient { BaseAddress = server.HttpBaseUrl };
        using var response = await http.GetAsync(new Uri($"/rtc/validate?access_token={Uri.EscapeDataString(token)}", UriKind.Relative), Ct);
        return response.StatusCode;
    }

    private static LiveKitJoinRequest Request => new("user-1", "Aisha", "s_test", ParticipantRole.Student);

    [Fact]
    public async Task Server_accepts_token_signed_with_its_secret()
    {
        server.SkipIfUnavailable();
        var service = LiveKitTokenServiceTests.CreateService(LiveKitServerFixture.ApiKey, LiveKitServerFixture.ApiSecret, TimeProvider.System);

        var status = await ValidateAsync(service.CreateJoinToken(Request).Token);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Server_rejects_token_signed_with_wrong_secret()
    {
        server.SkipIfUnavailable();
        var service = LiveKitTokenServiceTests.CreateService(LiveKitServerFixture.ApiKey, "wrong-secret-0123456789-abcdefghijklmnop", TimeProvider.System);

        var status = await ValidateAsync(service.CreateJoinToken(Request).Token);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task Server_rejects_expired_token()
    {
        server.SkipIfUnavailable();
        var tenMinutesAgo = new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-10));
        var service = LiveKitTokenServiceTests.CreateService(LiveKitServerFixture.ApiKey, LiveKitServerFixture.ApiSecret, tenMinutesAgo);

        var status = await ValidateAsync(service.CreateJoinToken(Request).Token);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }
}

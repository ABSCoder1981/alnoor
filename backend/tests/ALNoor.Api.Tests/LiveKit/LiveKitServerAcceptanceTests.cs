using System.Net;
using ALNoor.Api.Features.LiveKit;
using ALNoor.Api.Tests.Infrastructure;
using Microsoft.Extensions.Options;

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

    /// <summary>Rooms are not auto-created (as in production), so the room must exist before a token can be used.</summary>
    private async Task CreateRoomAsync(string room)
    {
        var ws = new UriBuilder(server.HttpBaseUrl!) { Scheme = "ws" }.Uri;
        var options = Options.Create(new LiveKitOptions { Url = ws, ApiKey = LiveKitServerFixture.ApiKey, ApiSecret = LiveKitServerFixture.ApiSecret });
        using var http = new HttpClient();
        await new LiveKitRoomService(http, new LiveKitTokenService(options, TimeProvider.System), options).CreateRoomAsync(room, Ct);
    }

    [Fact]
    public async Task Token_for_a_room_that_was_never_created_is_refused()
    {
        server.SkipIfUnavailable();
        var service = LiveKitTokenServiceTests.CreateService(LiveKitServerFixture.ApiKey, LiveKitServerFixture.ApiSecret, TimeProvider.System);

        var status = await ValidateAsync(service.CreateJoinToken(Request with { Room = $"s_gone_{Guid.NewGuid():N}" }).Token);

        // An old token cannot re-create an ended class room.
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Server_accepts_token_signed_with_its_secret()
    {
        server.SkipIfUnavailable();
        await CreateRoomAsync(Request.Room);
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

using ALNoor.Api.Features.LiveKit;
using ALNoor.Api.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace ALNoor.Api.Tests.LiveKit;

/// <summary>
/// Runs our RoomService client against a real LiveKit server, so the Twirp paths, JSON fields and server-token grants
/// are proven, not assumed (ADR 0001). Participant-level calls need a WebRTC client and are covered with the fake.
/// </summary>
public sealed class LiveKitRoomServiceContractTests(LiveKitServerFixture server) : IClassFixture<LiveKitServerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LiveKitRoomService Client(string secret = LiveKitServerFixture.ApiSecret)
    {
        var ws = new UriBuilder(server.HttpBaseUrl!) { Scheme = "ws" }.Uri;
        var options = Options.Create(new LiveKitOptions { Url = ws, ApiKey = LiveKitServerFixture.ApiKey, ApiSecret = secret });
        return new LiveKitRoomService(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, new LiveKitTokenService(options, TimeProvider.System), options);
    }

    private static string NewRoom() => $"s_test_{Guid.NewGuid():N}"[..30];

    [Fact]
    public async Task Room_lifecycle_works_against_a_real_server()
    {
        server.SkipIfUnavailable();
        var rooms = Client();
        var room = NewRoom();

        await rooms.CreateRoomAsync(room, Ct);
        Assert.Empty(await rooms.ListParticipantsAsync(room, Ct));

        await rooms.RemoveParticipantAsync(room, Guid.NewGuid().ToString(), Ct); // Absent participant: no error.
        await rooms.DeleteRoomAsync(room, Ct);
        await rooms.DeleteRoomAsync(room, Ct); // Already gone: no error.
    }

    [Fact]
    public async Task Listing_a_room_that_does_not_exist_returns_no_participants()
    {
        server.SkipIfUnavailable();

        Assert.Empty(await Client().ListParticipantsAsync(NewRoom(), Ct));
    }

    [Fact]
    public async Task Wrong_secret_is_reported_as_unavailable_not_swallowed()
    {
        server.SkipIfUnavailable();

        await Assert.ThrowsAsync<LiveKitUnavailableException>(() =>
            Client("wrong-secret-0123456789-abcdefghijklmnop").CreateRoomAsync(NewRoom(), Ct));
    }

    [Fact]
    public async Task Unreachable_server_is_reported_as_unavailable()
    {
        var options = Options.Create(new LiveKitOptions { Url = new Uri("ws://127.0.0.1:1"), ApiKey = "k", ApiSecret = LiveKitServerFixture.ApiSecret });
        var rooms = new LiveKitRoomService(new HttpClient { Timeout = TimeSpan.FromSeconds(3) }, new LiveKitTokenService(options, TimeProvider.System), options);

        await Assert.ThrowsAsync<LiveKitUnavailableException>(() => rooms.CreateRoomAsync("s_x", Ct));
    }

    [Theory]
    [InlineData("ws://192.168.1.8:7880", "http://192.168.1.8:7880/")]
    [InlineData("wss://alnoor.livekit.cloud", "https://alnoor.livekit.cloud/")]
    public void Client_url_maps_to_the_http_api_base(string ws, string expected)
    {
        Assert.Equal(expected, LiveKitRoomService.HttpBase(new Uri(ws)).ToString());
    }
}

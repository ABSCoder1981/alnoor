using System.Text;
using System.Text.Json;
using ALNoor.Api.Features.LiveKit;
using ALNoor.Api.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ALNoor.Api.Tests.LiveKit;

public sealed class LiveKitTokenServiceTests
{
    private const string Secret = "unit-test-secret-0123456789-abcdefghij";
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    internal static LiveKitTokenService CreateService(string apiKey, string secret, TimeProvider? time = null) =>
        new(Options.Create(new LiveKitOptions { Url = new Uri("ws://localhost:7880"), ApiKey = apiKey, ApiSecret = secret }),
            time ?? new FixedTimeProvider(Now));

    private static JsonElement DecodePayload(string token)
    {
        var payload = token.Split('.')[1];
        var json = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(payload));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task Token_is_signed_with_api_secret_and_identifies_participant()
    {
        var service = CreateService("my-key", Secret);

        var result = service.CreateJoinToken(new LiveKitJoinRequest("user-1", "Aisha", "s_abc", ParticipantRole.Student));

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(result.Token, new TokenValidationParameters
        {
            ValidIssuer = "my-key",
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
        });
        Assert.True(validation.IsValid, validation.Exception?.Message);

        var payload = DecodePayload(result.Token);
        Assert.Equal("user-1", payload.GetProperty("sub").GetString());
        Assert.Equal("Aisha", payload.GetProperty("name").GetString());
        Assert.Equal("student", payload.GetProperty("attributes").GetProperty("role").GetString());
    }

    [Fact]
    public void Token_expires_after_five_minutes()
    {
        var result = CreateService("k", Secret).CreateJoinToken(new LiveKitJoinRequest("u", "N", "r", ParticipantRole.Teacher));

        Assert.Equal(Now.AddMinutes(5), result.ExpiresAt);
        var payload = DecodePayload(result.Token);
        Assert.Equal(Now.AddMinutes(5).ToUnixTimeSeconds(), payload.GetProperty("exp").GetInt64());
        Assert.Equal(Now.ToUnixTimeSeconds(), payload.GetProperty("nbf").GetInt64());
    }

    [Fact]
    public void Grants_allow_only_this_room_microphone_camera_and_no_data_or_admin()
    {
        var result = CreateService("k", Secret).CreateJoinToken(new LiveKitJoinRequest("u", "N", "s_room1", ParticipantRole.Teacher));

        var video = DecodePayload(result.Token).GetProperty("video");
        Assert.True(video.GetProperty("roomJoin").GetBoolean());
        Assert.Equal("s_room1", video.GetProperty("room").GetString());
        Assert.True(video.GetProperty("canPublish").GetBoolean());
        Assert.True(video.GetProperty("canSubscribe").GetBoolean());
        Assert.False(video.GetProperty("canPublishData").GetBoolean());
        Assert.False(video.GetProperty("canUpdateOwnMetadata").GetBoolean());
        Assert.Equal(["microphone", "camera"], video.GetProperty("canPublishSources").EnumerateArray().Select(e => e.GetString()));

        string[] forbidden = ["roomAdmin", "roomCreate", "roomRecord", "roomList", "hidden", "recorder", "ingressAdmin"];
        Assert.All(forbidden, grant => Assert.False(video.TryGetProperty(grant, out _), $"Unexpected grant '{grant}'"));
    }

    [Fact]
    public void Teacher_role_is_set_as_participant_attribute()
    {
        var result = CreateService("k", Secret).CreateJoinToken(new LiveKitJoinRequest("u", "N", "r", ParticipantRole.Teacher));

        Assert.Equal("teacher", DecodePayload(result.Token).GetProperty("attributes").GetProperty("role").GetString());
    }

    [Theory]
    [InlineData("", "Name", "room")]
    [InlineData("id", " ", "room")]
    [InlineData("id", "Name", "")]
    public void Missing_values_are_rejected(string identity, string name, string room)
    {
        var service = CreateService("k", Secret);

        Assert.ThrowsAny<ArgumentException>(() => service.CreateJoinToken(new LiveKitJoinRequest(identity, name, room, ParticipantRole.Student)));
    }
}

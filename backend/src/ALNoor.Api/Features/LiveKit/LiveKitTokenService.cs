using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ALNoor.Api.Features.LiveKit;

public enum ParticipantRole
{
    Teacher,
    Student,
}

/// <param name="Identity">Stable participant id (our user id in production). Never a phone number.</param>
/// <param name="DisplayName">First name shown to others in the room.</param>
/// <param name="Room">Random room name; never contains personal data.</param>
public sealed record LiveKitJoinRequest(string Identity, string DisplayName, string Room, ParticipantRole Role);

public sealed record LiveKitJoinToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Creates LiveKit access tokens (HS256 JWTs signed with the LiveKit API secret).
/// Grants follow PRD section 14.3: join one room, publish microphone/camera only,
/// subscribe, no data messages (no chat), no metadata updates, no admin rights.
/// See docs/adr/0001-livekit-dotnet.md for why this is implemented here instead of a third-party SDK.
/// </summary>
public sealed class LiveKitTokenService(IOptions<LiveKitOptions> options, TimeProvider timeProvider)
{
    /// <summary>Only needed to connect; LiveKit refreshes tokens over a live connection (PRD 14.3).</summary>
    public static readonly TimeSpan JoinTokenLifetime = TimeSpan.FromMinutes(5);

    private static readonly string[] AllowedPublishSources = ["microphone", "camera"];

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public LiveKitJoinToken CreateJoinToken(LiveKitJoinRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Room);

        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + JoinTokenLifetime;

        var videoGrant = new Dictionary<string, object>
        {
            ["roomJoin"] = true,
            ["room"] = request.Room,
            ["canPublish"] = true,
            ["canSubscribe"] = true,
            ["canPublishData"] = false,
            ["canPublishSources"] = AllowedPublishSources,
            ["canUpdateOwnMetadata"] = false,
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.ApiKey,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = request.Identity,
                ["name"] = request.DisplayName,
                ["video"] = videoGrant,
                ["attributes"] = new Dictionary<string, object>
                {
                    ["role"] = request.Role == ParticipantRole.Teacher ? "teacher" : "student",
                },
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.ApiSecret)),
                SecurityAlgorithms.HmacSha256),
        };

        return new LiveKitJoinToken(_handler.CreateToken(descriptor), expiresAt);
    }
}

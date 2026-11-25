using System.ComponentModel.DataAnnotations;
using ALNoor.Api.Features.LiveKit;
using Microsoft.Extensions.Options;

namespace ALNoor.Api.Features.Dev;

/// <summary>Request for a Phase 2 test token. Room names are restricted so they can never carry personal data.</summary>
public sealed record DevTokenRequest(
    [property: Required, StringLength(50)] string Name,
    [property: Required, RegularExpression("^[a-z0-9-]{1,40}$", ErrorMessage = "Room may contain only a-z, 0-9 and '-' (max 40).")] string Room,
    ParticipantRole Role = ParticipantRole.Student);

public sealed record DevTokenResponse(Uri Url, string Token, string Identity, DateTimeOffset ExpiresAt);

/// <summary>
/// Phase 2 proof-of-concept endpoint: issues a LiveKit token WITHOUT authentication.
/// It is mapped only in the Development environment (see Program.cs) and is replaced by the
/// authorised POST /api/sessions/{id}/join in Phase 4. Never enable it elsewhere.
/// </summary>
internal static class DevLiveKitEndpoints
{
    public static IEndpointRouteBuilder MapDevLiveKitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/livekit-token", (
                DevTokenRequest request,
                LiveKitTokenService tokens,
                IOptions<LiveKitOptions> options) =>
            {
                // A fresh identity per request: LiveKit disconnects an earlier participant with the same identity.
                var identity = $"dev-{Guid.CreateVersion7():N}";
                var token = tokens.CreateJoinToken(new LiveKitJoinRequest(identity, request.Name.Trim(), request.Room, request.Role));
                return TypedResults.Ok(new DevTokenResponse(options.Value.Url!, token.Token, identity, token.ExpiresAt));
            })
            .WithName("CreateDevLiveKitToken")
            .WithTags("Dev (Development environment only)");

        return app;
    }
}

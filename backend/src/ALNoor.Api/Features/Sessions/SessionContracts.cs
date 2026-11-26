using System.ComponentModel.DataAnnotations;
using ALNoor.Api.Shared.Data;

namespace ALNoor.Api.Features.Sessions;

public sealed record SessionDto(
    Guid Id,
    Guid ClassId,
    string ClassTitle,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    SessionEndReason? EndReason);

public sealed record JoinResponse(
    Uri LivekitUrl,
    string Token,
    DateTimeOffset ExpiresAt,
    UserRole Role,
    string RoomName,
    string Identity);

public sealed record ParticipantDto(
    Guid UserId,
    string FullName,
    UserRole Role,
    DateTimeOffset? JoinedAt,
    bool IsMicPublished,
    bool IsMicMuted,
    bool IsCameraOn);

public sealed record MuteAllResponse(int MutedCount);

public sealed record RemoveParticipantRequest([property: StringLength(200)] string? Reason = null);

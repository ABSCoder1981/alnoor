namespace ALNoor.Api.Features.Sessions;

/// <summary>Structured, source-generated log messages for sessions (ids only; no personal data).</summary>
internal static partial class SessionLog
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Could not create LiveKit room for class {ClassId}")]
    public static partial void RoomCreateFailed(ILogger logger, Exception exception, Guid classId);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "Session {SessionId} ended but the LiveKit room could not be deleted")]
    public static partial void RoomDeleteFailed(ILogger logger, Exception exception, Guid sessionId);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "Could not remove user {UserId} from live room {Room}")]
    public static partial void RemoveFromRoomFailed(ILogger logger, Exception exception, Guid userId, string room);
}

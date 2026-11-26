using ALNoor.Api.Features.LiveKit;
using ALNoor.Api.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace ALNoor.Api.Features.Sessions;

/// <summary>
/// Removes a user from live rooms when they lose access: deactivation (FR-AUTH-06) or removal from a class (PRD 16.4).
/// Access is already revoked in the database before this runs (join re-checks it), so a LiveKit failure here only
/// delays the disconnect; it is logged rather than failing the teacher's request.
/// </summary>
public sealed class LiveRoomCleanup(AlNoorDbContext db, ILiveKitRoomService rooms, ILogger<LiveRoomCleanup> log)
{
    public async Task RemoveFromLiveRoomsAsync(Guid userId, Guid? onlyClassId, CancellationToken ct)
    {
        var query = db.ClassSessions.Where(s => s.Status == SessionStatus.Live
            && db.ClassStudents.Any(e => e.ClassId == s.ClassId && e.StudentId == userId));
        if (onlyClassId is not null)
        {
            query = query.Where(s => s.ClassId == onlyClassId);
        }

        foreach (var room in await query.Select(s => s.RoomName).ToListAsync(ct))
        {
            try
            {
                await rooms.RemoveParticipantAsync(room, userId.ToString(), ct);
            }
            catch (LiveKitUnavailableException ex)
            {
                SessionLog.RemoveFromRoomFailed(log, ex, userId, room);
            }
        }
    }
}

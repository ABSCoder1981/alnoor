using System.Security.Cryptography;
using ALNoor.Api.Features.Classes;
using ALNoor.Api.Features.LiveKit;
using ALNoor.Api.Shared.Audit;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Security;
using ALNoor.Api.Shared.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ALNoor.Api.Features.Sessions;

/// <summary>PRD 16.5: session lifecycle, join authorization and moderation.</summary>
internal static class SessionEndpoints
{
    public const string JoinRateLimitPolicy = "session-join";

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var anyMember = new[] { UserRole.Teacher, UserRole.Student };

        app.MapPost("/api/classes/{classId:guid}/sessions", StartAsync).RequireAppUser(UserRole.Teacher).WithTags("Sessions");
        app.MapGet("/api/classes/{classId:guid}/sessions/active", GetActiveAsync).RequireAppUser(anyMember).WithTags("Sessions");

        var sessions = app.MapGroup("/api/sessions/{id:guid}").WithTags("Sessions");
        sessions.MapGet("/", GetAsync).RequireAppUser(anyMember);
        sessions.MapPost("/join", JoinAsync).RequireAppUser(anyMember).RequireRateLimiting(JoinRateLimitPolicy);
        sessions.MapPost("/leave", () => TypedResults.NoContent()).RequireAppUser(anyMember); // Informational; attendance uses webhooks (Phase 5).
        sessions.MapPost("/end", EndAsync).RequireAppUser(UserRole.Teacher);
        sessions.MapGet("/participants", ParticipantsAsync).RequireAppUser(UserRole.Teacher);
        sessions.MapPost("/participants/mute-all", MuteAllAsync).RequireAppUser(UserRole.Teacher);
        sessions.MapPost("/participants/{userId:guid}/mute", MuteAsync).RequireAppUser(UserRole.Teacher);
        sessions.MapPost("/participants/{userId:guid}/remove", RemoveAsync).RequireAppUser(UserRole.Teacher);

        return app;
    }

    // ---- Lifecycle -----------------------------------------------------------------------------

    private static async Task<IResult> StartAsync(
        Guid classId, AlNoorDbContext db, CurrentUser current, ILiveKitRoomService rooms, AuditLogger audit,
        TimeProvider time, ILoggerFactory logs, CancellationToken ct)
    {
        var cls = await db.Classes.SingleOrDefaultAsync(c => c.Id == classId && c.TeacherId == current.Id, ct);
        if (cls is null)
        {
            return Problems.NotFound();
        }

        if (!cls.IsActive)
        {
            return Problems.Conflict("CLASS_INACTIVE", "This class is archived. Reactivate it before starting a session.");
        }

        var existing = await LiveSessionAsync(db, classId, ct);
        if (existing is not null)
        {
            return TypedResults.Ok(ToDto(existing, cls.Title)); // FR-SES-02: starting again returns the live session.
        }

        var room = $"s_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
        try
        {
            await rooms.CreateRoomAsync(room, ct);
        }
        catch (LiveKitUnavailableException ex)
        {
            SessionLog.RoomCreateFailed(logs.CreateLogger(nameof(SessionEndpoints)), ex, classId);
            return RtcUnavailable(); // Session is not created (PRD 16.5).
        }

        var session = new ClassSession { ClassId = classId, RoomName = room, StartedAt = time.GetUtcNow(), StartedByUserId = current.Id };
        db.ClassSessions.Add(session);
        audit.Add(AuditActions.SessionStarted, current.Id, nameof(ClassSession), session.Id, new { classId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Another request started it at the same moment: return that one and drop our unused room.
            db.ChangeTracker.Clear();
            await rooms.DeleteRoomAsync(room, CancellationToken.None);
            var winner = await LiveSessionAsync(db, classId, ct);
            return TypedResults.Ok(ToDto(winner!, cls.Title));
        }

        return TypedResults.Created($"/api/sessions/{session.Id}", ToDto(session, cls.Title));
    }

    private static async Task<IResult> GetActiveAsync(Guid classId, AlNoorDbContext db, CurrentUser current, CancellationToken ct)
    {
        var cls = await ClassEndpoints.Visible(db, current.User).Where(c => c.Id == classId).Select(c => new { c.Title }).SingleOrDefaultAsync(ct);
        if (cls is null)
        {
            return Problems.NotFound();
        }

        var live = await LiveSessionAsync(db, classId, ct);
        return live is null ? TypedResults.NoContent() : TypedResults.Ok(ToDto(live, cls.Title));
    }

    private static async Task<IResult> GetAsync(Guid id, AlNoorDbContext db, CurrentUser current, CancellationToken ct)
    {
        var found = await VisibleSessionAsync(db, current.User, id, ct);
        return found is null ? Problems.NotFound() : TypedResults.Ok(ToDto(found.Value.Session, found.Value.ClassTitle));
    }

    private static async Task<IResult> EndAsync(
        Guid id, AlNoorDbContext db, CurrentUser current, ILiveKitRoomService rooms, AuditLogger audit, TimeProvider time,
        ILoggerFactory logs, CancellationToken ct)
    {
        var session = await TeacherSessionAsync(db, current.Id, id, ct);
        if (session is null)
        {
            return Problems.NotFound();
        }

        var title = await db.Classes.Where(c => c.Id == session.ClassId).Select(c => c.Title).SingleAsync(ct);
        if (session.Status == SessionStatus.Ended)
        {
            return TypedResults.Ok(ToDto(session, title)); // Idempotent.
        }

        session.Status = SessionStatus.Ended;
        session.EndedAt = time.GetUtcNow();
        session.EndReason = SessionEndReason.Teacher;
        audit.Add(AuditActions.SessionEnded, current.Id, nameof(ClassSession), session.Id, new { reason = "Teacher" });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var reloaded = await db.ClassSessions.SingleAsync(s => s.Id == id, ct);
            return TypedResults.Ok(ToDto(reloaded, title));
        }

        try
        {
            await rooms.DeleteRoomAsync(session.RoomName, ct); // Disconnects everyone (FR-SES-03).
        }
        catch (LiveKitUnavailableException ex)
        {
            // The session is already ended in our database, so nobody can get a new token. LiveKit closes the
            // room on its own after the empty timeout; log so it can be checked.
            SessionLog.RoomDeleteFailed(logs.CreateLogger(nameof(SessionEndpoints)), ex, id);
        }

        // Phase 5: close open attendance segments and mark absentees here.
        return TypedResults.Ok(ToDto(session, title));
    }

    // ---- Join (PRD 14.2 / 16.5) ------------------------------------------------------------------

    private static async Task<IResult> JoinAsync(
        Guid id, AlNoorDbContext db, CurrentUser current, ILiveKitRoomService rooms, LiveKitTokenService tokens,
        IOptions<LiveKitOptions> liveKit, AuditLogger audit, CancellationToken ct)
    {
        var user = current.User; // Already checked: signed in, active (ACCOUNT_DEACTIVATED otherwise).
        var row = await db.ClassSessions
            .Where(s => s.Id == id)
            .Select(s => new { Session = s, s.Class!.TeacherId })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return Problems.NotFound();
        }

        var isTeacher = row.TeacherId == user.Id;
        if (!isTeacher)
        {
            var enrolled = user.Role == UserRole.Student && await db.ClassStudents.AnyAsync(
                e => e.ClassId == row.Session.ClassId && e.StudentId == user.Id && e.EnrollmentStatus == EnrollmentStatus.Active, ct);
            if (!enrolled)
            {
                return Problems.Forbidden("NOT_ENROLLED", "You are not enrolled in this class.");
            }
        }

        if (row.Session.Status != SessionStatus.Live)
        {
            return Problems.Conflict("SESSION_NOT_LIVE", "This class is not live right now.");
        }

        if (await db.SessionParticipantBlocks.AnyAsync(b => b.SessionId == id && b.UserId == user.Id, ct))
        {
            return Problems.Forbidden("REMOVED_FROM_SESSION", "The teacher removed you from this class. You can join the next one.");
        }

        if (!isTeacher)
        {
            IReadOnlyList<LiveKitParticipant> inRoom;
            try
            {
                inRoom = await rooms.ListParticipantsAsync(row.Session.RoomName, ct);
            }
            catch (LiveKitUnavailableException)
            {
                return RtcUnavailable();
            }

            // The teacher's slot is reserved; a user already in the room replaces their old connection (PRD M7).
            var me = user.Id.ToString();
            var teacher = row.TeacherId.ToString();
            var otherStudents = inRoom.Count(p => p.Identity != teacher && p.Identity != me);
            if (otherStudents >= ClassEndpoints.MaxStudentsPerClass)
            {
                return Problems.Conflict("ROOM_FULL", "The class is full.");
            }
        }

        var role = isTeacher ? ParticipantRole.Teacher : ParticipantRole.Student;
        var token = tokens.CreateJoinToken(new LiveKitJoinRequest(user.Id.ToString(), FirstName(user.FullName), row.Session.RoomName, role));
        audit.Add(AuditActions.SessionTokenIssued, user.Id, nameof(ClassSession), id);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new JoinResponse(
            liveKit.Value.Url!, token.Token, token.ExpiresAt, isTeacher ? UserRole.Teacher : UserRole.Student, row.Session.RoomName, user.Id.ToString()));
    }

    /// <summary>Only the first name is shared with LiveKit and classmates (PRD A2).</summary>
    internal static string FirstName(string fullName)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? trimmed[..space] : trimmed;
    }

    // ---- Moderation (PRD 14.5) -------------------------------------------------------------------

    private static async Task<IResult> ParticipantsAsync(Guid id, AlNoorDbContext db, CurrentUser current, ILiveKitRoomService rooms, CancellationToken ct)
    {
        var session = await TeacherSessionAsync(db, current.Id, id, ct);
        if (session is null)
        {
            return Problems.NotFound();
        }

        if (session.Status != SessionStatus.Live)
        {
            return TypedResults.Ok(Array.Empty<ParticipantDto>());
        }

        IReadOnlyList<LiveKitParticipant> inRoom;
        try
        {
            inRoom = await rooms.ListParticipantsAsync(session.RoomName, ct);
        }
        catch (LiveKitUnavailableException)
        {
            return RtcUnavailable();
        }

        var ids = inRoom.Select(p => Guid.TryParse(p.Identity, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct); // One query, no N+1.

        var result = inRoom
            .Select(p => Guid.TryParse(p.Identity, out var g) && users.TryGetValue(g, out var u) ? (p, u) : (p, null))
            .Where(x => x.u is not null)
            .Select(x => new ParticipantDto(
                x.u!.Id, x.u.FullName, x.u.Role, x.p.JoinedAt,
                IsMicPublished: x.p.Microphone is not null,
                IsMicMuted: x.p.Microphone?.Muted ?? true,
                IsCameraOn: x.p.Camera is { Muted: false }))
            .OrderBy(p => p.Role).ThenBy(p => p.FullName)
            .ToList();
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> MuteAsync(
        Guid id, Guid userId, AlNoorDbContext db, CurrentUser current, ILiveKitRoomService rooms, AuditLogger audit, CancellationToken ct)
    {
        var session = await TeacherSessionAsync(db, current.Id, id, ct);
        if (session is null)
        {
            return Problems.NotFound();
        }

        if (userId == current.Id)
        {
            return Problems.Validation("userId", "The teacher's own microphone is controlled in the classroom, not here.");
        }

        if (session.Status != SessionStatus.Live)
        {
            return Problems.Conflict("SESSION_NOT_LIVE", "This class is not live right now.");
        }

        try
        {
            var participant = (await rooms.ListParticipantsAsync(session.RoomName, ct)).FirstOrDefault(p => p.Identity == userId.ToString());
            if (participant is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, "PARTICIPANT_NOT_IN_ROOM", "This person is not in the class.");
            }

            if (participant.Microphone is null)
            {
                return Problems.Conflict("NO_MICROPHONE_TRACK", "This person has no microphone on.");
            }

            if (!participant.Microphone.Muted)
            {
                await rooms.MuteTrackAsync(session.RoomName, participant.Identity, participant.Microphone.Sid, ct);
            }
        }
        catch (LiveKitUnavailableException)
        {
            return RtcUnavailable();
        }

        audit.Add(AuditActions.ParticipantMuted, current.Id, nameof(ClassSession), id, new { userId });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> MuteAllAsync(
        Guid id, AlNoorDbContext db, CurrentUser current, ILiveKitRoomService rooms, AuditLogger audit, CancellationToken ct)
    {
        var session = await TeacherSessionAsync(db, current.Id, id, ct);
        if (session is null)
        {
            return Problems.NotFound();
        }

        if (session.Status != SessionStatus.Live)
        {
            return Problems.Conflict("SESSION_NOT_LIVE", "This class is not live right now.");
        }

        var muted = 0;
        try
        {
            var teacher = current.Id.ToString();
            foreach (var p in await rooms.ListParticipantsAsync(session.RoomName, ct))
            {
                if (p.Identity != teacher && p.Microphone is { Muted: false } mic)
                {
                    await rooms.MuteTrackAsync(session.RoomName, p.Identity, mic.Sid, ct);
                    muted++;
                }
            }
        }
        catch (LiveKitUnavailableException)
        {
            return RtcUnavailable();
        }

        audit.Add(AuditActions.ParticipantsMutedAll, current.Id, nameof(ClassSession), id, new { mutedCount = muted });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new MuteAllResponse(muted));
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, Guid userId, RemoveParticipantRequest? request, AlNoorDbContext db, CurrentUser current,
        ILiveKitRoomService rooms, AuditLogger audit, TimeProvider time, CancellationToken ct)
    {
        var session = await TeacherSessionAsync(db, current.Id, id, ct);
        if (session is null)
        {
            return Problems.NotFound();
        }

        if (userId == current.Id)
        {
            return Problems.Validation("userId", "The teacher cannot remove themselves. End the class instead.");
        }

        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            return Problems.NotFound();
        }

        // Block first, so the student cannot get a new token even if the LiveKit call fails.
        if (!await db.SessionParticipantBlocks.AnyAsync(b => b.SessionId == id && b.UserId == userId, ct))
        {
            db.SessionParticipantBlocks.Add(new SessionParticipantBlock
            {
                SessionId = id, UserId = userId, BlockedByUserId = current.Id, Reason = request?.Reason?.Trim(), CreatedAt = time.GetUtcNow(),
            });
            audit.Add(AuditActions.ParticipantRemoved, current.Id, nameof(ClassSession), id, new { userId });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear(); // Already blocked by a parallel request.
            }
        }

        if (session.Status == SessionStatus.Live)
        {
            try
            {
                await rooms.RemoveParticipantAsync(session.RoomName, userId.ToString(), ct);
            }
            catch (LiveKitUnavailableException)
            {
                return RtcUnavailable();
            }
        }

        return TypedResults.NoContent();
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static Task<ClassSession?> LiveSessionAsync(AlNoorDbContext db, Guid classId, CancellationToken ct) =>
        db.ClassSessions.SingleOrDefaultAsync(s => s.ClassId == classId && s.Status == SessionStatus.Live, ct);

    private static Task<ClassSession?> TeacherSessionAsync(AlNoorDbContext db, Guid teacherId, Guid sessionId, CancellationToken ct) =>
        db.ClassSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.Class!.TeacherId == teacherId, ct);

    private static async Task<(ClassSession Session, string ClassTitle)?> VisibleSessionAsync(AlNoorDbContext db, User user, Guid id, CancellationToken ct)
    {
        var visible = ClassEndpoints.Visible(db, user).Select(c => c.Id);
        var row = await db.ClassSessions
            .Where(s => s.Id == id && visible.Contains(s.ClassId))
            .Select(s => new { Session = s, s.Class!.Title })
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
        return row is null ? null : (row.Session, row.Title);
    }

    private static SessionDto ToDto(ClassSession s, string title) =>
        new(s.Id, s.ClassId, title, s.Status, s.StartedAt, s.EndedAt, s.EndReason);

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult RtcUnavailable() =>
        Problems.Create(StatusCodes.Status502BadGateway, "RTC_UNAVAILABLE", "The live classroom service is not available. Try again in a moment.");
}

using ALNoor.Api.Features.Sessions;
using ALNoor.Api.Shared.Audit;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Security;
using ALNoor.Api.Shared.Web;
using Microsoft.EntityFrameworkCore;

namespace ALNoor.Api.Features.Classes;

/// <summary>PRD 16.4 class management, schedule and enrollment.</summary>
internal static class ClassEndpoints
{
    /// <summary>Q15: at most 9 enrolled students per class (= 9 live student slots).</summary>
    public const int MaxStudentsPerClass = 9;

    private const int UpcomingCount = 5;

    public static IEndpointRouteBuilder MapClassEndpoints(this IEndpointRouteBuilder app)
    {
        var classes = app.MapGroup("/api/classes").WithTags("Classes");

        classes.MapGet("/", ListAsync).RequireAppUser(UserRole.Teacher, UserRole.Student);
        classes.MapGet("/{id:guid}", GetAsync).RequireAppUser(UserRole.Teacher, UserRole.Student);
        classes.MapPost("/", CreateAsync).RequireAppUser(UserRole.Teacher);
        classes.MapPut("/{id:guid}", UpdateAsync).RequireAppUser(UserRole.Teacher);
        classes.MapPost("/{id:guid}/students", EnrollAsync).RequireAppUser(UserRole.Teacher);
        classes.MapDelete("/{id:guid}/students/{studentId:guid}", UnenrollAsync).RequireAppUser(UserRole.Teacher);

        return app;
    }

    /// <summary>Classes this user may see: the teacher's own, or (students) active classes with an active enrollment.</summary>
    internal static IQueryable<Class> Visible(AlNoorDbContext db, User user) =>
        user.Role == UserRole.Teacher
            ? db.Classes.Where(c => c.TeacherId == user.Id)
            : db.Classes.Where(c => c.IsActive && c.Enrollments.Any(e => e.StudentId == user.Id && e.EnrollmentStatus == EnrollmentStatus.Active));

    private static async Task<List<ClassDto>> ListAsync(
        AlNoorDbContext db, CurrentUser current, TimeProvider time, bool? includeInactive, CancellationToken ct)
    {
        var query = Visible(db, current.User);
        if (current.User.Role == UserRole.Teacher && includeInactive != true)
        {
            query = query.Where(c => c.IsActive);
        }

        var rows = await query
            .OrderBy(c => c.Title)
            .Select(c => new
            {
                Class = c,
                c.Schedule,
                Teacher = new TeacherRef(c.Teacher!.Id, c.Teacher.FullName),
                StudentCount = c.Enrollments.Count(e => e.EnrollmentStatus == EnrollmentStatus.Active),
                LiveSessionId = db.ClassSessions.Where(s => s.ClassId == c.Id && s.Status == SessionStatus.Live).Select(s => (Guid?)s.Id).FirstOrDefault(),
            })
            .AsNoTracking()
            .ToListAsync(ct);

        var now = time.GetUtcNow();
        return rows.Select(r => ToDto(r.Class, r.Schedule, r.Teacher, r.StudentCount, r.LiveSessionId, students: null, now)).ToList();
    }

    private static async Task<IResult> GetAsync(Guid id, AlNoorDbContext db, CurrentUser current, TimeProvider time, CancellationToken ct)
    {
        var dto = await LoadDtoAsync(db, current.User, id, time.GetUtcNow(), ct);
        return dto is null ? Problems.NotFound() : TypedResults.Ok(dto);
    }

    private static async Task<ClassDto?> LoadDtoAsync(AlNoorDbContext db, User user, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var row = await Visible(db, user)
            .Where(c => c.Id == id)
            .Select(c => new
            {
                Class = c,
                c.Schedule,
                Teacher = new TeacherRef(c.Teacher!.Id, c.Teacher.FullName),
                StudentCount = c.Enrollments.Count(e => e.EnrollmentStatus == EnrollmentStatus.Active),
                LiveSessionId = db.ClassSessions.Where(s => s.ClassId == c.Id && s.Status == SessionStatus.Live).Select(s => (Guid?)s.Id).FirstOrDefault(),
            })
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        List<EnrolledStudentDto>? students = null;
        if (user.Role == UserRole.Teacher)
        {
            // Only the class teacher sees who is enrolled (PRD 16.4).
            students = await db.ClassStudents
                .Where(e => e.ClassId == id)
                .OrderBy(e => e.EnrollmentStatus).ThenBy(e => e.Student!.FullName)
                .Select(e => new EnrolledStudentDto(e.StudentId, e.Student!.FullName, e.EnrollmentStatus))
                .ToListAsync(ct);
        }

        return ToDto(row.Class, row.Schedule, row.Teacher, row.StudentCount, row.LiveSessionId, students, now);
    }

    private static async Task<IResult> CreateAsync(
        SaveClassRequest request, AlNoorDbContext db, CurrentUser current, AuditLogger audit, TimeProvider time, CancellationToken ct)
    {
        var (slots, problem) = Validate(request);
        if (problem is not null)
        {
            return problem;
        }

        var now = time.GetUtcNow();
        var cls = new Class
        {
            TenantId = current.User.TenantId,
            TeacherId = current.Id,
            Title = request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            TimeZoneId = request.TimeZoneId,
            DefaultDurationMinutes = request.DefaultDurationMinutes,
            IsActive = request.IsActive ?? true,
            CreatedAt = now,
            UpdatedAt = now,
            Schedule = ToEntities(slots),
        };
        db.Classes.Add(cls);
        audit.Add(AuditActions.ClassCreated, current.Id, nameof(Class), cls.Id);
        await db.SaveChangesAsync(ct);

        var dto = await LoadDtoAsync(db, current.User, cls.Id, now, ct);
        return TypedResults.Created($"/api/classes/{cls.Id}", dto);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, SaveClassRequest request, AlNoorDbContext db, CurrentUser current, AuditLogger audit, TimeProvider time, CancellationToken ct)
    {
        var cls = await db.Classes.Include(c => c.Schedule).SingleOrDefaultAsync(c => c.Id == id && c.TeacherId == current.Id, ct);
        if (cls is null)
        {
            return Problems.NotFound();
        }

        var (slots, problem) = Validate(request);
        if (problem is not null)
        {
            return problem;
        }

        if (request.IsActive == false && cls.IsActive
            && await db.ClassSessions.AnyAsync(s => s.ClassId == id && s.Status == SessionStatus.Live, ct))
        {
            return Problems.Conflict("SESSION_LIVE", "End the live class before archiving it.");
        }

        cls.Title = request.Title.Trim();
        cls.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        cls.TimeZoneId = request.TimeZoneId;
        cls.DefaultDurationMinutes = request.DefaultDurationMinutes;
        cls.IsActive = request.IsActive ?? cls.IsActive;
        cls.UpdatedAt = time.GetUtcNow();

        // Replace the weekly slots. New rows are added explicitly: they already carry client-generated ids,
        // so EF would otherwise treat them as existing rows.
        db.ClassSchedules.RemoveRange(cls.Schedule);
        var replacement = ToEntities(slots);
        replacement.ForEach(s => s.ClassId = cls.Id);
        db.ClassSchedules.AddRange(replacement);
        audit.Add(AuditActions.ClassUpdated, current.Id, nameof(Class), cls.Id);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Conflict("CONCURRENT_UPDATE", "This class was changed by someone else. Reload and try again.");
        }

        return TypedResults.Ok(await LoadDtoAsync(db, current.User, id, time.GetUtcNow(), ct));
    }

    private static async Task<IResult> EnrollAsync(
        Guid id, EnrollStudentsRequest request, AlNoorDbContext db, CurrentUser current, AuditLogger audit, TimeProvider time, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Lock the class row so two concurrent requests cannot both pass the capacity check.
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"Classes\" WHERE \"Id\" = {id} AND \"TeacherId\" = {current.Id} AND \"TenantId\" = {current.User.TenantId} FOR UPDATE")
            .ToListAsync(ct);
        if (locked.Count == 0)
        {
            return Problems.NotFound();
        }

        var ids = request.StudentIds.Distinct().ToList();
        var students = await db.Users.Where(u => ids.Contains(u.Id) && u.Role == UserRole.Student).ToListAsync(ct);
        var missing = ids.Except(students.Select(s => s.Id)).ToList();
        if (missing.Count > 0)
        {
            return Problems.Validation("studentIds", "One or more students were not found.");
        }

        if (students.Any(s => !s.IsActive))
        {
            return Problems.Validation("studentIds", "Deactivated students cannot be enrolled.");
        }

        var enrollments = await db.ClassStudents.Where(e => e.ClassId == id).ToListAsync(ct);
        var activeIds = enrollments.Where(e => e.EnrollmentStatus == EnrollmentStatus.Active).Select(e => e.StudentId).ToHashSet();
        var toAdd = ids.Where(s => !activeIds.Contains(s)).ToList();
        if (activeIds.Count + toAdd.Count > MaxStudentsPerClass)
        {
            return Problems.Conflict("CLASS_FULL", $"A class can have at most {MaxStudentsPerClass} students.");
        }

        var now = time.GetUtcNow();
        foreach (var studentId in toAdd)
        {
            var existing = enrollments.SingleOrDefault(e => e.StudentId == studentId);
            if (existing is null)
            {
                db.ClassStudents.Add(new ClassStudent { ClassId = id, StudentId = studentId, EnrolledAt = now });
            }
            else
            {
                existing.EnrollmentStatus = EnrollmentStatus.Active;
                existing.EnrolledAt = now;
                existing.RemovedAt = null;
            }

            audit.Add(AuditActions.EnrollmentAdded, current.Id, nameof(Class), id, new { studentId });
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var list = await db.ClassStudents
            .Where(e => e.ClassId == id && e.EnrollmentStatus == EnrollmentStatus.Active)
            .OrderBy(e => e.Student!.FullName)
            .Select(e => new EnrolledStudentDto(e.StudentId, e.Student!.FullName, e.EnrollmentStatus))
            .ToListAsync(ct);
        return TypedResults.Ok(list);
    }

    private static async Task<IResult> UnenrollAsync(
        Guid id, Guid studentId, AlNoorDbContext db, CurrentUser current, LiveRoomCleanup liveRooms, AuditLogger audit, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Classes.AnyAsync(c => c.Id == id && c.TeacherId == current.Id, ct))
        {
            return Problems.NotFound();
        }

        var enrollment = await db.ClassStudents.SingleOrDefaultAsync(e => e.ClassId == id && e.StudentId == studentId, ct);
        if (enrollment is null)
        {
            return Problems.NotFound();
        }

        if (enrollment.EnrollmentStatus == EnrollmentStatus.Active)
        {
            enrollment.EnrollmentStatus = EnrollmentStatus.Removed;
            enrollment.RemovedAt = time.GetUtcNow();
            audit.Add(AuditActions.EnrollmentRemoved, current.Id, nameof(Class), id, new { studentId });
            await db.SaveChangesAsync(ct);
            await liveRooms.RemoveFromLiveRoomsAsync(studentId, onlyClassId: id, ct); // PRD 16.4
        }

        return TypedResults.NoContent();
    }

    private static (List<ScheduleSlot> Slots, IResult? Problem) Validate(SaveClassRequest request)
    {
        if (ScheduleCalculator.FindIanaZone(request.TimeZoneId) is null)
        {
            return ([], Problems.Validation("timeZoneId", "Use an IANA time zone, e.g. Asia/Kolkata."));
        }

        var slots = new List<ScheduleSlot>();
        for (var i = 0; i < request.Schedule.Count; i++)
        {
            var entry = request.Schedule[i];
            if (!ScheduleCalculator.TryParseTime(entry.StartTimeLocal, out var start))
            {
                return ([], Problems.Validation($"schedule[{i}].startTimeLocal", "Use 24-hour time HH:mm, e.g. 17:00."));
            }

            slots.Add(new ScheduleSlot(entry.DayOfWeek, start, entry.DurationMinutes));
        }

        if (ScheduleCalculator.FindOverlap(slots) is { } overlap)
        {
            return ([], Problems.Validation("schedule", $"Schedule entries {overlap.First + 1} and {overlap.Second + 1} overlap."));
        }

        return (slots, null);
    }

    private static List<ClassSchedule> ToEntities(List<ScheduleSlot> slots) =>
        slots.Select(s => new ClassSchedule { DayOfWeek = (short)s.DayOfWeek, StartTimeLocal = s.StartTimeLocal, DurationMinutes = s.DurationMinutes }).ToList();

    private static ClassDto ToDto(Class c, List<ClassSchedule> schedule, TeacherRef teacher, int studentCount, Guid? liveSessionId, List<EnrolledStudentDto>? students, DateTimeOffset now)
    {
        var ordered = schedule.OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTimeLocal).ToList();
        var slots = ordered.Select(s => new ScheduleSlot(s.DayOfWeek, s.StartTimeLocal, s.DurationMinutes)).ToList();
        var zone = ScheduleCalculator.FindIanaZone(c.TimeZoneId);
        var next = zone is null ? [] : ScheduleCalculator.NextOccurrences(slots, zone, now, UpcomingCount);

        return new ClassDto(
            c.Id, c.Title, c.Description, c.TimeZoneId, c.DefaultDurationMinutes, c.IsActive,
            ordered.Select(s => new ScheduleEntryDto(s.DayOfWeek, ScheduleCalculator.FormatTime(s.StartTimeLocal), s.DurationMinutes)).ToList(),
            next,
            studentCount,
            liveSessionId,
            teacher,
            students);
    }
}

using System.ComponentModel.DataAnnotations;
using ALNoor.Api.Features.Auth;
using ALNoor.Api.Shared.Audit;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Security;
using ALNoor.Api.Shared.Validation;
using ALNoor.Api.Shared.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ALNoor.Api.Features.Students;

public sealed record StudentListItem(Guid Id, string FullName, string PhoneNumber, bool IsActive, bool MustChangePassword, int ClassCount);

public sealed record UpdateStudentRequest(
    [property: Required, StringLength(100, MinimumLength = 2)] string FullName,
    [property: Required, StringLength(30)] string PhoneNumber);

public sealed record UpdateStudentStatusRequest([property: Required] bool? IsActive);

public sealed record ResetPasswordRequest([property: Required, StringLength(PasswordPolicy.MaxLength, MinimumLength = 1)] string TemporaryPassword);

/// <summary>PRD 16.3. A teacher manages students they created or who are enrolled in one of their classes (FR-CLS-07).</summary>
internal static class StudentEndpoints
{
    public static IEndpointRouteBuilder MapStudentEndpoints(this IEndpointRouteBuilder app)
    {
        var students = app.MapGroup("/api/students").WithTags("Students").RequireAppUser(UserRole.Teacher);

        students.MapGet("/", ListAsync);
        students.MapGet("/{id:guid}", GetAsync);
        students.MapPut("/{id:guid}", UpdateAsync);
        students.MapPut("/{id:guid}/status", UpdateStatusAsync);
        students.MapPost("/{id:guid}/reset-password", ResetPasswordAsync);

        return app;
    }

    /// <summary>Students visible to this teacher.</summary>
    private static IQueryable<User> VisibleStudents(AlNoorDbContext db, Guid teacherId) =>
        db.Users.Where(u => u.Role == UserRole.Student &&
            (u.CreatedByUserId == teacherId || db.ClassStudents.Any(cs => cs.StudentId == u.Id && cs.Class!.TeacherId == teacherId)));

    private static async Task<List<StudentListItem>> ListAsync(
        AlNoorDbContext db, CurrentUser current, string? search, bool? isActive, CancellationToken ct)
    {
        var query = VisibleStudents(db, current.Id);
        if (isActive is not null)
        {
            query = query.Where(u => u.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100)
            {
                term = term[..100];
            }

            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(u => EF.Functions.ILike(u.FullName, pattern, "\\") || EF.Functions.Like(u.PhoneNumber, pattern, "\\"));
        }

        // Single query: the class count is a correlated sub-query, not N+1.
        return await query
            .OrderBy(u => u.FullName)
            .Select(u => new StudentListItem(
                u.Id, u.FullName, u.PhoneNumber, u.IsActive, u.MustChangePassword,
                db.ClassStudents.Count(cs => cs.StudentId == u.Id && cs.EnrollmentStatus == EnrollmentStatus.Active
                    && cs.Class!.TeacherId == current.Id && cs.Class.IsActive)))
            .ToListAsync(ct);
    }

    private static async Task<IResult> GetAsync(Guid id, AlNoorDbContext db, CurrentUser current, CancellationToken ct)
    {
        var student = await VisibleStudents(db, current.Id).AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        return student is null ? Problems.NotFound() : TypedResults.Ok(AuthEndpoints.ToStudentDto(student));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateStudentRequest request, AlNoorDbContext db, CurrentUser current, AuditLogger audit, TimeProvider time, CancellationToken ct)
    {
        var student = await VisibleStudents(db, current.Id).SingleOrDefaultAsync(u => u.Id == id, ct);
        if (student is null)
        {
            return Problems.NotFound();
        }

        var phone = PhoneNumberNormalizer.ToE164(request.PhoneNumber);
        if (phone is null)
        {
            return Problems.Validation("phoneNumber", "Enter a valid phone number with its country code, e.g. +919876543210.");
        }

        if (phone != student.PhoneNumber && await db.Users.AnyAsync(u => u.PhoneNumber == phone && u.Id != id, ct))
        {
            return Problems.Conflict("PHONE_ALREADY_REGISTERED", "An account with this phone number already exists.");
        }

        student.FullName = request.FullName.Trim();
        student.PhoneNumber = phone;
        student.UpdatedAt = time.GetUtcNow();
        audit.Add(AuditActions.StudentUpdated, current.Id, nameof(User), student.Id);
        return await SaveAsync(db, student, ct);
    }

    private static async Task<IResult> UpdateStatusAsync(
        Guid id, UpdateStudentStatusRequest request, AlNoorDbContext db, CurrentUser current, TokenIssuer tokens,
        AuditLogger audit, TimeProvider time, CancellationToken ct)
    {
        var student = await VisibleStudents(db, current.Id).SingleOrDefaultAsync(u => u.Id == id, ct);
        if (student is null)
        {
            return Problems.NotFound();
        }

        var activate = request.IsActive!.Value;
        if (student.IsActive != activate)
        {
            student.IsActive = activate;
            student.UpdatedAt = time.GetUtcNow();
            audit.Add(activate ? AuditActions.StudentActivated : AuditActions.StudentDeactivated, current.Id, nameof(User), student.Id);
            if (!activate)
            {
                // FR-AUTH-06: no new tokens. Access tokens stop working immediately because every request re-checks IsActive.
                // Removal from a live room is added with sessions in Phase 4.
                await tokens.RevokeAllAsync(student.Id, exceptTokenId: null, ct);
            }
        }

        return await SaveAsync(db, student, ct);
    }

    private static async Task<IResult> ResetPasswordAsync(
        Guid id, ResetPasswordRequest request, AlNoorDbContext db, CurrentUser current, IPasswordHasher<User> hasher,
        TokenIssuer tokens, AuditLogger audit, TimeProvider time, CancellationToken ct)
    {
        var student = await VisibleStudents(db, current.Id).SingleOrDefaultAsync(u => u.Id == id, ct);
        if (student is null)
        {
            return Problems.NotFound();
        }

        var error = PasswordPolicy.Validate(request.TemporaryPassword);
        if (error is not null)
        {
            return Problems.Validation("temporaryPassword", error);
        }

        student.PasswordHash = hasher.HashPassword(student, request.TemporaryPassword);
        student.MustChangePassword = true;
        student.FailedLoginCount = 0;
        student.LockoutEndAt = null;
        student.UpdatedAt = time.GetUtcNow();
        await tokens.RevokeAllAsync(student.Id, exceptTokenId: null, ct);
        audit.Add(AuditActions.StudentPasswordReset, current.Id, nameof(User), student.Id);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SaveAsync(AlNoorDbContext db, User student, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Conflict("CONCURRENT_UPDATE", "This student was changed by someone else. Reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Problems.Conflict("PHONE_ALREADY_REGISTERED", "An account with this phone number already exists.");
        }

        return TypedResults.Ok(AuthEndpoints.ToStudentDto(student));
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}

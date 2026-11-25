using System.Text.Json;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Tenancy;

namespace ALNoor.Api.Shared.Audit;

/// <summary>
/// Adds audit entries (PRD 21.1 "Audit") to the current unit of work; they are saved with the change they describe.
/// Never pass passwords, tokens or other secrets in <c>details</c>.
/// </summary>
public sealed class AuditLogger(AlNoorDbContext db, TenantContext tenant, IHttpContextAccessor http, TimeProvider time)
{
    public void Add(string action, Guid? actorUserId, string? entityType = null, Guid? entityId = null, object? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenant.TenantId,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is null ? null : JsonSerializer.Serialize(details),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = time.GetUtcNow(),
        });
    }
}

/// <summary>Stable audit action names.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string AccountLocked = "auth.locked";
    public const string RefreshTokenReused = "auth.refresh_token_reused";
    public const string Logout = "auth.logout";
    public const string PasswordChanged = "auth.password_changed";
    public const string StudentCreated = "student.created";
    public const string StudentUpdated = "student.updated";
    public const string StudentActivated = "student.activated";
    public const string StudentDeactivated = "student.deactivated";
    public const string StudentPasswordReset = "student.password_reset";
    public const string TeacherCreated = "teacher.created";
    public const string TeacherPasswordReset = "teacher.password_reset";
    public const string ClassCreated = "class.created";
    public const string ClassUpdated = "class.updated";
    public const string EnrollmentAdded = "enrollment.added";
    public const string EnrollmentRemoved = "enrollment.removed";
}

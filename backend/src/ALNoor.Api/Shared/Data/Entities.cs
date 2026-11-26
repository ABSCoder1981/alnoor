namespace ALNoor.Api.Shared.Data;

// Entities for PRD section 15 (Phase 3 subset). Conventions (15.1): UUIDv7 keys generated in the app,
// UTC timestamptz ending in "At", enums stored as varchar with check constraints, soft delete via IsActive,
// optimistic concurrency (PostgreSQL xmin) on editable rows.

public enum UserRole
{
    Teacher,
    Student,
    Admin,
}

public enum EnrollmentStatus
{
    Active,
    Removed,
}

/// <summary>A school/organisation. V1 has exactly one row (PRD 15.4).</summary>
public sealed class Tenant
{
    /// <summary>The single V1 tenant, created by the initial migration.</summary>
    public static readonly Guid DefaultId = new("0193a3f0-0000-7000-8000-000000000001");

    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string FullName { get; set; }

    /// <summary>E.164, unique per tenant (PRD M1).</summary>
    public required string PhoneNumber { get; set; }

    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEndAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the raw token (base64url). The raw token is never stored (PRD 15.5).</summary>
    public required string TokenHash { get; set; }

    /// <summary>All tokens produced by rotating one login share a family; reuse revokes the family.</summary>
    public Guid FamilyId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? DeviceInfo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public uint Version { get; set; }
}

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public required string Action { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    /// <summary>JSON details. Never passwords, tokens or full phone numbers of unknown callers.</summary>
    public string? Details { get; set; }

    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Class
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid TeacherId { get; set; }
    public User? Teacher { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }

    /// <summary>IANA time zone id, e.g. Asia/Kolkata (PRD M5).</summary>
    public required string TimeZoneId { get; set; }

    public int DefaultDurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }

    public List<ClassSchedule> Schedule { get; set; } = [];
    public List<ClassStudent> Enrollments { get; set; } = [];
}

/// <summary>One weekly slot, in the class's time zone. Used only to show upcoming times (PRD C2).</summary>
public sealed class ClassSchedule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClassId { get; set; }

    /// <summary>0 = Sunday … 6 = Saturday (System.DayOfWeek numbering).</summary>
    public short DayOfWeek { get; set; }

    public TimeOnly StartTimeLocal { get; set; }
    public int DurationMinutes { get; set; }
}

public sealed class ClassStudent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClassId { get; set; }
    public Class? Class { get; set; }
    public Guid StudentId { get; set; }
    public User? Student { get; set; }
    public EnrollmentStatus EnrollmentStatus { get; set; } = EnrollmentStatus.Active;
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

public enum SessionStatus
{
    Live,
    Ended,
}

public enum SessionEndReason
{
    Teacher,
    Timeout,
    System,
}

/// <summary>One live occurrence of a class (PRD C2: created when the teacher taps Start).</summary>
public sealed class ClassSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClassId { get; set; }
    public Class? Class { get; set; }

    /// <summary>Random LiveKit room name, e.g. s_9f2c…; never contains personal data (PRD 21.1).</summary>
    public required string RoomName { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Live;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid StartedByUserId { get; set; }
    public SessionEndReason? EndReason { get; set; }
    public uint Version { get; set; }
}

/// <summary>A participant removed by the teacher cannot rejoin that session (PRD M9 / Q7).</summary>
public sealed class SessionParticipantBlock
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid BlockedByUserId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

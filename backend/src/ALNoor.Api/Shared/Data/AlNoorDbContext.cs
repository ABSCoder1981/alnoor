using ALNoor.Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ALNoor.Api.Shared.Data;

/// <summary>
/// Application database context. Users and Classes are filtered to the current tenant (PRD 15.4);
/// child tables get their tenant through the parent class.
/// </summary>
public sealed class AlNoorDbContext(DbContextOptions<AlNoorDbContext> options, TenantContext tenant) : DbContext(options)
{
    private Guid CurrentTenantId => tenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Class> Classes => Set<Class>();
    public DbSet<ClassSchedule> ClassSchedules => Set<ClassSchedule>();
    public DbSet<ClassStudent> ClassStudents => Set<ClassStudent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasData(new Tenant
            {
                Id = Tenant.DefaultId,
                Name = "ALNoor",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero),
            });
        });

        modelBuilder.Entity<User>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(100);
            e.Property(x => x.PhoneNumber).HasMaxLength(20);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.TenantId, x.PhoneNumber }).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Users_Role", "\"Role\" IN ('Teacher','Student','Admin')"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.DeviceInfo).HasMaxLength(100);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.FamilyId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            // Tokens belong to a user; filter through the user so tenant isolation also applies here.
            e.HasQueryFilter(x => Users.Any(u => u.Id == x.UserId));
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.EntityType).HasMaxLength(50);
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.Property(x => x.IpAddress).HasMaxLength(45);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt }).IsDescending(false, true);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Class>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.TeacherId, x.IsActive });
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
            // All FKs RESTRICT (PRD 15.3, no hard deletes); schedule rows are replaced explicitly on edit.
            e.HasMany(x => x.Schedule).WithOne().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Enrollments).WithOne(x => x.Class).HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Classes_DefaultDuration", "\"DefaultDurationMinutes\" BETWEEN 10 AND 180"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ClassSchedule>(e =>
        {
            e.HasIndex(x => x.ClassId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ClassSchedules_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6");
                t.HasCheckConstraint("CK_ClassSchedules_Duration", "\"DurationMinutes\" BETWEEN 10 AND 180");
            });
            e.HasQueryFilter(x => Classes.Any(c => c.Id == x.ClassId));
        });

        modelBuilder.Entity<ClassStudent>(e =>
        {
            e.Property(x => x.EnrollmentStatus).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(x => new { x.ClassId, x.StudentId }).IsUnique();
            e.HasIndex(x => new { x.StudentId, x.EnrollmentStatus });
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ClassStudents_Status", "\"EnrollmentStatus\" IN ('Active','Removed')"));
            e.HasQueryFilter(x => Classes.Any(c => c.Id == x.ClassId));
        });
    }
}

using System.Text;
using ALNoor.Api.Features.Auth;
using ALNoor.Api.Shared.Audit;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ALNoor.Api.Features.Admin;

/// <summary>
/// Developer commands (PRD FR-AUTH-11, Q9; no admin UI in V1):
///   dotnet run --project src/ALNoor.Api -- seed-teacher --name "Teacher Name" --phone +919800000000
///   dotnet run --project src/ALNoor.Api -- reset-teacher-password --phone +919800000000
/// The password is read from ALNOOR_ADMIN_PASSWORD or typed at a hidden prompt (never a command-line argument,
/// which would end up in shell history and process listings).
/// </summary>
internal static class AdminCommands
{
    public const string PasswordVariable = "ALNOOR_ADMIN_PASSWORD";
    private static readonly string[] Commands = ["seed-teacher", "reset-teacher-password"];

    public static bool IsCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0]);

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, Func<string?> readPassword)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AlNoorDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var options = ParseOptions(args.Skip(1).ToArray());

        var phone = PhoneNumberNormalizer.ToE164(options.GetValueOrDefault("phone"));
        if (phone is null)
        {
            await output.WriteLineAsync("Error: --phone must be a valid number with country code, e.g. +919800000000.");
            return 2;
        }

        var password = readPassword();
        var error = PasswordPolicy.Validate(password);
        if (error is not null)
        {
            await output.WriteLineAsync($"Error: {error}");
            return 2;
        }

        var now = time.GetUtcNow();
        var existing = await db.Users.SingleOrDefaultAsync(u => u.PhoneNumber == phone);

        if (args[0] == "seed-teacher")
        {
            var name = options.GetValueOrDefault("name")?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length is < 2 or > 100)
            {
                await output.WriteLineAsync("Error: --name is required (2-100 characters).");
                return 2;
            }

            if (existing is not null)
            {
                await output.WriteLineAsync("Error: an account with this phone number already exists. Use reset-teacher-password instead.");
                return 3;
            }

            var teacher = new User
            {
                TenantId = Tenant.DefaultId,
                FullName = name,
                PhoneNumber = phone,
                PasswordHash = string.Empty,
                Role = UserRole.Teacher,
                CreatedAt = now,
                UpdatedAt = now,
            };
            teacher.PasswordHash = hasher.HashPassword(teacher, password!);
            db.Users.Add(teacher);
            AddAudit(db, AuditActions.TeacherCreated, teacher.Id, now);
            await db.SaveChangesAsync();
            await output.WriteLineAsync($"Teacher created: {teacher.Id}");
            return 0;
        }

        if (existing is null || existing.Role != UserRole.Teacher)
        {
            await output.WriteLineAsync("Error: no teacher account with this phone number.");
            return 3;
        }

        existing.PasswordHash = hasher.HashPassword(existing, password!);
        existing.FailedLoginCount = 0;
        existing.LockoutEndAt = null;
        existing.UpdatedAt = now;
        await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == existing.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
        AddAudit(db, AuditActions.TeacherPasswordReset, existing.Id, now);
        await db.SaveChangesAsync();
        await output.WriteLineAsync("Teacher password reset. All sessions were signed out.");
        return 0;
    }

    /// <summary>Reads the password from the environment, else prompts twice without echo.</summary>
    public static string? ReadPasswordFromEnvironmentOrConsole()
    {
        var fromEnv = Environment.GetEnvironmentVariable(PasswordVariable);
        if (!string.IsNullOrEmpty(fromEnv))
        {
            return fromEnv;
        }

        if (Console.IsInputRedirected)
        {
            return null;
        }

        var first = Prompt("New password: ");
        var second = Prompt("Repeat password: ");
        return first == second ? first : null;
    }

    private static string Prompt(string label)
    {
        Console.Write(label);
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                result[args[i][2..]] = args[i + 1];
            }
        }

        return result;
    }

    private static void AddAudit(AlNoorDbContext db, string action, Guid userId, DateTimeOffset now) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = Tenant.DefaultId,
            ActorUserId = null, // Developer command, not a signed-in user.
            Action = action,
            EntityType = nameof(User),
            EntityId = userId,
            Details = "{\"source\":\"cli\"}",
            CreatedAt = now,
        });
}

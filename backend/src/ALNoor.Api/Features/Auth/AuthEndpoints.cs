using ALNoor.Api.Shared.Audit;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Security;
using ALNoor.Api.Shared.Tenancy;
using ALNoor.Api.Shared.Validation;
using ALNoor.Api.Shared.Web;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ALNoor.Api.Features.Auth;

/// <summary>PRD 16.2 authentication endpoints.</summary>
internal static class AuthEndpoints
{
    private const string InvalidCredentialsTitle = "The phone number or password is incorrect.";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAppUser().AllowPendingPasswordChange();
        auth.MapGet("/me", Me).RequireAppUser().AllowPendingPasswordChange();
        auth.MapPut("/password", ChangePasswordAsync).RequireAppUser().AllowPendingPasswordChange();
        auth.MapPost("/register", RegisterStudentAsync).RequireAppUser(UserRole.Teacher);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        AlNoorDbContext db,
        IPasswordHasher<User> hasher,
        TokenIssuer tokens,
        AuditLogger audit,
        LoginRateLimiter rateLimiter,
        IOptions<LoginPolicyOptions> policy,
        TimeProvider time)
    {
        var phone = PhoneNumberNormalizer.ToE164(request.PhoneNumber);
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimiter.TryAcquire(ip, phone ?? request.PhoneNumber.Trim()))
        {
            http.Response.Headers.RetryAfter = "60";
            return Problems.Create(StatusCodes.Status429TooManyRequests, "RATE_LIMITED", "Too many login attempts. Wait a minute and try again.");
        }

        if (phone is null)
        {
            return Problems.Validation("phoneNumber", "Enter the phone number with its country code, e.g. +919876543210.");
        }

        var now = time.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(u => u.PhoneNumber == phone, http.RequestAborted);
        if (user is null)
        {
            // Same work and same answer as a wrong password, so callers cannot discover which numbers exist.
            hasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
            audit.Add(AuditActions.LoginFailed, null, details: new { phone = PhoneNumberNormalizer.Mask(phone), reason = "unknown" });
            await db.SaveChangesAsync(http.RequestAborted);
            return Problems.Create(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", InvalidCredentialsTitle);
        }

        if (user.LockoutEndAt > now)
        {
            return Locked(user.LockoutEndAt.Value);
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            var locked = user.FailedLoginCount >= policy.Value.MaxFailedAttempts;
            if (locked)
            {
                user.LockoutEndAt = now.AddMinutes(policy.Value.LockoutMinutes);
                user.FailedLoginCount = 0;
                audit.Add(AuditActions.AccountLocked, user.Id, nameof(User), user.Id);
            }

            audit.Add(AuditActions.LoginFailed, user.Id, nameof(User), user.Id, new { reason = "password" });
            await db.SaveChangesAsync(http.RequestAborted);
            return locked
                ? Locked(user.LockoutEndAt!.Value)
                : Problems.Create(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", InvalidCredentialsTitle);
        }

        if (!user.IsActive)
        {
            return Problems.Forbidden("ACCOUNT_DEACTIVATED", "This account has been deactivated. Contact your teacher.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        user.LastLoginAt = now;
        var response = tokens.Issue(user, http.Response, request.ClientType, request.DeviceInfo, familyId: null, out _);
        audit.Add(AuditActions.LoginSucceeded, user.Id, nameof(User), user.Id, new { client = request.ClientType.ToString() });
        await db.SaveChangesAsync(http.RequestAborted);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        HttpContext http,
        AlNoorDbContext db,
        TokenIssuer tokens,
        AuditLogger audit,
        TenantContext tenant,
        TimeProvider time)
    {
        var raw = TokenIssuer.ReadRawToken(http.Request, request.RefreshToken);
        if (raw is null)
        {
            return InvalidRefresh();
        }

        var hash = RefreshTokenValue.Hash(raw);
        var stored = await db.RefreshTokens.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.TokenHash == hash, http.RequestAborted);
        if (stored is null)
        {
            return InvalidRefresh();
        }

        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == stored.UserId, http.RequestAborted);
        tenant.Set(user.TenantId);
        var now = time.GetUtcNow();

        if (stored.RevokedAt is not null)
        {
            if (stored.ReplacedByTokenId is not null)
            {
                // A rotated (already used) token came back: assume theft and revoke the whole family (FR-AUTH-04).
                await db.RefreshTokens.IgnoreQueryFilters()
                    .Where(t => t.FamilyId == stored.FamilyId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), http.RequestAborted);
                audit.Add(AuditActions.RefreshTokenReused, user.Id, nameof(User), user.Id);
                await db.SaveChangesAsync(http.RequestAborted);
                TokenIssuer.ClearCookie(http.Response);
                return Problems.Create(StatusCodes.Status401Unauthorized, "REFRESH_TOKEN_REUSED", "Sign in again.");
            }

            return InvalidRefresh(http);
        }

        if (stored.ExpiresAt <= now)
        {
            return InvalidRefresh(http);
        }

        if (!user.IsActive)
        {
            await tokens.RevokeAllAsync(user.Id, exceptTokenId: null, http.RequestAborted);
            TokenIssuer.ClearCookie(http.Response);
            return Problems.Forbidden("ACCOUNT_DEACTIVATED", "This account has been deactivated. Contact your teacher.");
        }

        var response = tokens.Issue(user, http.Response, request.ClientType, stored.DeviceInfo, stored.FamilyId, out var replacement);
        stored.RevokedAt = now;
        stored.ReplacedByTokenId = replacement.Id;
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two refreshes raced with the same token; the other one won. Not theft, so no family revocation.
            return InvalidRefresh();
        }

        return TypedResults.Ok(response);
    }

    private static async Task<IResult> LogoutAsync(
        LogoutRequest request,
        HttpContext http,
        AlNoorDbContext db,
        CurrentUser current,
        AuditLogger audit,
        TimeProvider time)
    {
        var raw = TokenIssuer.ReadRawToken(http.Request, request.RefreshToken);
        if (raw is not null)
        {
            var hash = RefreshTokenValue.Hash(raw);
            var now = time.GetUtcNow();
            await db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.UserId == current.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), http.RequestAborted);
        }

        TokenIssuer.ClearCookie(http.Response);
        audit.Add(AuditActions.Logout, current.Id, nameof(User), current.Id);
        await db.SaveChangesAsync(http.RequestAborted);
        return TypedResults.NoContent();
    }

    private static MeResponse Me(CurrentUser current)
    {
        var u = current.User;
        return new MeResponse(u.Id, u.FullName, u.PhoneNumber, u.Role, u.IsActive, u.MustChangePassword);
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext http,
        AlNoorDbContext db,
        CurrentUser current,
        IPasswordHasher<User> hasher,
        TokenIssuer tokens,
        AuditLogger audit,
        TimeProvider time)
    {
        var user = await db.Users.SingleAsync(u => u.Id == current.Id, http.RequestAborted);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return Problems.Create(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "The current password is incorrect.");
        }

        var error = PasswordPolicy.Validate(request.NewPassword, request.CurrentPassword);
        if (error is not null)
        {
            return Problems.Validation("newPassword", error);
        }

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = time.GetUtcNow();

        // Keep the caller's own session (if it sent its refresh token); sign out everywhere else.
        Guid? keep = null;
        var raw = TokenIssuer.ReadRawToken(http.Request, request.RefreshToken);
        if (raw is not null)
        {
            var hash = RefreshTokenValue.Hash(raw);
            keep = await db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.UserId == user.Id && t.RevokedAt == null)
                .Select(t => (Guid?)t.Id)
                .SingleOrDefaultAsync(http.RequestAborted);
        }

        await tokens.RevokeAllAsync(user.Id, keep, http.RequestAborted);
        audit.Add(AuditActions.PasswordChanged, user.Id, nameof(User), user.Id);
        await db.SaveChangesAsync(http.RequestAborted);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RegisterStudentAsync(
        RegisterStudentRequest request,
        HttpContext http,
        AlNoorDbContext db,
        CurrentUser current,
        IPasswordHasher<User> hasher,
        AuditLogger audit,
        TimeProvider time)
    {
        var phone = PhoneNumberNormalizer.ToE164(request.PhoneNumber);
        if (phone is null)
        {
            return Problems.Validation("phoneNumber", "Enter a valid phone number with its country code, e.g. +919876543210.");
        }

        var passwordError = PasswordPolicy.Validate(request.TemporaryPassword);
        if (passwordError is not null)
        {
            return Problems.Validation("temporaryPassword", passwordError);
        }

        if (await db.Users.AnyAsync(u => u.PhoneNumber == phone, http.RequestAborted))
        {
            return Problems.Conflict("PHONE_ALREADY_REGISTERED", "An account with this phone number already exists.");
        }

        var now = time.GetUtcNow();
        var student = new User
        {
            TenantId = current.User.TenantId,
            FullName = request.FullName.Trim(),
            PhoneNumber = phone,
            PasswordHash = string.Empty,
            Role = UserRole.Student, // Never chosen by the caller (PRD C1).
            MustChangePassword = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = current.Id,
        };
        student.PasswordHash = hasher.HashPassword(student, request.TemporaryPassword);
        db.Users.Add(student);
        audit.Add(AuditActions.StudentCreated, current.Id, nameof(User), student.Id);

        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Problems.Conflict("PHONE_ALREADY_REGISTERED", "An account with this phone number already exists.");
        }

        return TypedResults.Created($"/api/students/{student.Id}", ToStudentDto(student));
    }

    internal static StudentDto ToStudentDto(User u) => new(u.Id, u.FullName, u.PhoneNumber, u.Role, u.IsActive, u.MustChangePassword);

    private static ProblemHttpResult Locked(DateTimeOffset until) =>
        Problems.Create(StatusCodes.Status423Locked, "ACCOUNT_LOCKED", "Too many failed attempts. Try again later.",
            new Dictionary<string, object?> { ["lockoutEndsAt"] = until });

    private static ProblemHttpResult InvalidRefresh(HttpContext? http = null)
    {
        if (http is not null)
        {
            TokenIssuer.ClearCookie(http.Response);
        }

        return Problems.Create(StatusCodes.Status401Unauthorized, "INVALID_REFRESH_TOKEN", "Sign in again.");
    }

    // Used only to spend the same hashing time for unknown phone numbers.
    private static readonly User DummyUser = new() { FullName = "-", PhoneNumber = "-", PasswordHash = "-" };
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(DummyUser, "not-a-real-password-0000");
}

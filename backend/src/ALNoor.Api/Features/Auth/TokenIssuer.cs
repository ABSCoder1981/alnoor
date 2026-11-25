using ALNoor.Api.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ALNoor.Api.Features.Auth;

/// <summary>Creates access/refresh token pairs and handles the web refresh cookie.</summary>
public sealed class TokenIssuer(AlNoorDbContext db, AccessTokenService accessTokens, IOptions<JwtOptions> jwt, TimeProvider time)
{
    public const string RefreshCookieName = "alnoor_rt";
    private const string RefreshCookiePath = "/api/auth";

    /// <summary>Adds a new refresh token (not yet saved) and returns the response. Pass familyId to rotate within a family.</summary>
    public AuthResponse Issue(User user, HttpResponse response, ClientType client, string? deviceInfo, Guid? familyId, out RefreshToken stored)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(response);
        var now = time.GetUtcNow();
        var raw = RefreshTokenValue.NewRaw();
        stored = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = RefreshTokenValue.Hash(raw),
            FamilyId = familyId ?? Guid.CreateVersion7(),
            ExpiresAt = now.AddDays(jwt.Value.RefreshTokenDays),
            DeviceInfo = deviceInfo,
            CreatedAt = now,
        };
        db.RefreshTokens.Add(stored);

        var access = accessTokens.Create(user);
        if (client == ClientType.Web)
        {
            response.Cookies.Append(RefreshCookieName, raw, new CookieOptions
            {
                HttpOnly = true,
                Secure = true, // Browsers accept Secure cookies on http://localhost during development.
                SameSite = SameSiteMode.Strict,
                Path = RefreshCookiePath,
                Expires = stored.ExpiresAt,
                IsEssential = true,
            });
        }

        return new AuthResponse(
            access.Token,
            access.ExpiresAt,
            client == ClientType.Web ? null : raw,
            stored.ExpiresAt,
            new AuthUserDto(user.Id, user.FullName, user.Role, user.MustChangePassword));
    }

    public static string? ReadRawToken(HttpRequest request, string? fromBody) =>
        !string.IsNullOrWhiteSpace(fromBody) ? fromBody : request.Cookies[RefreshCookieName];

    public static void ClearCookie(HttpResponse response) =>
        response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath, Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict });

    /// <summary>Revokes every active refresh token of a user, optionally keeping one (e.g. the caller's own after a password change).</summary>
    public async Task RevokeAllAsync(Guid userId, Guid? exceptTokenId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.Id != exceptTokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }
}

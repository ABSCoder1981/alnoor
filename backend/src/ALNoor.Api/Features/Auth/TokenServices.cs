using System.Security.Cryptography;
using System.Text;
using ALNoor.Api.Shared.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ALNoor.Api.Features.Auth;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues 15-minute access JWTs with claims sub, tid, role, jti (PRD 17.1). The role claim is a UI hint only.</summary>
public sealed class AccessTokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public AccessToken Create(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var settings = options.Value;
        var now = time.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                ["tid"] = user.TenantId.ToString(),
                ["role"] = user.Role.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(settings.SigningKeyBytes()), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expiresAt);
    }
}

/// <summary>Refresh tokens: 32 random bytes (base64url) given to the client once; only the SHA-256 hash is stored (PRD 15.5).</summary>
internal static class RefreshTokenValue
{
    public static string NewRaw() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}

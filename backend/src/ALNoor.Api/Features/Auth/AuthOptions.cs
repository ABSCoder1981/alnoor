using System.ComponentModel.DataAnnotations;

namespace ALNoor.Api.Features.Auth;

/// <summary>Access-token signing settings (PRD 17.1). Jwt__SigningKey comes from user-secrets / environment only.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Base64 of at least 32 random bytes (HS256 needs a 256-bit key).</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "alnoor-api";

    [Required]
    public string Audience { get; set; } = "alnoor-clients";

    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 30;

    public byte[] SigningKeyBytes()
    {
        try
        {
            return Convert.FromBase64String(SigningKey);
        }
        catch (FormatException)
        {
            return [];
        }
    }
}

/// <summary>Password and lockout policy (PRD 17.3). Rate limits are configurable so tests can raise them.</summary>
public sealed class LoginPolicyOptions
{
    public const string SectionName = "Auth:LoginPolicy";

    [Range(1, 100)]
    public int MaxFailedAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;

    [Range(1, 10_000)]
    public int LoginRequestsPerMinutePerIp { get; set; } = 10;

    [Range(1, 10_000)]
    public int LoginRequestsPerMinutePerPhone { get; set; } = 5;
}

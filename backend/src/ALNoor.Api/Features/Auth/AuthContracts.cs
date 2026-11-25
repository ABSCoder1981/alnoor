using System.ComponentModel.DataAnnotations;
using ALNoor.Api.Shared.Data;

namespace ALNoor.Api.Features.Auth;

/// <summary>"web" delivers the refresh token as an HttpOnly cookie instead of in the body (PRD 16.2, 17.1).</summary>
public enum ClientType
{
    Android,
    Web,
}

public sealed record LoginRequest(
    [property: Required, StringLength(30)] string PhoneNumber,
    [property: Required, StringLength(PasswordPolicy.MaxLength, MinimumLength = 1)] string Password,
    [property: StringLength(100)] string? DeviceInfo = null,
    ClientType ClientType = ClientType.Android);

public sealed record RefreshRequest(
    [property: StringLength(100)] string? RefreshToken = null,
    ClientType ClientType = ClientType.Android);

public sealed record LogoutRequest([property: StringLength(100)] string? RefreshToken = null);

public sealed record ChangePasswordRequest(
    [property: Required, StringLength(PasswordPolicy.MaxLength, MinimumLength = 1)] string CurrentPassword,
    [property: Required, StringLength(PasswordPolicy.MaxLength, MinimumLength = 1)] string NewPassword,
    [property: StringLength(100)] string? RefreshToken = null);

public sealed record RegisterStudentRequest(
    [property: Required, StringLength(100, MinimumLength = 2)] string FullName,
    [property: Required, StringLength(30)] string PhoneNumber,
    [property: Required, StringLength(PasswordPolicy.MaxLength, MinimumLength = 1)] string TemporaryPassword);

public sealed record AuthUserDto(Guid Id, string FullName, UserRole Role, bool MustChangePassword);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string? RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthUserDto User);

public sealed record MeResponse(Guid Id, string FullName, string PhoneNumber, UserRole Role, bool IsActive, bool MustChangePassword);

public sealed record StudentDto(Guid Id, string FullName, string PhoneNumber, UserRole Role, bool IsActive, bool MustChangePassword);

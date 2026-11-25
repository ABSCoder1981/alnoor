namespace ALNoor.Api.Features.Auth;

/// <summary>
/// PRD 17.3: 8–128 characters, no forced complexity, block the most common passwords.
/// Returns an error message, or null when the password is acceptable.
/// </summary>
internal static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    // Most common passwords from public breach lists, plus local variants. Compared case-insensitively.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "12345678", "123456789", "1234567890", "12341234", "11111111", "00000000", "88888888", "87654321",
        "123123123", "11223344", "12121212", "11112222", "1q2w3e4r", "1qaz2wsx", "qwertyui", "qwerty123",
        "qwerty12", "qwertyuiop", "asdfghjk", "asdfasdf", "zxcvbnm1", "password", "password1", "password12",
        "password123", "passw0rd", "p@ssw0rd", "p@ssword", "iloveyou", "princess", "sunshine", "football",
        "baseball", "superman", "trustno1", "welcome1", "welcome123", "abcd1234", "abc12345", "abcdefgh",
        "letmein1", "admin123", "administrator", "changeme", "default1", "computer", "internet", "starwars",
        "whatever", "dragon12", "monkey12", "master12", "access14", "michael1", "jennifer", "1234qwer",
        "q1w2e3r4", "a1b2c3d4", "aa123456", "india123", "pakistan", "hindustan", "bismillah", "bismillah1",
        "allah123", "allahuakbar", "mashallah", "inshallah", "alhamdulillah", "subhanallah", "muhammad",
        "mohammed", "mohammad", "ramadan1", "makkah123", "madina123", "quran123", "alquran1", "alnoor123",
        "alnoor12", "teacher1", "student1", "student123", "class123", "school123",
    };

    public static string? Validate(string? password, string? currentPassword = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"Password must be at least {MinLength} characters.";
        }

        if (password.Length > MaxLength)
        {
            return $"Password must be at most {MaxLength} characters.";
        }

        if (Common.Contains(password))
        {
            return "This password is too common. Choose a different one.";
        }

        if (currentPassword is not null && string.Equals(password, currentPassword, StringComparison.Ordinal))
        {
            return "The new password must be different from the current one.";
        }

        return null;
    }
}

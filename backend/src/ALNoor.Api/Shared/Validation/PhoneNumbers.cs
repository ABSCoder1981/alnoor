using PhoneNumbers;

namespace ALNoor.Api.Shared.Validation;

/// <summary>Phone numbers are stored and compared in E.164 (PRD FR-AUTH-01, 15.1).</summary>
internal static class PhoneNumberNormalizer
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>Returns the E.164 form ("+919876543210"), or null if the input is not a valid international number.</summary>
    public static string? ToE164(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 30)
        {
            return null;
        }

        var trimmed = input.Trim();
        if (!trimmed.StartsWith('+'))
        {
            return null; // Country code is required; clients add it from the country picker.
        }

        try
        {
            var parsed = Util.Parse(trimmed, null);
            return Util.IsValidNumber(parsed) ? Util.Format(parsed, PhoneNumberFormat.E164) : null;
        }
        catch (NumberParseException)
        {
            return null;
        }
    }

    /// <summary>For logs/audit about unknown callers: keep only the last 4 digits.</summary>
    public static string Mask(string? phone) =>
        string.IsNullOrEmpty(phone) || phone.Length <= 4 ? "****" : $"****{phone[^4..]}";
}

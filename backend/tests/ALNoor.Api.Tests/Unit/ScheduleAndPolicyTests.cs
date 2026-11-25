using ALNoor.Api.Features.Auth;
using ALNoor.Api.Features.Classes;
using ALNoor.Api.Shared.Validation;
using ALNoor.Api.Shared.Web;

namespace ALNoor.Api.Tests.Unit;

public sealed class ScheduleCalculatorTests
{
    private static ScheduleSlot Slot(int day, string time, int minutes) =>
        new(day, TimeOnly.ParseExact(time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture), minutes);

    [Fact]
    public void Back_to_back_slots_do_not_overlap()
    {
        Assert.Null(ScheduleCalculator.FindOverlap([Slot(1, "17:00", 60), Slot(1, "18:00", 60)]));
    }

    [Fact]
    public void Overlapping_slots_on_the_same_day_are_found()
    {
        Assert.Equal((0, 1), ScheduleCalculator.FindOverlap([Slot(1, "17:00", 60), Slot(1, "17:59", 30)]));
    }

    [Fact]
    public void Overlap_across_midnight_and_across_the_week_boundary_is_found()
    {
        Assert.NotNull(ScheduleCalculator.FindOverlap([Slot(1, "23:30", 60), Slot(2, "00:15", 30)]));
        Assert.NotNull(ScheduleCalculator.FindOverlap([Slot(6, "23:30", 60), Slot(0, "00:00", 30)])); // Sat -> Sun
    }

    [Fact]
    public void Next_occurrences_are_converted_from_the_class_time_zone()
    {
        var kolkata = ScheduleCalculator.FindIanaZone("Asia/Kolkata")!;
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero); // Wednesday 17:30 in Kolkata.

        var next = ScheduleCalculator.NextOccurrences([Slot(1, "17:00", 45), Slot(3, "17:00", 45)], kolkata, now, 3);

        // Wednesday's 17:00-17:45 class is still running at 17:30, so it is included.
        Assert.Equal(
        [
            new DateTimeOffset(2026, 11, 25, 11, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 30, 11, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 2, 11, 30, 0, TimeSpan.Zero),
        ], next);
    }

    [Fact]
    public void Daylight_saving_changes_keep_the_local_time()
    {
        var london = ScheduleCalculator.FindIanaZone("Europe/London")!;
        var beforeChange = new DateTimeOffset(2027, 3, 20, 0, 0, 0, TimeSpan.Zero); // BST starts 28 March 2027.

        var next = ScheduleCalculator.NextOccurrences([Slot(0, "17:00", 60)], london, beforeChange, 2);

        Assert.Equal(new DateTimeOffset(2027, 3, 21, 17, 0, 0, TimeSpan.Zero), next[0]); // GMT
        Assert.Equal(new DateTimeOffset(2027, 3, 28, 16, 0, 0, TimeSpan.Zero), next[1]); // BST = UTC+1
    }

    [Fact]
    public void Slot_in_a_dst_gap_moves_forward()
    {
        var london = ScheduleCalculator.FindIanaZone("Europe/London")!;
        var now = new DateTimeOffset(2027, 3, 27, 0, 0, 0, TimeSpan.Zero);

        // 01:30 on 28 March 2027 does not exist in London (clocks jump 01:00 -> 02:00).
        var next = ScheduleCalculator.NextOccurrences([Slot(0, "01:30", 30)], london, now, 1);

        Assert.Equal(new DateTimeOffset(2027, 3, 28, 1, 30, 0, TimeSpan.Zero), Assert.Single(next)); // 02:30 BST
    }

    [Fact]
    public void Empty_schedule_has_no_occurrences()
    {
        Assert.Empty(ScheduleCalculator.NextOccurrences([], ScheduleCalculator.FindIanaZone("Asia/Kolkata")!, DateTimeOffset.UtcNow, 5));
    }

    [Theory]
    [InlineData("Asia/Kolkata", true)]
    [InlineData("Europe/London", true)]
    [InlineData("India Standard Time", false)]
    [InlineData("Not/AZone", false)]
    [InlineData("", false)]
    public void Only_iana_time_zones_are_accepted(string id, bool valid)
    {
        Assert.Equal(valid, ScheduleCalculator.FindIanaZone(id) is not null);
    }
}

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("short", false)]
    [InlineData("12345678", false)]
    [InlineData("Password123", false)]
    [InlineData("bismillah", false)]
    [InlineData("tajweed-practice", true)]
    [InlineData("eight ch", true)]
    public void Validates_length_and_common_passwords(string password, bool ok)
    {
        Assert.Equal(ok, PasswordPolicy.Validate(password) is null);
    }

    [Fact]
    public void Rejects_overlong_and_unchanged_passwords()
    {
        Assert.NotNull(PasswordPolicy.Validate(new string('a', 129)));
        Assert.NotNull(PasswordPolicy.Validate("same-password-1", currentPassword: "same-password-1"));
    }
}

public sealed class PhoneAndFormatTests
{
    [Theory]
    [InlineData("+919876543210", "+919876543210")]
    [InlineData(" +91 98765 43210 ", "+919876543210")]
    [InlineData("+44 7911 123456", "+447911123456")]
    [InlineData("9876543210", null)]
    [InlineData("+91123", null)]
    [InlineData("", null)]
    public void Phone_numbers_are_normalised_to_e164(string input, string? expected)
    {
        Assert.Equal(expected, PhoneNumberNormalizer.ToE164(input));
    }

    [Fact]
    public void Masking_keeps_only_the_last_four_digits()
    {
        Assert.Equal("****3210", PhoneNumberNormalizer.Mask("+919876543210"));
    }

    [Theory]
    [InlineData("Name", "name")]
    [InlineData("Schedule[0].StartTimeLocal", "schedule[0].startTimeLocal")]
    [InlineData("phoneNumber", "phoneNumber")]
    public void Validation_keys_become_camel_case(string input, string expected)
    {
        Assert.Equal(expected, ProblemDetailsCodes.ToCamelPath(input));
    }
}

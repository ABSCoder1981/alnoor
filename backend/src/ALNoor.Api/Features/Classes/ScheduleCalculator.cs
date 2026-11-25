using System.Globalization;

namespace ALNoor.Api.Features.Classes;

public sealed record ScheduleSlot(int DayOfWeek, TimeOnly StartTimeLocal, int DurationMinutes);

/// <summary>
/// Weekly schedule rules (PRD FR-CLS-01/05, 16.4): validation and upcoming occurrences in UTC.
/// Times are stored in the class's IANA time zone; clients convert UTC to the device's local time.
/// </summary>
internal static class ScheduleCalculator
{
    public const int MaxEntries = 14;
    public const int MinDuration = 10;
    public const int MaxDuration = 180;
    private const int MinutesPerWeek = 7 * 24 * 60;

    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Accepts only IANA ids (e.g. "Asia/Kolkata"), not Windows ids, so data is portable.</summary>
    public static TimeZoneInfo? FindIanaZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return null;
        }

        return zone.HasIanaId ? zone : null;
    }

    /// <summary>Returns the indexes of two overlapping slots (overlap measured around the week, including Sat→Sun), or null.</summary>
    public static (int First, int Second)? FindOverlap(IReadOnlyList<ScheduleSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        for (var i = 0; i < slots.Count; i++)
        {
            for (var j = i + 1; j < slots.Count; j++)
            {
                if (Overlaps(slots[i], slots[j]))
                {
                    return (i, j);
                }
            }
        }

        return null;
    }

    private static bool Overlaps(ScheduleSlot a, ScheduleSlot b)
    {
        var aStart = StartMinuteOfWeek(a);
        var bStart = StartMinuteOfWeek(b);
        // Distance from a's start to b's start going forward around the week, and vice versa.
        var aToB = ((bStart - aStart) % MinutesPerWeek + MinutesPerWeek) % MinutesPerWeek;
        var bToA = ((aStart - bStart) % MinutesPerWeek + MinutesPerWeek) % MinutesPerWeek;
        return aToB < a.DurationMinutes || bToA < b.DurationMinutes;
    }

    private static int StartMinuteOfWeek(ScheduleSlot s) => (s.DayOfWeek * 24 * 60) + (s.StartTimeLocal.Hour * 60) + s.StartTimeLocal.Minute;

    /// <summary>
    /// Next occurrences (UTC start times) that have not yet ended, in order. A slot that falls in a DST gap is moved
    /// forward by the gap; an ambiguous time uses the first (daylight) instance.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> NextOccurrences(IReadOnlyList<ScheduleSlot> slots, TimeZoneInfo zone, DateTimeOffset now, int count)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(zone);
        if (slots.Count == 0 || count <= 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var results = new List<DateTimeOffset>();
        // Look one day back (a slot may still be running past midnight) and far enough ahead to fill `count`.
        var days = Math.Max(8, (count / Math.Max(1, slots.Count) + 2) * 7);
        for (var offset = -1; offset <= days; offset++)
        {
            var date = today.AddDays(offset);
            foreach (var slot in slots.Where(s => s.DayOfWeek == (int)date.DayOfWeek))
            {
                var start = ToUtc(date.ToDateTime(slot.StartTimeLocal), zone);
                if (start.AddMinutes(slot.DurationMinutes) > now)
                {
                    results.Add(start);
                }
            }
        }

        return results.Distinct().Order().Take(count).ToList();
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            var adjustment = zone.GetAdjustmentRules().FirstOrDefault(r => r.DateStart <= unspecified && r.DateEnd >= unspecified);
            unspecified = unspecified.Add(adjustment?.DaylightDelta ?? TimeSpan.FromHours(1));
        }

        if (zone.IsAmbiguousTime(unspecified))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(unspecified);
            var daylight = offsets.Max();
            return new DateTimeOffset(unspecified, daylight).ToUniversalTime();
        }

        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }
}

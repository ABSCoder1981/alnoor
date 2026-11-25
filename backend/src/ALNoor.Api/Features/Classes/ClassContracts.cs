using System.ComponentModel.DataAnnotations;
using ALNoor.Api.Shared.Data;

namespace ALNoor.Api.Features.Classes;

public sealed record ScheduleEntryDto(
    [property: Range(0, 6)] int DayOfWeek,
    [property: Required] string StartTimeLocal,
    [property: Range(ScheduleCalculator.MinDuration, ScheduleCalculator.MaxDuration)] int DurationMinutes);

public sealed record SaveClassRequest(
    [property: Required, StringLength(100, MinimumLength = 2)] string Title,
    [property: StringLength(500)] string? Description,
    [property: Required, StringLength(64)] string TimeZoneId,
    [property: Range(ScheduleCalculator.MinDuration, ScheduleCalculator.MaxDuration)] int DefaultDurationMinutes,
    [property: Required, MaxLength(ScheduleCalculator.MaxEntries)] List<ScheduleEntryDto> Schedule,
    bool? IsActive = null);

public sealed record EnrollStudentsRequest([property: Required, MinLength(1), MaxLength(50)] List<Guid> StudentIds);

public sealed record TeacherRef(Guid Id, string FullName);

public sealed record EnrolledStudentDto(Guid Id, string FullName, EnrollmentStatus EnrollmentStatus);

public sealed record ClassDto(
    Guid Id,
    string Title,
    string? Description,
    string TimeZoneId,
    int DefaultDurationMinutes,
    bool IsActive,
    IReadOnlyList<ScheduleEntryDto> Schedule,
    IReadOnlyList<DateTimeOffset> NextOccurrences,
    int StudentCount,
    Guid? LiveSessionId,
    TeacherRef Teacher,
    IReadOnlyList<EnrolledStudentDto>? Students);

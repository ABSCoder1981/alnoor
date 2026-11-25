using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ALNoor.Api.Tests.Infrastructure;

namespace ALNoor.Api.Tests.Classes;

public sealed class ClassEndpointTests(DatabaseApiFixture fx) : IClassFixture<DatabaseApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static object NewClass(string title = "Tajweed – Group A", object[]? schedule = null) => new
    {
        title,
        description = "Beginners",
        timeZoneId = "Asia/Kolkata",
        defaultDurationMinutes = 45,
        schedule = schedule ?? new object[]
        {
            new { dayOfWeek = 3, startTimeLocal = "17:00", durationMinutes = 45 },
            new { dayOfWeek = 1, startTimeLocal = "17:00", durationMinutes = 45 },
        },
    };

    private static async Task<Guid> CreateClassAsync(HttpClient teacher, string title = "Tajweed – Group A")
    {
        using var response = await teacher.PostAsJsonAsync("/api/classes", NewClass(title), Ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await BodyAsync(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_returns_ordered_schedule_and_upcoming_times_in_utc()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync(name: "Ustadha Maryam"));

        using var response = await teacher.PostAsJsonAsync("/api/classes", NewClass(), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("Tajweed – Group A", body.GetProperty("title").GetString());
        Assert.Equal("Ustadha Maryam", body.GetProperty("teacher").GetProperty("fullName").GetString());
        Assert.Equal(0, body.GetProperty("studentCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("liveSessionId").ValueKind);

        var schedule = body.GetProperty("schedule").EnumerateArray().ToList();
        Assert.Equal(1, schedule[0].GetProperty("dayOfWeek").GetInt32());
        Assert.Equal("17:00", schedule[0].GetProperty("startTimeLocal").GetString());

        var next = body.GetProperty("nextOccurrences").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.NotEmpty(next);
        // 17:00 in Asia/Kolkata (UTC+05:30) is 11:30 UTC.
        Assert.All(next, t => Assert.EndsWith("T11:30:00Z", t, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_class_requests_are_rejected()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());

        using var windowsZone = await teacher.PostAsJsonAsync("/api/classes", new { title = "Class X", timeZoneId = "India Standard Time", defaultDurationMinutes = 45, schedule = Array.Empty<object>() }, Ct);
        using var badTime = await teacher.PostAsJsonAsync("/api/classes", NewClass(schedule: [new { dayOfWeek = 1, startTimeLocal = "5pm", durationMinutes = 45 }]), Ct);
        using var overlap = await teacher.PostAsJsonAsync("/api/classes", NewClass(schedule:
        [
            new { dayOfWeek = 1, startTimeLocal = "17:00", durationMinutes = 60 },
            new { dayOfWeek = 1, startTimeLocal = "17:30", durationMinutes = 30 },
        ]), Ct);
        using var tooLong = await teacher.PostAsJsonAsync("/api/classes", NewClass(schedule: [new { dayOfWeek = 1, startTimeLocal = "17:00", durationMinutes = 500 }]), Ct);
        using var badDay = await teacher.PostAsJsonAsync("/api/classes", NewClass(schedule: [new { dayOfWeek = 7, startTimeLocal = "17:00", durationMinutes = 45 }]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, windowsZone.StatusCode);
        Assert.True((await BodyAsync(windowsZone)).GetProperty("errors").TryGetProperty("timeZoneId", out _));
        Assert.Equal(HttpStatusCode.BadRequest, badTime.StatusCode);
        Assert.True((await BodyAsync(badTime)).GetProperty("errors").TryGetProperty("schedule[0].startTimeLocal", out _));
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badDay.StatusCode);
    }

    [Fact]
    public async Task Teacher_sees_and_edits_only_own_classes()
    {
        using var teacherA = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        using var teacherB = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var classA = await CreateClassAsync(teacherA, "Class of A");

        var listB = await teacherB.GetFromJsonAsync<JsonElement>("/api/classes", Ct);
        Assert.DoesNotContain(listB.EnumerateArray(), c => c.GetProperty("id").GetGuid() == classA);

        using var get = await teacherB.GetAsync($"/api/classes/{classA}", Ct);
        using var edit = await teacherB.PutAsJsonAsync($"/api/classes/{classA}", NewClass("Hijacked"), Ct);
        using var enroll = await teacherB.PostAsJsonAsync($"/api/classes/{classA}/students", new { studentIds = new[] { Guid.NewGuid() } }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, enroll.StatusCode);
    }

    [Fact]
    public async Task Update_replaces_schedule_and_archived_classes_are_hidden_by_default()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var id = await CreateClassAsync(teacher);

        using var update = await teacher.PutAsJsonAsync($"/api/classes/{id}", new
        {
            title = "Renamed",
            description = (string?)null,
            timeZoneId = "Asia/Kolkata",
            defaultDurationMinutes = 60,
            schedule = new[] { new { dayOfWeek = 5, startTimeLocal = "06:15", durationMinutes = 60 } },
            isActive = false,
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var body = await BodyAsync(update);
        Assert.Equal("Renamed", body.GetProperty("title").GetString());
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.Equal("06:15", Assert.Single(body.GetProperty("schedule").EnumerateArray()).GetProperty("startTimeLocal").GetString());

        var active = await teacher.GetFromJsonAsync<JsonElement>("/api/classes", Ct);
        Assert.DoesNotContain(active.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        var all = await teacher.GetFromJsonAsync<JsonElement>("/api/classes?includeInactive=true", Ct);
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Enrollment_controls_what_students_see()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var classId = await CreateClassAsync(teacher, "Enrolled Class");
        var otherClassId = await CreateClassAsync(teacher, "Not Enrolled Class");
        var (studentId, studentPhone) = await fx.CreateActiveStudentAsync(teacher, "Zainab");
        using var student = await fx.ClientForAsync(studentPhone);

        Assert.Empty((await student.GetFromJsonAsync<JsonElement>("/api/classes", Ct)).EnumerateArray());

        using var enroll = await teacher.PostAsJsonAsync($"/api/classes/{classId}/students", new { studentIds = new[] { studentId } }, Ct);
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
        Assert.Equal("Zainab", Assert.Single((await BodyAsync(enroll)).EnumerateArray()).GetProperty("fullName").GetString());

        var visible = await student.GetFromJsonAsync<JsonElement>("/api/classes", Ct);
        Assert.Equal(classId, Assert.Single(visible.EnumerateArray()).GetProperty("id").GetGuid());

        using var details = await student.GetAsync($"/api/classes/{classId}", Ct);
        var detailBody = await BodyAsync(details);
        Assert.Equal(1, detailBody.GetProperty("studentCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, detailBody.GetProperty("students").ValueKind); // Students never see the roster.

        using var notEnrolled = await student.GetAsync($"/api/classes/{otherClassId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, notEnrolled.StatusCode);

        using var studentEdit = await student.PostAsJsonAsync("/api/classes", NewClass(), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, studentEdit.StatusCode);

        using var remove = await teacher.DeleteAsync($"/api/classes/{classId}/students/{studentId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Empty((await student.GetFromJsonAsync<JsonElement>("/api/classes", Ct)).EnumerateArray());

        var teacherView = await teacher.GetFromJsonAsync<JsonElement>($"/api/classes/{classId}", Ct);
        Assert.Equal("Removed", Assert.Single(teacherView.GetProperty("students").EnumerateArray()).GetProperty("enrollmentStatus").GetString());
    }

    [Fact]
    public async Task Enrollment_is_idempotent_and_capped_at_nine()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var classId = await CreateClassAsync(teacher);
        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            ids.Add((await DatabaseApiFixture.CreateStudentAsync(teacher, $"Student {i}")).Id);
        }

        using var nine = await teacher.PostAsJsonAsync($"/api/classes/{classId}/students", new { studentIds = ids.Take(9) }, Ct);
        Assert.Equal(HttpStatusCode.OK, nine.StatusCode);
        Assert.Equal(9, (await BodyAsync(nine)).GetArrayLength());

        using var again = await teacher.PostAsJsonAsync($"/api/classes/{classId}/students", new { studentIds = ids.Take(3) }, Ct);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        using var tenth = await teacher.PostAsJsonAsync($"/api/classes/{classId}/students", new { studentIds = new[] { ids[9] } }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, tenth.StatusCode);
        Assert.Equal("CLASS_FULL", (await BodyAsync(tenth)).GetProperty("code").GetString());

        // Removing one frees a place.
        (await teacher.DeleteAsync($"/api/classes/{classId}/students/{ids[0]}", Ct)).Dispose();
        using var afterRemoval = await teacher.PostAsJsonAsync($"/api/classes/{classId}/students", new { studentIds = new[] { ids[9] } }, Ct);
        Assert.Equal(HttpStatusCode.OK, afterRemoval.StatusCode);
    }

    [Fact]
    public async Task Concurrent_enrollments_cannot_exceed_the_cap()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var classId = await CreateClassAsync(teacher);
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            ids.Add((await DatabaseApiFixture.CreateStudentAsync(teacher, $"Racer {i}")).Id);
        }

        var tasks = ids.Select(id => teacher.PostAsJsonAsync($"/api/classes/{classId}/students", new { studentIds = new[] { id } }, Ct)).ToList();
        var responses = await Task.WhenAll(tasks);

        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var r in responses)
        {
            r.Dispose();
        }
    }

    [Fact]
    public async Task Deactivated_or_unknown_students_cannot_be_enrolled()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var classId = await CreateClassAsync(teacher);
        var (studentId, _) = await DatabaseApiFixture.CreateStudentAsync(teacher);
        (await teacher.PutAsJsonAsync($"/api/students/{studentId}/status", new { isActive = false }, Ct)).Dispose();

        using var inactive = await teacher.PostAsJsonAsync($"/api/classes/{classId}/students", new { studentIds = new[] { studentId } }, Ct);
        using var unknown = await teacher.PostAsJsonAsync($"/api/classes/{classId}/students", new { studentIds = new[] { Guid.NewGuid() } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }
}

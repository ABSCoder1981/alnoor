using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ALNoor.Api.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace ALNoor.Api.Tests.Sessions;

public sealed class SessionEndpointTests(DatabaseApiFixture fx) : IClassFixture<DatabaseApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private sealed record Setup(HttpClient Teacher, Guid TeacherId, Guid ClassId, List<(Guid Id, string Phone)> Students) : IDisposable
    {
        public void Dispose() => Teacher.Dispose();
    }

    /// <summary>A teacher with one class and the given number of active, enrolled students.</summary>
    private async Task<Setup> SetupAsync(int students = 1, string teacherName = "Ustadh Ahmed")
    {
        var teacherPhone = await fx.CreateTeacherAsync(name: teacherName);
        var teacher = await fx.ClientForAsync(teacherPhone);
        var me = await teacher.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        using var create = await teacher.PostAsJsonAsync("/api/classes", new { title = "Hifz Group", timeZoneId = "Asia/Kolkata", defaultDurationMinutes = 60, schedule = Array.Empty<object>() }, Ct);
        var classId = (await BodyAsync(create)).GetProperty("id").GetGuid();
        var list = new List<(Guid, string)>();
        for (var i = 0; i < students; i++)
        {
            list.Add(await fx.CreateActiveStudentAsync(teacher, $"Student{i} Family"));
        }

        if (students > 0)
        {
            using var enroll = await teacher.PostAsJsonAsync($"/api/classes/{classId}/students", new { studentIds = list.Select(s => s.Item1) }, Ct);
            Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
        }

        return new Setup(teacher, me.GetProperty("id").GetGuid(), classId, list);
    }

    private static async Task<Guid> StartAsync(Setup s)
    {
        using var start = await s.Teacher.PostAsync($"/api/classes/{s.ClassId}/sessions", null, Ct);
        Assert.True(start.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await start.Content.ReadAsStringAsync(Ct));
        return (await BodyAsync(start)).GetProperty("id").GetGuid();
    }

    private static async Task<string> RoomOfAsync(Setup s, Guid sessionId)
    {
        using var join = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        return (await BodyAsync(join)).GetProperty("roomName").GetString()!;
    }

    private static JsonElement TokenPayload(string token) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]))).RootElement.Clone();

    [Fact]
    public async Task Teacher_starts_a_session_once_and_the_class_shows_it_live()
    {
        using var s = await SetupAsync();

        using var first = await s.Teacher.PostAsync($"/api/classes/{s.ClassId}/sessions", null, Ct);
        using var again = await s.Teacher.PostAsync($"/api/classes/{s.ClassId}/sessions", null, Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode); // FR-SES-02
        var id = (await BodyAsync(first)).GetProperty("id").GetGuid();
        var againBody = await BodyAsync(again);
        Assert.Equal(id, againBody.GetProperty("id").GetGuid());
        Assert.Equal("Live", againBody.GetProperty("status").GetString());

        var cls = await s.Teacher.GetFromJsonAsync<JsonElement>($"/api/classes/{s.ClassId}", Ct);
        Assert.Equal(id, cls.GetProperty("liveSessionId").GetGuid());

        var room = await RoomOfAsync(s, id);
        Assert.Matches("^s_[0-9a-f]{24}$", room); // Random, no personal data.
        Assert.True(fx.Api.Rooms.RoomExists(room));
    }

    [Fact]
    public async Task Parallel_starts_create_exactly_one_live_session()
    {
        using var s = await SetupAsync(students: 0);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => s.Teacher.PostAsync($"/api/classes/{s.ClassId}/sessions", null, Ct)));

        var ids = new HashSet<Guid>();
        foreach (var r in responses)
        {
            Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"{(int)r.StatusCode}");
            ids.Add((await BodyAsync(r)).GetProperty("id").GetGuid());
            r.Dispose();
        }

        Assert.Single(ids);
    }

    [Fact]
    public async Task Start_fails_cleanly_when_livekit_is_down()
    {
        using var s = await SetupAsync(students: 0);
        fx.Api.Rooms.Unavailable = true;
        try
        {
            using var start = await s.Teacher.PostAsync($"/api/classes/{s.ClassId}/sessions", null, Ct);

            Assert.Equal(HttpStatusCode.BadGateway, start.StatusCode);
            Assert.Equal("RTC_UNAVAILABLE", (await BodyAsync(start)).GetProperty("code").GetString());
        }
        finally
        {
            fx.Api.Rooms.Unavailable = false;
        }

        using var active = await s.Teacher.GetAsync($"/api/classes/{s.ClassId}/sessions/active", Ct);
        Assert.Equal(HttpStatusCode.NoContent, active.StatusCode); // No session was created.
    }

    [Fact]
    public async Task Archived_class_cannot_start_and_live_class_cannot_be_archived()
    {
        using var s = await SetupAsync(students: 0);
        var body = new { title = "Hifz Group", timeZoneId = "Asia/Kolkata", defaultDurationMinutes = 60, schedule = Array.Empty<object>(), isActive = false };

        await StartAsync(s);
        using var archiveWhileLive = await s.Teacher.PutAsJsonAsync($"/api/classes/{s.ClassId}", body, Ct);
        Assert.Equal(HttpStatusCode.Conflict, archiveWhileLive.StatusCode);
        Assert.Equal("SESSION_LIVE", (await BodyAsync(archiveWhileLive)).GetProperty("code").GetString());

        using var other = await SetupAsync(students: 0);
        (await other.Teacher.PutAsJsonAsync($"/api/classes/{other.ClassId}", body, Ct)).Dispose();
        using var start = await other.Teacher.PostAsync($"/api/classes/{other.ClassId}/sessions", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
        Assert.Equal("CLASS_INACTIVE", (await BodyAsync(start)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Enrolled_student_sees_the_live_session_and_gets_a_student_token()
    {
        using var s = await SetupAsync();
        using var student = await fx.ClientForAsync(s.Students[0].Phone);

        using var before = await student.GetAsync($"/api/classes/{s.ClassId}/sessions/active", Ct);
        Assert.Equal(HttpStatusCode.NoContent, before.StatusCode); // "Waiting for teacher"

        var sessionId = await StartAsync(s);
        var active = await student.GetFromJsonAsync<JsonElement>($"/api/classes/{s.ClassId}/sessions/active", Ct);
        Assert.Equal(sessionId, active.GetProperty("id").GetGuid());
        Assert.Equal("Hifz Group", active.GetProperty("classTitle").GetString());

        using var join = await student.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        var body = await BodyAsync(join);
        Assert.Equal("Student", body.GetProperty("role").GetString());
        Assert.Equal(s.Students[0].Id.ToString(), body.GetProperty("identity").GetString());
        Assert.Equal(ApiFactory.LiveKitUrl, body.GetProperty("livekitUrl").GetString()!.TrimEnd('/'));

        var payload = TokenPayload(body.GetProperty("token").GetString()!);
        Assert.Equal(s.Students[0].Id.ToString(), payload.GetProperty("sub").GetString());
        Assert.Equal("Student0", payload.GetProperty("name").GetString()); // First name only (PRD A2).
        Assert.Equal(body.GetProperty("roomName").GetString(), payload.GetProperty("video").GetProperty("room").GetString());
        Assert.False(payload.GetProperty("video").GetProperty("canPublishData").GetBoolean());
    }

    [Fact]
    public async Task Join_checks_enrollment_session_state_and_blocks_in_order()
    {
        using var s = await SetupAsync(students: 2);
        using var outsiderTeacher = await SetupAsync(students: 1);
        var sessionId = await StartAsync(s);

        using var stranger = await fx.ClientForAsync(outsiderTeacher.Students[0].Phone);
        using var notEnrolled = await stranger.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, notEnrolled.StatusCode);
        Assert.Equal("NOT_ENROLLED", (await BodyAsync(notEnrolled)).GetProperty("code").GetString());

        using var otherTeacherJoins = await outsiderTeacher.Teacher.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, otherTeacherJoins.StatusCode);

        // Removed by the teacher: cannot rejoin this session (M9).
        using var remove = await s.Teacher.PostAsJsonAsync($"/api/sessions/{sessionId}/participants/{s.Students[0].Id}/remove", new { reason = "Background noise" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var removedStudent = await fx.ClientForAsync(s.Students[0].Phone);
        using var blocked = await removedStudent.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("REMOVED_FROM_SESSION", (await BodyAsync(blocked)).GetProperty("code").GetString());

        // Unenrolled students lose access immediately.
        (await s.Teacher.DeleteAsync($"/api/classes/{s.ClassId}/students/{s.Students[1].Id}", Ct)).Dispose();
        using var unenrolled = await fx.ClientForAsync(s.Students[1].Phone);
        using var unenrolledJoin = await unenrolled.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        Assert.Equal("NOT_ENROLLED", (await BodyAsync(unenrolledJoin)).GetProperty("code").GetString());

        // After the end, nobody gets a token; the next session is open again for the removed student.
        using var end = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/end", null, Ct);
        using var teacherAfterEnd = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, teacherAfterEnd.StatusCode);
        Assert.Equal("SESSION_NOT_LIVE", (await BodyAsync(teacherAfterEnd)).GetProperty("code").GetString());

        var next = await StartAsync(s);
        using var nextJoin = await removedStudent.PostAsync($"/api/sessions/{next}/join", null, Ct);
        Assert.Equal(HttpStatusCode.OK, nextJoin.StatusCode);
    }

    [Fact]
    public async Task Room_holds_nine_students_and_the_teacher_slot_is_reserved()
    {
        using var s = await SetupAsync(students: 9);
        var sessionId = await StartAsync(s);
        var room = await RoomOfAsync(s, sessionId);

        // Nine other students already connected (simulated), plus a stranger identity that is not ours.
        var extra = Guid.NewGuid();
        foreach (var (id, _) in s.Students.Skip(1))
        {
            fx.Api.Rooms.AddParticipant(room, id, "x");
        }

        fx.Api.Rooms.AddParticipant(room, extra, "ghost");
        using var ninth = await fx.ClientForAsync(s.Students[0].Phone);
        using var full = await ninth.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal("ROOM_FULL", (await BodyAsync(full)).GetProperty("code").GetString());

        using var teacherStillJoins = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        Assert.Equal(HttpStatusCode.OK, teacherStillJoins.StatusCode);

        // A student already in the room may reconnect (replaces their old connection).
        await fx.Api.Rooms.RemoveParticipantAsync(room, extra.ToString(), Ct);
        fx.Api.Rooms.AddParticipant(room, s.Students[0].Id, "me");
        using var reconnect = await ninth.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);
        Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
    }

    [Fact]
    public async Task Teacher_moderates_participants()
    {
        using var s = await SetupAsync(students: 3);
        var sessionId = await StartAsync(s);
        var room = await RoomOfAsync(s, sessionId);
        fx.Api.Rooms.AddParticipant(room, s.TeacherId, "Ustadh");
        fx.Api.Rooms.AddParticipant(room, s.Students[0].Id, "S0");
        fx.Api.Rooms.AddParticipant(room, s.Students[1].Id, "S1", cameraOn: true);
        fx.Api.Rooms.AddParticipant(room, s.Students[2].Id, "S2", micPublished: false);

        var list = await s.Teacher.GetFromJsonAsync<JsonElement>($"/api/sessions/{sessionId}/participants", Ct);
        Assert.Equal(4, list.GetArrayLength());
        Assert.Equal("Teacher", list[0].GetProperty("role").GetString());
        var s1 = list.EnumerateArray().Single(p => p.GetProperty("userId").GetGuid() == s.Students[1].Id);
        Assert.True(s1.GetProperty("isCameraOn").GetBoolean());
        Assert.Equal("Student1 Family", s1.GetProperty("fullName").GetString());

        using var mute = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/participants/{s.Students[0].Id}/mute", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, mute.StatusCode);
        Assert.True(fx.Api.Rooms.Participants(room).Single(p => p.Identity == s.Students[0].Id.ToString()).Microphone!.Muted);
        Assert.Contains($"Notice {room} muted [{s.Students[0].Id}]", fx.Api.Rooms.Calls); // "The teacher muted you"

        using var noMic = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/participants/{s.Students[2].Id}/mute", null, Ct);
        Assert.Equal("NO_MICROPHONE_TRACK", (await BodyAsync(noMic)).GetProperty("code").GetString());

        using var absent = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/participants/{Guid.NewGuid()}/mute", null, Ct);
        Assert.Equal("PARTICIPANT_NOT_IN_ROOM", (await BodyAsync(absent)).GetProperty("code").GetString());

        using var self = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/participants/{s.TeacherId}/mute", null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);

        // Mute all: only students with a live mic (S1), never the teacher.
        using var all = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/participants/mute-all", null, Ct);
        Assert.Equal(1, (await BodyAsync(all)).GetProperty("mutedCount").GetInt32());
        Assert.Contains($"Notice {room} muted_all []", fx.Api.Rooms.Calls); // "The teacher muted everyone"
        Assert.False(fx.Api.Rooms.Participants(room).Single(p => p.Identity == s.TeacherId.ToString()).Microphone!.Muted);

        using var remove = await s.Teacher.PostAsJsonAsync($"/api/sessions/{sessionId}/participants/{s.Students[1].Id}/remove", new { }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.DoesNotContain(fx.Api.Rooms.Participants(room), p => p.Identity == s.Students[1].Id.ToString());
        using var removeAgain = await s.Teacher.PostAsJsonAsync($"/api/sessions/{sessionId}/participants/{s.Students[1].Id}/remove", new { }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, removeAgain.StatusCode); // Idempotent.
    }

    [Fact]
    public async Task Students_cannot_moderate_or_end()
    {
        using var s = await SetupAsync(students: 2);
        var sessionId = await StartAsync(s);
        using var student = await fx.ClientForAsync(s.Students[0].Phone);

        foreach (var path in new[] { "end", "participants/mute-all", $"participants/{s.Students[1].Id}/mute", $"participants/{s.Students[1].Id}/remove" })
        {
            using var r = await student.PostAsJsonAsync($"/api/sessions/{sessionId}/{path}", new { }, Ct);
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"{path}: {(int)r.StatusCode}");
        }

        using var participants = await student.GetAsync($"/api/sessions/{sessionId}/participants", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, participants.StatusCode);
        using var start = await student.PostAsync($"/api/classes/{s.ClassId}/sessions", null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
    }

    [Fact]
    public async Task End_closes_the_room_and_is_idempotent()
    {
        using var s = await SetupAsync(students: 0);
        var sessionId = await StartAsync(s);
        var room = await RoomOfAsync(s, sessionId);

        using var end = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/end", null, Ct);
        using var again = await s.Teacher.PostAsync($"/api/sessions/{sessionId}/end", null, Ct);

        var body = await BodyAsync(end);
        Assert.Equal("Ended", body.GetProperty("status").GetString());
        Assert.Equal("Teacher", body.GetProperty("endReason").GetString());
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False(fx.Api.Rooms.RoomExists(room));
        Assert.Equal(1, fx.Api.Rooms.Calls.Count(c => c == $"DeleteRoom {room}"));

        var cls = await s.Teacher.GetFromJsonAsync<JsonElement>($"/api/classes/{s.ClassId}", Ct);
        Assert.Equal(JsonValueKind.Null, cls.GetProperty("liveSessionId").ValueKind);
    }

    [Fact]
    public async Task Deactivating_or_unenrolling_a_student_removes_them_from_the_live_room()
    {
        using var s = await SetupAsync(students: 2);
        var sessionId = await StartAsync(s);
        var room = await RoomOfAsync(s, sessionId);
        fx.Api.Rooms.AddParticipant(room, s.Students[0].Id, "S0");
        fx.Api.Rooms.AddParticipant(room, s.Students[1].Id, "S1");

        (await s.Teacher.PutAsJsonAsync($"/api/students/{s.Students[0].Id}/status", new { isActive = false }, Ct)).Dispose();
        (await s.Teacher.DeleteAsync($"/api/classes/{s.ClassId}/students/{s.Students[1].Id}", Ct)).Dispose();

        Assert.Empty(fx.Api.Rooms.Participants(room));
    }

    [Fact]
    public async Task Sessions_of_other_teachers_are_not_found()
    {
        using var a = await SetupAsync(students: 0);
        using var b = await SetupAsync(students: 0);
        var sessionA = await StartAsync(a);

        using var get = await b.Teacher.GetAsync($"/api/sessions/{sessionA}", Ct);
        using var end = await b.Teacher.PostAsync($"/api/sessions/{sessionA}/end", null, Ct);
        using var start = await b.Teacher.PostAsync($"/api/classes/{a.ClassId}/sessions", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, end.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, start.StatusCode);
    }

    [Fact]
    public async Task Join_is_rate_limited_per_user()
    {
        using var s = await SetupAsync(students: 0);
        var sessionId = await StartAsync(s);
        await using var limited = new ApiFactory(fx.ConnectionString, overrides: new Dictionary<string, string?> { ["Sessions:JoinRequestsPerMinute"] = "2" });
        using var client = limited.CreateClient();
        client.DefaultRequestHeaders.Authorization = s.Teacher.DefaultRequestHeaders.Authorization;

        (await client.PostAsync($"/api/sessions/{sessionId}/join", null, Ct)).Dispose();
        (await client.PostAsync($"/api/sessions/{sessionId}/join", null, Ct)).Dispose();
        using var third = await client.PostAsync($"/api/sessions/{sessionId}/join", null, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("RATE_LIMITED", (await BodyAsync(third)).GetProperty("code").GetString());
    }

    [Fact]
    public void First_name_is_the_first_word()
    {
        Assert.Equal("Aisha", ALNoor.Api.Features.Sessions.SessionEndpoints.FirstName("  Aisha Khan Rahman "));
        Assert.Equal("Bilal", ALNoor.Api.Features.Sessions.SessionEndpoints.FirstName("Bilal"));
    }
}

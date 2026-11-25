using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ALNoor.Api.Tests.Infrastructure;

namespace ALNoor.Api.Tests.Students;

public sealed class StudentEndpointTests(DatabaseApiFixture fx) : IClassFixture<DatabaseApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>(Ct);

    [Fact]
    public async Task Teacher_lists_only_their_own_students_with_search_and_filter()
    {
        using var teacherA = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        using var teacherB = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var (aishaId, _) = await DatabaseApiFixture.CreateStudentAsync(teacherA, "Aisha Rahman");
        var (bilalId, _) = await DatabaseApiFixture.CreateStudentAsync(teacherA, "Bilal Siddiqui");
        var (otherId, _) = await DatabaseApiFixture.CreateStudentAsync(teacherB, "Other Teacher Student");

        var all = await teacherA.GetFromJsonAsync<JsonElement>("/api/students", Ct);
        var ids = all.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(aishaId, ids);
        Assert.Contains(bilalId, ids);
        Assert.DoesNotContain(otherId, ids);

        var search = await teacherA.GetFromJsonAsync<JsonElement>("/api/students?search=aish", Ct);
        Assert.Equal(aishaId, Assert.Single(search.EnumerateArray()).GetProperty("id").GetGuid());

        // LIKE wildcards in the search term are treated as text.
        var wildcard = await teacherA.GetFromJsonAsync<JsonElement>("/api/students?search=%25", Ct);
        Assert.Empty(wildcard.EnumerateArray());

        using var otherTeachersStudent = await teacherA.GetAsync($"/api/students/{otherId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, otherTeachersStudent.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_change_another_teachers_student()
    {
        using var teacherA = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        using var teacherB = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var (studentId, _) = await DatabaseApiFixture.CreateStudentAsync(teacherB);

        using var update = await teacherA.PutAsJsonAsync($"/api/students/{studentId}", new { fullName = "Hijacked", phoneNumber = DatabaseApiFixture.NewPhone() }, Ct);
        using var status = await teacherA.PutAsJsonAsync($"/api/students/{studentId}/status", new { isActive = false }, Ct);
        using var reset = await teacherA.PostAsJsonAsync($"/api/students/{studentId}/reset-password", new { temporaryPassword = "temporary-pass-999" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
    }

    [Fact]
    public async Task Students_cannot_use_student_management()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var (_, phone) = await fx.CreateActiveStudentAsync(teacher);
        using var student = await fx.ClientForAsync(phone);

        using var list = await student.GetAsync("/api/students", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Update_normalises_phone_and_rejects_a_phone_in_use()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var (id, _) = await DatabaseApiFixture.CreateStudentAsync(teacher, "Old Name");
        var (_, takenPhone) = await DatabaseApiFixture.CreateStudentAsync(teacher, "Other");
        var newPhone = DatabaseApiFixture.NewPhone();

        using var ok = await teacher.PutAsJsonAsync($"/api/students/{id}", new { fullName = "  New Name  ", phoneNumber = $"{newPhone[..3]} {newPhone[3..]}" }, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await BodyAsync(ok);
        Assert.Equal("New Name", body.GetProperty("fullName").GetString());
        Assert.Equal(newPhone, body.GetProperty("phoneNumber").GetString());

        using var conflict = await teacher.PutAsJsonAsync($"/api/students/{id}", new { fullName = "New Name", phoneNumber = takenPhone }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("PHONE_ALREADY_REGISTERED", (await BodyAsync(conflict)).GetProperty("code").GetString());

        using var invalid = await teacher.PutAsJsonAsync($"/api/students/{id}", new { fullName = "X", phoneNumber = "123" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await BodyAsync(invalid)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("fullName", out _), errors.ToString()); // camelCase keys (PRD 16.1)
    }

    [Fact]
    public async Task Reset_password_sets_a_temporary_password_and_signs_the_student_out()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var (id, phone) = await fx.CreateActiveStudentAsync(teacher);
        var session = await fx.LoginAsync(phone);

        using var reset = await teacher.PostAsJsonAsync($"/api/students/{id}/reset-password", new { temporaryPassword = "fresh-temporary-77" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using var anonymous = fx.Client();
        using var oldPassword = await anonymous.PostAsJsonAsync("/api/auth/login", new { phoneNumber = phone, password = DatabaseApiFixture.DefaultPassword }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);

        using var oldRefresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.GetProperty("refreshToken").GetString() }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);

        var temporary = await fx.LoginAsync(phone, "fresh-temporary-77");
        Assert.True(temporary.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Reactivated_student_can_log_in_again()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var (id, phone) = await fx.CreateActiveStudentAsync(teacher);

        (await teacher.PutAsJsonAsync($"/api/students/{id}/status", new { isActive = false }, Ct)).Dispose();
        using var reactivate = await teacher.PutAsJsonAsync($"/api/students/{id}/status", new { isActive = true }, Ct);

        Assert.True((await BodyAsync(reactivate)).GetProperty("isActive").GetBoolean());
        await fx.LoginAsync(phone);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ALNoor.Api.Tests.Auth;

public sealed class AuthEndpointTests(DatabaseApiFixture fx) : IClassFixture<DatabaseApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<HttpResponseMessage> PostLoginAsync(string phone, string password, string clientType = "Android")
    {
        using var client = fx.Client();
        return await client.PostAsJsonAsync("/api/auth/login", new { phoneNumber = phone, password, clientType }, Ct);
    }

    [Fact]
    public async Task Login_returns_tokens_and_user_and_access_token_works()
    {
        var phone = await fx.CreateTeacherAsync(name: "Ustadh Ahmed");

        var login = await fx.LoginAsync(phone);

        Assert.False(string.IsNullOrEmpty(login.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(login.GetProperty("refreshToken").GetString()));
        Assert.EndsWith("Z", login.GetProperty("accessTokenExpiresAt").GetString(), StringComparison.Ordinal);
        Assert.Equal("Teacher", login.GetProperty("user").GetProperty("role").GetString());
        Assert.False(login.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());

        using var client = fx.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        using var me = await client.GetAsync("/api/auth/me", Ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await BodyAsync(me);
        Assert.Equal(phone, body.GetProperty("phoneNumber").GetString());
        Assert.Equal("Ustadh Ahmed", body.GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Wrong_password_and_unknown_phone_get_the_same_answer()
    {
        var phone = await fx.CreateTeacherAsync();

        using var wrong = await PostLoginAsync(phone, "not-the-password");
        using var unknown = await PostLoginAsync(DatabaseApiFixture.NewPhone(), "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        var a = await BodyAsync(wrong);
        var b = await BodyAsync(unknown);
        Assert.Equal("INVALID_CREDENTIALS", a.GetProperty("code").GetString());
        Assert.Equal(a.GetProperty("code").GetString(), b.GetProperty("code").GetString());
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("9876543210")]
    [InlineData("+91 12")]
    [InlineData("not a phone")]
    public async Task Invalid_phone_format_returns_400(string phone)
    {
        using var response = await PostLoginAsync(phone, "whatever-123");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("VALIDATION_FAILED", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("phoneNumber", out _));
    }

    [Fact]
    public async Task Phone_with_spaces_is_normalised_to_e164()
    {
        var phone = await fx.CreateTeacherAsync();
        var spaced = $"{phone[..3]} {phone[3..8]} {phone[8..]}";

        using var response = await PostLoginAsync(spaced, DatabaseApiFixture.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_even_for_the_correct_password()
    {
        var phone = await fx.CreateTeacherAsync();

        for (var i = 1; i <= 4; i++)
        {
            using var failed = await PostLoginAsync(phone, $"wrong-password-{i}");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using var fifth = await PostLoginAsync(phone, "wrong-password-5");
        Assert.Equal((HttpStatusCode)423, fifth.StatusCode);
        var body = await BodyAsync(fifth);
        Assert.Equal("ACCOUNT_LOCKED", body.GetProperty("code").GetString());
        var until = DateTimeOffset.Parse(body.GetProperty("lockoutEndsAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(until - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));

        using var correct = await PostLoginAsync(phone, DatabaseApiFixture.DefaultPassword);
        Assert.Equal((HttpStatusCode)423, correct.StatusCode);
    }

    [Fact]
    public async Task Successful_login_resets_the_failure_counter()
    {
        var phone = await fx.CreateTeacherAsync();
        for (var i = 0; i < 4; i++)
        {
            (await PostLoginAsync(phone, "wrong-password")).Dispose();
        }

        (await PostLoginAsync(phone, DatabaseApiFixture.DefaultPassword)).Dispose();
        for (var i = 0; i < 4; i++)
        {
            (await PostLoginAsync(phone, "wrong-password")).Dispose();
        }

        using var stillOpen = await PostLoginAsync(phone, DatabaseApiFixture.DefaultPassword);
        Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_an_old_token_revokes_the_family()
    {
        var phone = await fx.CreateTeacherAsync();
        var login = await fx.LoginAsync(phone);
        var first = login.GetProperty("refreshToken").GetString();
        using var client = fx.Client();

        using var rotated = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first }, Ct);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = (await BodyAsync(rotated)).GetProperty("refreshToken").GetString();
        Assert.NotEqual(first, second);

        // The old token comes back (stolen copy): 401 and the whole family is revoked.
        using var reused = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal("REFRESH_TOKEN_REUSED", (await BodyAsync(reused)).GetProperty("code").GetString());

        using var newestAfterTheft = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = second }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, newestAfterTheft.StatusCode);
    }

    [Fact]
    public async Task Unknown_refresh_token_is_rejected()
    {
        using var client = fx.Client();

        using var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "made-up-token" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("INVALID_REFRESH_TOKEN", (await BodyAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_tokens_are_stored_only_as_hashes()
    {
        var phone = await fx.CreateTeacherAsync();
        var raw = (await fx.LoginAsync(phone)).GetProperty("refreshToken").GetString()!;

        await fx.WithDbAsync(async db =>
        {
            Assert.False(await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(t => t.TokenHash == raw, Ct));
            Assert.True(await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(t => t.TokenHash.Length == 64, Ct));
        });
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var phone = await fx.CreateTeacherAsync();
        var login = await fx.LoginAsync(phone);
        using var client = fx.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());

        using var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = login.GetProperty("refreshToken").GetString() }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.GetProperty("refreshToken").GetString() }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("INVALID_REFRESH_TOKEN", (await BodyAsync(refresh)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Web_login_uses_an_httponly_strict_cookie_and_refresh_reads_it()
    {
        var phone = await fx.CreateTeacherAsync();
        // Secure cookies are only sent over HTTPS, as in a real browser.
        using var client = fx.Api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var login = await client.PostAsJsonAsync("/api/auth/login", new { phoneNumber = phone, password = DatabaseApiFixture.DefaultPassword, clientType = "web" }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await BodyAsync(login)).GetProperty("refreshToken").ValueKind);

        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("alnoor_rt=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);

        // WebApplicationFactory's client keeps cookies, like a browser.
        using var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { clientType = "web" }, Ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.False(string.IsNullOrEmpty((await BodyAsync(refresh)).GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Protected_endpoints_require_a_valid_token()
    {
        using var anonymous = fx.Client();
        using var noToken = await anonymous.GetAsync("/api/auth/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.Equal("UNAUTHENTICATED", (await BodyAsync(noToken)).GetProperty("code").GetString());

        using var forged = fx.Client();
        forged.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.invalid");
        using var forgedResponse = await forged.GetAsync("/api/auth/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);
    }

    [Fact]
    public async Task New_student_must_change_password_before_using_the_api()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var (_, studentPhone) = await DatabaseApiFixture.CreateStudentAsync(teacher, temporaryPassword: "temporary-pass-123");

        var login = await fx.LoginAsync(studentPhone, "temporary-pass-123");
        Assert.True(login.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
        using var student = fx.Client();
        student.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());

        using var blocked = await student.GetAsync("/api/classes", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", (await BodyAsync(blocked)).GetProperty("code").GetString());

        using var me = await student.GetAsync("/api/auth/me", Ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using var common = await student.PutAsJsonAsync("/api/auth/password", new { currentPassword = "temporary-pass-123", newPassword = "password123" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, common.StatusCode);
        Assert.True((await BodyAsync(common)).GetProperty("errors").TryGetProperty("newPassword", out _));

        using var same = await student.PutAsJsonAsync("/api/auth/password", new { currentPassword = "temporary-pass-123", newPassword = "temporary-pass-123" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);

        using var wrongCurrent = await student.PutAsJsonAsync("/api/auth/password", new { currentPassword = "nope-nope-nope", newPassword = "a-good-new-password" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCurrent.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", (await BodyAsync(wrongCurrent)).GetProperty("code").GetString());

        using var changed = await student.PutAsJsonAsync("/api/auth/password", new { currentPassword = "temporary-pass-123", newPassword = "a-good-new-password" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        using var allowed = await student.GetAsync("/api/classes", Ct);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var oldPassword = await PostLoginAsync(studentPhone, "temporary-pass-123");
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
    }

    [Fact]
    public async Task Changing_password_signs_out_other_sessions_but_keeps_the_current_one()
    {
        var phone = await fx.CreateTeacherAsync();
        var phoneSession = await fx.LoginAsync(phone);
        var laptopSession = await fx.LoginAsync(phone);
        using var client = fx.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", phoneSession.GetProperty("accessToken").GetString());

        using var change = await client.PutAsJsonAsync("/api/auth/password", new
        {
            currentPassword = DatabaseApiFixture.DefaultPassword,
            newPassword = "brand-new-password-9",
            refreshToken = phoneSession.GetProperty("refreshToken").GetString(),
        }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        using var kept = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = phoneSession.GetProperty("refreshToken").GetString() }, Ct);
        using var other = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = laptopSession.GetProperty("refreshToken").GetString() }, Ct);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact]
    public async Task Register_always_creates_a_student_and_rejects_duplicates()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var phone = DatabaseApiFixture.NewPhone();

        using var created = await teacher.PostAsJsonAsync("/api/auth/register", new { fullName = "Aisha Khan", phoneNumber = phone, temporaryPassword = "temporary-pass-123", role = "Teacher" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await BodyAsync(created);
        Assert.Equal("Student", body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());
        Assert.False(body.TryGetProperty("passwordHash", out _));

        using var duplicate = await teacher.PostAsJsonAsync("/api/auth/register", new { fullName = "Someone Else", phoneNumber = phone, temporaryPassword = "temporary-pass-123" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("PHONE_ALREADY_REGISTERED", (await BodyAsync(duplicate)).GetProperty("code").GetString());

        using var weak = await teacher.PostAsJsonAsync("/api/auth/register", new { fullName = "Weak Pass", phoneNumber = DatabaseApiFixture.NewPhone(), temporaryPassword = "12345678" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
    }

    [Fact]
    public async Task Students_cannot_register_accounts()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var (_, studentPhone) = await fx.CreateActiveStudentAsync(teacher);
        using var student = await fx.ClientForAsync(studentPhone);

        using var response = await student.PostAsJsonAsync("/api/auth/register", new { fullName = "Friend", phoneNumber = DatabaseApiFixture.NewPhone(), temporaryPassword = "temporary-pass-123" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await BodyAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deactivation_blocks_login_refresh_and_existing_access_tokens_immediately()
    {
        using var teacher = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        var (studentId, studentPhone) = await fx.CreateActiveStudentAsync(teacher);
        var studentLogin = await fx.LoginAsync(studentPhone);
        using var student = fx.Client();
        student.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentLogin.GetProperty("accessToken").GetString());

        using var deactivate = await teacher.PutAsJsonAsync($"/api/students/{studentId}/status", new { isActive = false }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        using var withOldAccessToken = await student.GetAsync("/api/classes", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, withOldAccessToken.StatusCode);
        Assert.Equal("ACCOUNT_DEACTIVATED", (await BodyAsync(withOldAccessToken)).GetProperty("code").GetString());

        using var refresh = await student.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = studentLogin.GetProperty("refreshToken").GetString() }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode); // Token was revoked on deactivation.

        using var login = await PostLoginAsync(studentPhone, DatabaseApiFixture.DefaultPassword);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("ACCOUNT_DEACTIVATED", (await BodyAsync(login)).GetProperty("code").GetString());

        using var wrongPassword = await PostLoginAsync(studentPhone, "not-the-password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode); // Status not revealed without the password.
    }

    [Fact]
    public async Task Audit_log_records_logins_without_secrets()
    {
        var phone = await fx.CreateTeacherAsync();
        await fx.LoginAsync(phone);
        (await PostLoginAsync(phone, "wrong-password-x")).Dispose();

        await fx.WithDbAsync(async db =>
        {
            var userId = await db.Users.IgnoreQueryFilters().Where(u => u.PhoneNumber == phone).Select(u => u.Id).SingleAsync(Ct);
            var entries = await db.AuditLogs.Where(a => a.EntityId == userId).ToListAsync(Ct);
            Assert.Contains(entries, e => e.Action == "auth.login");
            Assert.Contains(entries, e => e.Action == "auth.login_failed");
            Assert.Contains(entries, e => e.Action == "teacher.created");
            Assert.All(entries, e => Assert.DoesNotContain("wrong-password", e.Details ?? string.Empty, StringComparison.Ordinal));
            Assert.All(entries, e => Assert.DoesNotContain(DatabaseApiFixture.DefaultPassword, e.Details ?? string.Empty, StringComparison.Ordinal));
        });
    }
}

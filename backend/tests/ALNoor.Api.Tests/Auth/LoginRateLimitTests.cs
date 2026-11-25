using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ALNoor.Api.Tests.Infrastructure;

namespace ALNoor.Api.Tests.Auth;

public sealed class LoginRateLimitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sixth_login_for_the_same_phone_within_a_minute_is_rejected()
    {
        // The limiter runs before the database is touched, so no database is needed.
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, overrides: new Dictionary<string, string?>
        {
            ["Auth:LoginPolicy:LoginRequestsPerMinutePerPhone"] = "5",
        });
        using var client = factory.CreateClient();
        var body = new { phoneNumber = "not-a-phone", password = "x" };

        for (var i = 0; i < 5; i++)
        {
            using var allowed = await client.PostAsJsonAsync("/api/auth/login", body, Ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/api/auth/login", body, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("RATE_LIMITED", (await limited.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Eleventh_login_from_the_same_ip_within_a_minute_is_rejected()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, overrides: new Dictionary<string, string?>
        {
            ["Auth:LoginPolicy:LoginRequestsPerMinutePerIp"] = "10",
        });
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            using var allowed = await client.PostAsJsonAsync("/api/auth/login", new { phoneNumber = $"bad-{i}", password = "x" }, Ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/api/auth/login", new { phoneNumber = "bad-x", password = "x" }, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}

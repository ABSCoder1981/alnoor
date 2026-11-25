using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ALNoor.Api.Tests.Infrastructure;

namespace ALNoor.Api.Tests.Dev;

public sealed class DevLiveKitEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Uri Endpoint = new("/api/dev/livekit-token", UriKind.Relative);

    private static async Task<HttpResponseMessage> PostAsync(string environment, object body)
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, environment);
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync(Endpoint, body, Ct);
    }

    [Fact]
    public async Task Development_returns_url_token_and_unique_identity()
    {
        using var first = await PostAsync("Development", new { name = "Aisha", room = "poc", role = "student" });
        using var second = await PostAsync("Development", new { name = "Aisha", room = "poc", role = "student" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var b = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(ApiFactory.LiveKitUrl, a.GetProperty("url").GetString()?.TrimEnd('/'));
        Assert.Equal(3, a.GetProperty("token").GetString()!.Split('.').Length);
        Assert.StartsWith("dev-", a.GetProperty("identity").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(a.GetProperty("identity").GetString(), b.GetProperty("identity").GetString());
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task Endpoint_does_not_exist_outside_development(string environment)
    {
        using var response = await PostAsync(environment, new { name = "Aisha", room = "poc" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public static TheoryData<string, object> InvalidRequests => new()
    {
        { "empty name", new { name = "", room = "poc" } },
        { "blank name", new { name = "   ", room = "poc" } },
        { "long name", new { name = new string('a', 51), room = "poc" } },
        { "room with personal data", new { name = "Aisha", room = "Aisha +919800000000" } },
        { "room upper case", new { name = "Aisha", room = "POC" } },
        { "missing room", new { name = "Aisha" } },
        { "unknown role", new { name = "Aisha", room = "poc", role = "admin" } },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_requests_return_400_problem_details(string because, object body)
    {
        using var response = await PostAsync("Development", body);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{because}: got {(int)response.StatusCode}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());
    }
}

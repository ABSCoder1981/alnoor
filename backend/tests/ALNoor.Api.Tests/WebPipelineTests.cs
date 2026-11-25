using System.Net;
using ALNoor.Api.Tests.Infrastructure;

namespace ALNoor.Api.Tests;

public sealed class WebPipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpRequestMessage LiveRequestFrom(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        request.Headers.Add("Origin", origin);
        return request;
    }

    [Fact]
    public async Task Cors_allows_configured_web_origin()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();
        using var request = LiveRequestFrom(ApiFactory.AllowedOrigin);

        using var response = await client.SendAsync(request, Ct);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal(ApiFactory.AllowedOrigin, Assert.Single(values));
    }

    [Fact]
    public async Task Cors_rejects_unknown_origin()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();
        using var request = LiveRequestFrom("https://evil.example");

        using var response = await client.SendAsync(request, Ct);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Responses_include_security_headers()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("default-src 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}

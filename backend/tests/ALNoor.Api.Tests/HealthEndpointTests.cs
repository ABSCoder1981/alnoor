using System.Net;
using ALNoor.Api.Tests.Infrastructure;

namespace ALNoor.Api.Tests;

public sealed class HealthEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Live_returns_healthy_without_a_database()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Health_returns_503_when_database_is_unreachable()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal("Unhealthy", body);
    }

    [Fact]
    public async Task Health_returns_healthy_when_database_is_reachable()
    {
        postgres.SkipIfUnavailable();
        await using var factory = new ApiFactory(postgres.ConnectionString!);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }
}

using Testcontainers.PostgreSql;

namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>
/// Starts a throwaway PostgreSQL container (same major version as docker-compose.yml).
/// If Docker is not available, <see cref="StartError"/> is set and dependent tests are skipped
/// with an explicit reason rather than silently passing.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string? ConnectionString { get; private set; }

    public string? StartError { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:18.7-alpine").Build();
            await _container.StartAsync(TestContext.Current.CancellationToken);
            ConnectionString = _container.GetConnectionString();
        }
#pragma warning disable CA1031 // Any start failure means "Docker unavailable"; it is reported, not swallowed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StartError = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Skips locally when Docker is missing, but fails in CI (where Docker must exist),
    /// so a green CI run always means the database tests really executed.
    /// </summary>
    public void SkipIfUnavailable()
    {
        if (ConnectionString is not null)
        {
            return;
        }

        var message = $"Docker/PostgreSQL not available: {StartError}";
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(message);
        }

        Assert.Skip(message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

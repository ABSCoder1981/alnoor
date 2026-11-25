using Testcontainers.PostgreSql;

namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>Starts a throwaway PostgreSQL container (same major version as docker-compose.yml).</summary>
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

    public void SkipIfUnavailable() =>
        DockerRequirement.SkipOrFailIfUnavailable(ConnectionString is not null, "PostgreSQL", StartError);

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

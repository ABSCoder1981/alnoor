using Npgsql;
using Testcontainers.PostgreSql;

namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>
/// Provides an empty PostgreSQL database for one test class.
/// - If ALNOOR_TEST_POSTGRES is set (a connection string for a role with CREATEDB, e.g. a native local install),
///   a uniquely named database is created there and dropped afterwards.
/// - Otherwise a throwaway container is started (same major version as docker-compose.yml); this is what CI uses.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ServerVariable = "ALNOOR_TEST_POSTGRES";

    private PostgreSqlContainer? _container;
    private string? _serverConnectionString;
    private string? _databaseName;

    public string? ConnectionString { get; private set; }

    public string? StartError { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            var server = Environment.GetEnvironmentVariable(ServerVariable);
            if (!string.IsNullOrWhiteSpace(server))
            {
                _serverConnectionString = server;
                _databaseName = $"alnoor_test_{Guid.NewGuid():N}";
                await using var connection = new NpgsqlConnection(server);
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
                await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
                ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = _databaseName }.ConnectionString;
                return;
            }

            _container = new PostgreSqlBuilder("postgres:18.7-alpine").Build();
            await _container.StartAsync(TestContext.Current.CancellationToken);
            ConnectionString = _container.GetConnectionString();
        }
#pragma warning disable CA1031 // Any start failure means "no database available"; it is reported, not swallowed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StartError = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public void SkipIfUnavailable() =>
        DockerRequirement.SkipOrFailIfUnavailable(ConnectionString is not null, $"PostgreSQL (Docker or {ServerVariable})", StartError);

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        if (_serverConnectionString is not null && _databaseName is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(_serverConnectionString);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

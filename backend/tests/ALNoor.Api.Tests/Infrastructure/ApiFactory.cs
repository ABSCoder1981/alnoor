using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>Hosts the API in memory with a given connection string and test configuration.</summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "https://app.test.example";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin);
    }

    /// <summary>A Npgsql connection string to a port where nothing listens, with a short timeout.</summary>
    public static string UnreachableDatabase =>
        "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";
}

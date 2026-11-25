using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ALNoor.Api.Features.Admin;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>
/// One migrated database plus one in-memory API per test class. Tests use unique phone numbers, so they can share it.
/// </summary>
public sealed class DatabaseApiFixture : IAsyncLifetime
{
    public const string DefaultPassword = "correct-horse-battery";
    private static int _phoneCounter = Random.Shared.Next(1_000_000, 9_000_000);

    private readonly PostgresFixture _postgres = new();

    public ApiFactory? Factory { get; private set; }

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        if (_postgres.ConnectionString is null)
        {
            return;
        }

        Factory = new ApiFactory(_postgres.ConnectionString);
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AlNoorDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public ApiFactory Api
    {
        get
        {
            _postgres.SkipIfUnavailable();
            return Factory!;
        }
    }

    public string ConnectionString => _postgres.ConnectionString!;

    /// <summary>A valid, unique Indian mobile number in E.164.</summary>
    public static string NewPhone() => $"+9198{Interlocked.Increment(ref _phoneCounter):D8}";

    /// <summary>Creates a teacher through the real developer command (FR-AUTH-11).</summary>
    public async Task<string> CreateTeacherAsync(string? phone = null, string name = "Teacher Test")
    {
        phone ??= NewPhone();
        using var output = new StringWriter();
        var exit = await AdminCommands.RunAsync(Api.Services, ["seed-teacher", "--name", name, "--phone", phone], output, () => DefaultPassword);
        Assert.True(exit == 0, output.ToString());
        return phone;
    }

    public HttpClient Client() => Api.CreateClient();

    public async Task<JsonElement> LoginAsync(string phone, string password = DefaultPassword, string clientType = "Android")
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { phoneNumber = phone, password, clientType }, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"Login failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    public async Task<HttpClient> ClientForAsync(string phone, string password = DefaultPassword)
    {
        var login = await LoginAsync(phone, password);
        var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Teacher creates a student (temporary password); the student then changes it so they can use the API.</summary>
    public async Task<(Guid Id, string Phone)> CreateActiveStudentAsync(HttpClient teacher, string name = "Student Test")
    {
        var (id, phone) = await CreateStudentAsync(teacher, name, "temporary-pass-123");
        using var student = await ClientForAsync(phone, "temporary-pass-123");
        using var change = await student.PutAsJsonAsync("/api/auth/password", new { currentPassword = "temporary-pass-123", newPassword = DefaultPassword }, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, change.StatusCode);
        return (id, phone);
    }

    public static async Task<(Guid Id, string Phone)> CreateStudentAsync(HttpClient teacher, string name = "Student Test", string temporaryPassword = "temporary-pass-123")
    {
        ArgumentNullException.ThrowIfNull(teacher);
        var phone = NewPhone();
        using var response = await teacher.PostAsJsonAsync("/api/auth/register", new { fullName = name, phoneNumber = phone, temporaryPassword }, TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (body.GetProperty("id").GetGuid(), phone);
    }

    /// <summary>Direct database access for setup/assertions (ignores tenant filters).</summary>
    public async Task WithDbAsync(Func<AlNoorDbContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>();
        await action(scope.ServiceProvider.GetRequiredService<AlNoorDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}

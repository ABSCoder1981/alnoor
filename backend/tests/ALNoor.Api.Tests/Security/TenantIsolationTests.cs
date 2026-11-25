using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ALNoor.Api.Features.Auth;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ALNoor.Api.Tests.Security;

/// <summary>Data from another tenant must be invisible, even when ids are known (PRD 15.4, CLAUDE.md tenant isolation).</summary>
public sealed class TenantIsolationTests(DatabaseApiFixture fx) : IClassFixture<DatabaseApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Users_and_classes_of_another_tenant_are_not_visible()
    {
        // Tenant 1 (default): a teacher with a class and a student.
        using var teacher1 = await fx.ClientForAsync(await fx.CreateTeacherAsync());
        using var createClass = await teacher1.PostAsJsonAsync("/api/classes", new { title = "Tenant One Class", timeZoneId = "Asia/Kolkata", defaultDurationMinutes = 45, schedule = Array.Empty<object>() }, Ct);
        var class1 = (await createClass.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var (student1, _) = await DatabaseApiFixture.CreateStudentAsync(teacher1);

        // Tenant 2: created directly in the database (no tenant UI in V1).
        var tenant2 = Guid.CreateVersion7();
        var teacher2 = new User
        {
            TenantId = tenant2, FullName = "Other Tenant Teacher", PhoneNumber = DatabaseApiFixture.NewPhone(), PasswordHash = "x",
            Role = UserRole.Teacher, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var class2 = new Class
        {
            TenantId = tenant2, TeacherId = teacher2.Id, Title = "Tenant Two Class", TimeZoneId = "Asia/Kolkata",
            DefaultDurationMinutes = 45, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        await fx.WithDbAsync(async db =>
        {
            db.Tenants.Add(new Tenant { Id = tenant2, Name = "Other School", CreatedAt = DateTimeOffset.UtcNow });
            db.Users.Add(teacher2);
            db.Classes.Add(class2);
            await db.SaveChangesAsync(Ct);
        });

        // Tenant-2 teacher signs in with a token issued for them.
        using var client2 = fx.Client();
        var token = fx.Api.Services.GetRequiredService<AccessTokenService>().Create(teacher2).Token;
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Tenant 2 cannot see tenant 1's data...
        using var foreignClass = await client2.GetAsync($"/api/classes/{class1}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignClass.StatusCode);
        using var foreignStudent = await client2.GetAsync($"/api/students/{student1}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignStudent.StatusCode);
        using var enrollForeign = await client2.PostAsJsonAsync($"/api/classes/{class2.Id}/students", new { studentIds = new[] { student1 } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, enrollForeign.StatusCode);

        // ...and tenant 1 cannot see tenant 2's.
        using var reverse = await teacher1.GetAsync($"/api/classes/{class2.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, reverse.StatusCode);

        // Phone uniqueness is per tenant (PRD 15.3): a number used in tenant 2 can still be registered in tenant 1.
        using var samePhone = await teacher1.PostAsJsonAsync("/api/auth/register", new { fullName = "Same Phone", phoneNumber = teacher2.PhoneNumber, temporaryPassword = "temporary-pass-123" }, Ct);
        Assert.Equal(HttpStatusCode.Created, samePhone.StatusCode);
    }
}

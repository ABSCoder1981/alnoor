using System.Security.Claims;
using ALNoor.Api.Shared.Data;
using ALNoor.Api.Shared.Tenancy;
using ALNoor.Api.Shared.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ALNoor.Api.Shared.Security;

/// <summary>The signed-in user, loaded from the database for this request (PRD 16.1: roles are never trusted from the client).</summary>
public sealed class CurrentUser
{
    private User? _user;

    public User User => _user ?? throw new InvalidOperationException("CurrentUser is only available on endpoints that require a signed-in user.");

    public Guid Id => User.Id;

    internal void Set(User user) => _user = user;
}

/// <summary>Marks an endpoint as usable while a forced password change is pending (me, change password, logout).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowPendingPasswordChangeAttribute : Attribute;

/// <summary>
/// Runs after JWT validation on every protected endpoint: reloads the user, rejects deactivated users and pending
/// password changes, checks the role from the database, and sets the tenant for query filters.
/// </summary>
internal sealed class AppUserFilter(UserRole[] allowedRoles) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var sub = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(sub, out var userId))
        {
            return Problems.Create(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in again.");
        }

        var db = http.RequestServices.GetRequiredService<AlNoorDbContext>();
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, http.RequestAborted);
        if (user is null)
        {
            return Problems.Create(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in again.");
        }

        if (!user.IsActive)
        {
            return Problems.Forbidden("ACCOUNT_DEACTIVATED", "This account has been deactivated. Contact your teacher.");
        }

        var allowsPendingChange = http.GetEndpoint()?.Metadata.GetMetadata<AllowPendingPasswordChangeAttribute>() is not null;
        if (user.MustChangePassword && !allowsPendingChange)
        {
            return Problems.Forbidden("PASSWORD_CHANGE_REQUIRED", "Change your temporary password first.");
        }

        if (allowedRoles.Length > 0 && !allowedRoles.Contains(user.Role))
        {
            return Problems.Forbidden();
        }

        http.RequestServices.GetRequiredService<TenantContext>().Set(user.TenantId);
        http.RequestServices.GetRequiredService<CurrentUser>().Set(user);
        return await next(context);
    }
}

internal static class AppUserEndpointExtensions
{
    /// <summary>Requires a valid access token and an active user with one of the given roles (any role if none given).</summary>
    public static TBuilder RequireAppUser<TBuilder>(this TBuilder builder, params UserRole[] roles)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization().AddEndpointFilter(new AppUserFilter(roles));

    public static TBuilder AllowPendingPasswordChange<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowPendingPasswordChangeAttribute());
}

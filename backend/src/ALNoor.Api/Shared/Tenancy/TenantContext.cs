using ALNoor.Api.Shared.Data;

namespace ALNoor.Api.Shared.Tenancy;

/// <summary>
/// The tenant for the current request. Set from the signed-in user's database record, never from client input
/// (PRD 15.4). Unauthenticated requests (login, refresh) use the single V1 tenant.
/// </summary>
public sealed class TenantContext
{
    public Guid TenantId { get; private set; } = Tenant.DefaultId;

    public void Set(Guid tenantId) => TenantId = tenantId;
}

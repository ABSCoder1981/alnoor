using Microsoft.EntityFrameworkCore;

namespace ALNoor.Api.Shared.Data;

/// <summary>
/// Application database context. Entities and the first migration arrive in Phase 3
/// (PRD section 15); Phase 1 only wires the connection and health check.
/// </summary>
public sealed class AlNoorDbContext(DbContextOptions<AlNoorDbContext> options) : DbContext(options)
{
}

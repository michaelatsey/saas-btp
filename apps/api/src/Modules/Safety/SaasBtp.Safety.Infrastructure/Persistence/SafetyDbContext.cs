using Microsoft.EntityFrameworkCore;
using SaasBtp.Safety.Domain.Constats;

namespace SaasBtp.Safety.Infrastructure.Persistence;

/// <summary>
/// The Safety bounded context's EF Core <see cref="DbContext"/> — the first product persistence.
/// </summary>
/// <remarks>
/// Runtime ORM only: it maps a schema DbUp owns and creates (ADR-ARCH-006). It never issues DDL —
/// no <c>Migrate()</c>, no <c>EnsureCreated()</c>. The mapping is verified against the DbUp-migrated
/// schema by the anti-drift integration test. Exposes a public options constructor so that test can
/// build it directly (no DI, no repository in this story).
/// </remarks>
/// <param name="options">The context options (Npgsql provider + connection).</param>
public sealed class SafetyDbContext(DbContextOptions<SafetyDbContext> options) : DbContext(options)
{
    /// <summary>The Constat aggregate set.</summary>
    public DbSet<Constat> Constats => Set<Constat>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // The Safety context lives in the 'safety' schema (sql.md: one schema per bounded context).
        modelBuilder.HasDefaultSchema("safety");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SafetyDbContext).Assembly);
    }
}

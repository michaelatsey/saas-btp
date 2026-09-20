using Microsoft.EntityFrameworkCore;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Infrastructure.Persistence;

/// <summary>
/// The Access bounded context's EF Core <see cref="DbContext"/>.
/// </summary>
/// <remarks>
/// Runtime ORM only (ADR-ARCH-006 / ADR-ARCH-015): it maps the schema DbUp owns
/// (<c>0001_access_founding_model.sql</c>) and never issues DDL — no <c>Migrate()</c>, no
/// <c>EnsureCreated()</c>. The mapping is verified against the DbUp-migrated schema by the
/// anti-drift integration test. Public options constructor so that test can build it directly.
/// <para>
/// STALE PENDING RE-DERIVATION (ADR-ARCH-016). The Access Domain now has two aggregate roots that
/// CONTAIN their relations, so the mapping this context applies no longer matches the model: the
/// ownership, membership and access edges must be mapped as collections owned by their root, not as
/// independent sets, and the roots' new read surface must be mapped or ignored explicitly. The
/// relation sets and their configurations are disabled below rather than reshaped — persistence is
/// re-derived in its own pass, against the settled Domain and without touching the DbUp-owned DDL.
/// </para>
/// </remarks>
/// <param name="options">The context options (Npgsql provider + connection).</param>
public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    /// <summary>The Organization aggregate set.</summary>
    public DbSet<Organization> Organizations => Set<Organization>();

    /// <summary>The Workspace aggregate set.</summary>
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    // DISABLED (ADR-ARCH-016): the ownership, membership and access edges are no longer aggregate
    // roots, so they no longer have root sets of their own. They are components of Organization /
    // Workspace and will be reached through them once the mapping is re-derived.
#if ACCESS_PERSISTENCE_PENDING_REDERIVATION
    /// <summary>The Ownership relation set.</summary>
    public DbSet<Ownership> Ownerships => Set<Ownership>();

    /// <summary>The Organization Membership relation set.</summary>
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();

    /// <summary>The Workspace Access relation set.</summary>
    public DbSet<WorkspaceAccess> WorkspaceAccesses => Set<WorkspaceAccess>();
#endif

    /// <summary>The identity-projection set (infrastructure, not a business fact).</summary>
    internal DbSet<ProfileRecord> Profiles => Set<ProfileRecord>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // The Access context lives in the 'access' schema (sql.md: one schema per bounded context).
        modelBuilder.HasDefaultSchema("access");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccessDbContext).Assembly);
    }
}

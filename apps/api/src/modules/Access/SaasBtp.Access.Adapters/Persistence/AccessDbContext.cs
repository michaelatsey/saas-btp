using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SaasBtp.Access.Adapters.Persistence.Memberships;
using SaasBtp.Access.Adapters.Persistence.Organizations;
using SaasBtp.Access.Adapters.Persistence.Ownerships;
using SaasBtp.Access.Adapters.Persistence.Profiles;
using SaasBtp.Access.Adapters.Persistence.WorkspaceAccesses;
using SaasBtp.Access.Adapters.Persistence.Workspaces;
using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters.Persistence;

/// <summary>
/// The Access module's database context, on the <c>access</c> schema only (ADR-ARCH-033 §3,
/// ADR-ARCH-034 §3). It never creates or alters the schema: the migration runner owns it, and
/// <c>MappingMatchesMigratedSchema</c> compares this mapping with what the runner migrated.
/// </summary>
/// <remarks>
/// <para>
/// Each aggregate's mapping lives in its own folder, with its repository and its converters, and is
/// applied here by one explicit <c>ApplyConfiguration</c> per entity: no assembly scanning
/// (ADR-ARCH-034 decisions 2 and 7). What every table shares is <see cref="AccessTable"/>. The
/// conversions stay in <see cref="ConfigureConventions"/>, since each applies to the whole model.
/// </para>
/// <para>
/// Every value is set by the API before the write: no key is generated, and the audit values come
/// from the domain (<c>sql.md</c>, Audit / lifecycle). As ADR-ARCH-033 §5 requires, the model
/// declares the organization's relation to its current ownership and maps the ownership's
/// <c>organization_id</c> and the access's <c>(workspace_id, organization_id)</c> as plain columns,
/// so that EF Core sees no cycle; the founding saves each row that must precede another in its own
/// <c>SaveChanges</c>.
/// </para>
/// </remarks>
internal sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The Actor and audit-instant conventions of MicroKit.Persistence. Its identifier convention is
        // not used: it applies to EntityId<T> classes, and the Access identifiers are record structs
        // converted below.
        configurationBuilder.AddActorConvention().AddAuditInstantConvention();

        configurationBuilder.Properties<ProfileId>().HaveConversion<ProfileIdConverter>();
        configurationBuilder.Properties<UserId>().HaveConversion<UserIdConverter>();
        configurationBuilder.Properties<OwnershipId>().HaveConversion<OwnershipIdConverter>();
        configurationBuilder.Properties<OrganizationId>().HaveConversion<OrganizationIdConverter>();
        configurationBuilder.Properties<MembershipId>().HaveConversion<MembershipIdConverter>();
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<WorkspaceIdConverter>();
        configurationBuilder.Properties<WorkspaceAccessId>().HaveConversion<WorkspaceAccessIdConverter>();
        configurationBuilder.Properties<OrganizationName>().HaveConversion<OrganizationNameConverter>().HaveMaxLength(AccessTable.NameLength);
        configurationBuilder.Properties<WorkspaceName>().HaveConversion<WorkspaceNameConverter>().HaveMaxLength(AccessTable.NameLength);
        configurationBuilder.Properties<OrganizationStatus>().HaveConversion<OrganizationStatusConverter>().HaveMaxLength(AccessTable.StatusLength);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AccessTable.Schema);

        modelBuilder.ApplyConfiguration(new ProfileConfiguration());
        modelBuilder.ApplyConfiguration(new OwnershipConfiguration());
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new MembershipConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceAccessConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceConfiguration());
    }
}

using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
/// Names are those of <c>0001__access__create_tables.sql</c>. Every value is set by the API before
/// the write: no key is generated, and the audit values come from the domain (<c>sql.md</c>, Audit /
/// lifecycle). As ADR-ARCH-033 §5 requires, the model declares the organization's relation to its
/// current ownership and maps the ownership's <c>organization_id</c> and the access's
/// <c>(workspace_id, organization_id)</c> as plain columns, so that EF Core sees no cycle; the
/// founding saves each row that must precede another in its own <c>SaveChanges</c>.
/// </remarks>
internal sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    private const string Schema = "access";

    /// <summary>The most Unicode scalar values a name holds (BP001-R7), and so a <c>varchar</c> counts.</summary>
    private const int NameLength = 200;

    /// <summary>The longest status token, <c>declared</c> or <c>verified</c>.</summary>
    private const int StatusLength = 8;

    /// <summary>The filter of the unique keys of current relations (contract.md §5).</summary>
    private const string Current = "ended_at is null";

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
        configurationBuilder.Properties<OrganizationName>().HaveConversion<OrganizationNameConverter>().HaveMaxLength(NameLength);
        configurationBuilder.Properties<WorkspaceName>().HaveConversion<WorkspaceNameConverter>().HaveMaxLength(NameLength);
        configurationBuilder.Properties<OrganizationStatus>().HaveConversion<OrganizationStatusConverter>().HaveMaxLength(StatusLength);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Profile>(profile =>
        {
            Root(profile, "profiles", "pk_profiles");
            profile.Property(p => p.UserId).HasColumnName("user_id");
            profile.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ux_profiles__user_id");
        });

        modelBuilder.Entity<Ownership>(ownership =>
        {
            Root(ownership, "ownerships", "pk_ownerships");
            ownership.Property(o => o.ProfileId).HasColumnName("profile_id");
            ownership.Property(o => o.OrganizationId).HasColumnName("organization_id");
            ownership.HasAlternateKey(o => new { o.OrganizationId, o.Id }).HasName("ux_ownerships__organization_id_id");
            ownership.HasOne<Profile>().WithMany().HasForeignKey(o => o.ProfileId)
                .HasConstraintName("fk_ownerships__profiles");
        });

        modelBuilder.Entity<Organization>(organization =>
        {
            Root(organization, "organizations", "pk_organizations");
            organization.Property(o => o.Name).HasColumnName("name");
            organization.Property(o => o.Status).HasColumnName("status");
            organization.Property(o => o.CurrentOwnershipId).HasColumnName("current_ownership_id");
            // The one current owner (ADR-ARCH-033 §6): the organization references its ownership
            // through the pair (organization_id, id).
            organization.HasOne<Ownership>().WithMany()
                .HasForeignKey(o => new { o.Id, o.CurrentOwnershipId })
                .HasPrincipalKey(o => new { o.OrganizationId, o.Id })
                .HasConstraintName("fk_organizations__ownerships");
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            Root(membership, "memberships", "pk_memberships");
            membership.Property(m => m.ProfileId).HasColumnName("profile_id");
            membership.Property(m => m.OrganizationId).HasColumnName("organization_id");
            membership.Property(m => m.EndedAt).HasColumnName("ended_at");
            membership.HasIndex(m => new { m.ProfileId, m.OrganizationId }).IsUnique().HasFilter(Current)
                .HasDatabaseName("ux_memberships__profile_id_organization_id");
            membership.HasOne<Profile>().WithMany().HasForeignKey(m => m.ProfileId)
                .HasConstraintName("fk_memberships__profiles");
            membership.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId)
                .HasConstraintName("fk_memberships__organizations");
        });

        modelBuilder.Entity<WorkspaceAccess>(access =>
        {
            Root(access, "workspace_accesses", "pk_workspace_accesses");
            access.Property(a => a.ProfileId).HasColumnName("profile_id");
            access.Property(a => a.WorkspaceId).HasColumnName("workspace_id");
            access.Property(a => a.OrganizationId).HasColumnName("organization_id");
            access.Property(a => a.EndedAt).HasColumnName("ended_at");
            access.HasIndex(a => new { a.ProfileId, a.WorkspaceId }).IsUnique().HasFilter(Current)
                .HasDatabaseName("ux_workspace_accesses__profile_id_workspace_id");
            access.HasOne<Profile>().WithMany().HasForeignKey(a => a.ProfileId)
                .HasConstraintName("fk_workspace_accesses__profiles");
        });

        modelBuilder.Entity<Workspace>(workspace =>
        {
            Root(workspace, "workspaces", "pk_workspaces");
            workspace.Property(w => w.OrganizationId).HasColumnName("organization_id");
            workspace.Property(w => w.Name).HasColumnName("name");
            workspace.HasIndex(w => new { w.OrganizationId, w.Name }).IsUnique()
                .HasDatabaseName("ux_workspaces__organization_id_name");
            workspace.HasOne<Organization>().WithMany().HasForeignKey(w => w.OrganizationId)
                .HasConstraintName("fk_workspaces__organizations");
        });
    }

    /// <summary>
    /// What every Access table maps for its aggregate root: the table, the key, set by the API and
    /// never generated, the four audit columns and the aggregate version (<c>sql.md</c>).
    /// </summary>
    private static void Root<TRoot>(EntityTypeBuilder<TRoot> root, string table, string primaryKey)
        where TRoot : class
    {
        root.ToTable(table, Schema);
        root.HasKey("Id").HasName(primaryKey);
        root.Property("Id").HasColumnName("id").ValueGeneratedNever();
        root.Property("CreatedAt").HasColumnName("created_at");
        root.Property("CreatedBy").HasColumnName("created_by");
        root.Property("UpdatedAt").HasColumnName("updated_at");
        root.Property("UpdatedBy").HasColumnName("updated_by");
        root.Property<long>(AggregateVersion.PropertyName).HasColumnName("aggregate_version");
    }
}

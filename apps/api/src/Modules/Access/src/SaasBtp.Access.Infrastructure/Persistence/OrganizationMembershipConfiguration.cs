// ============================================================================================
// DISABLED — this maps a type that is no longer an aggregate root (ADR-ARCH-016).
//
// The relation is now a component of Organization / Workspace, so its mapping belongs to that
// root's configuration as an owned collection, not to a configuration of its own. The file is
// kept VERBATIM and inert: it holds the column names, constraint names and index names that the
// DbUp-owned DDL already carries, and those are the facts the re-derived mapping must reproduce
// exactly. Reshaping it in the same pass that changed the model would have made a persistence
// decision under cover of a compile fix. Re-enable by folding these mappings into the root.
// ============================================================================================
#if ACCESS_PERSISTENCE_PENDING_REDERIVATION
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Access.Domain.OrganizationMemberships;
using SaasBtp.Access.Domain.Organizations;

namespace SaasBtp.Access.Infrastructure.Persistence;

/// <summary>
/// Maps <see cref="OrganizationMembership"/> onto <c>access.organization_memberships</c>
/// (DbUp-owned).
/// </summary>
/// <remarks>
/// No revocation column: belonging has no lifecycle in the current model, and adding
/// <c>is_active</c> speculatively would let "not yet accepted" and "revoked" share one column — the
/// conflation the authorization model exists to prevent.
/// </remarks>
internal sealed class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> builder)
    {
        builder.ToTable("organization_memberships");

        builder.HasKey(membership => membership.Id).HasName("pk_organization_memberships");

        builder.Property(membership => membership.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OrganizationMembershipId(value));

        builder.Property<OrganizationId>("_organizationId")
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property<Guid>("_memberId").HasColumnName("user_id").IsRequired();

        builder.Property<DateTime>("_createdAtUtc")
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey("_organizationId")
            .HasConstraintName("fk_organization_memberships__organizations")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ProfileRecord>()
            .WithMany()
            .HasForeignKey("_memberId")
            .HasConstraintName("fk_organization_memberships__profiles")
            .OnDelete(DeleteBehavior.Restrict);

        // Belonging is binary: one fact per (person, organization). Profile-first, mirroring the DDL.
        builder.HasIndex("_memberId", "_organizationId")
            .IsUnique()
            .HasDatabaseName("ux_organization_memberships__user_id_organization_id");
    }
}
#endif

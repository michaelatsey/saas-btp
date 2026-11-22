using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;

namespace SaasBtp.Access.Adapters.Persistence.Organizations;

/// <summary>The mapping of an organization to <c>access.organizations</c>.</summary>
internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        AccessTable.Root(builder, "organizations", "pk_organizations");
        builder.Property(o => o.Name).HasColumnName("name");
        builder.Property(o => o.Status).HasColumnName("status");
        builder.Property(o => o.CurrentOwnershipId).HasColumnName("current_ownership_id");
        // The one current owner (ADR-ARCH-033 §6): the organization references its ownership
        // through the pair (organization_id, id).
        builder.HasOne<Ownership>().WithMany()
            .HasForeignKey(o => new { o.Id, o.CurrentOwnershipId })
            .HasPrincipalKey(o => new { o.OrganizationId, o.Id })
            .HasConstraintName("fk_organizations__ownerships");
    }
}

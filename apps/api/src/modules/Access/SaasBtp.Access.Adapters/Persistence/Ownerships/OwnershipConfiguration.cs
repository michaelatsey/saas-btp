using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Adapters.Persistence.Ownerships;

/// <summary>
/// The mapping of an ownership to <c>access.ownerships</c>. Its <c>organization_id</c> is a plain
/// column, so that EF Core sees no cycle with the organization's current ownership (ADR-ARCH-033 §5).
/// </summary>
internal sealed class OwnershipConfiguration : IEntityTypeConfiguration<Ownership>
{
    public void Configure(EntityTypeBuilder<Ownership> builder)
    {
        AccessTable.Root(builder, "ownerships", "pk_ownerships");
        builder.Property(o => o.ProfileId).HasColumnName("profile_id");
        builder.Property(o => o.OrganizationId).HasColumnName("organization_id");
        builder.HasAlternateKey(o => new { o.OrganizationId, o.Id }).HasName("ux_ownerships__organization_id_id");
        builder.HasOne<Profile>().WithMany().HasForeignKey(o => o.ProfileId)
            .HasConstraintName("fk_ownerships__profiles");
    }
}

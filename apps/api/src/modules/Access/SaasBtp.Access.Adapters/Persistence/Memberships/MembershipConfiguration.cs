using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Adapters.Persistence.Memberships;

/// <summary>The mapping of a membership to <c>access.memberships</c>.</summary>
internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        AccessTable.Root(builder, "memberships", "pk_memberships");
        builder.Property(m => m.ProfileId).HasColumnName("profile_id");
        builder.Property(m => m.OrganizationId).HasColumnName("organization_id");
        builder.Property(m => m.EndedAt).HasColumnName("ended_at");
        builder.HasIndex(m => new { m.ProfileId, m.OrganizationId }).IsUnique().HasFilter(AccessTable.Current)
            .HasDatabaseName("ux_memberships__profile_id_organization_id");
        builder.HasOne<Profile>().WithMany().HasForeignKey(m => m.ProfileId)
            .HasConstraintName("fk_memberships__profiles");
        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId)
            .HasConstraintName("fk_memberships__organizations");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Adapters.Persistence.Profiles;

/// <summary>The mapping of a profile to <c>access.profiles</c>.</summary>
internal sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        AccessTable.Root(builder, "profiles", "pk_profiles");
        builder.Property(p => p.UserId).HasColumnName("user_id");
        builder.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ux_profiles__user_id");
    }
}

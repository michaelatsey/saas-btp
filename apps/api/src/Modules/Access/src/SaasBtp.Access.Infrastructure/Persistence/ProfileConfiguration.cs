using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SaasBtp.Access.Infrastructure.Persistence;

/// <summary>
/// Maps the identity projection onto <c>access.profiles</c> (DbUp-owned).
/// </summary>
/// <remarks>
/// There is NO foreign key to <c>auth.users</c>: application DDL never crosses the auth boundary,
/// which Supabase owns. The link is upheld on the write path — the founding handler projects the
/// identity the auth boundary returned — not by a cross-boundary constraint (ADR-ARCH-005).
/// </remarks>
internal sealed class ProfileConfiguration : IEntityTypeConfiguration<ProfileRecord>
{
    public void Configure(EntityTypeBuilder<ProfileRecord> builder)
    {
        builder.ToTable("profiles");

        builder.HasKey(profile => profile.UserId).HasName("pk_profiles");

        builder.Property(profile => profile.UserId).HasColumnName("user_id");

        // Stored NORMALIZED (lowercase, trimmed) by the domain's Email value object. The unique
        // index compares RAW strings — Postgres does not fold case — so it only protects data that
        // already honours that contract (ADR-ARCH-011).
        builder.Property(profile => profile.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(profile => profile.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(200)
            .IsRequired();

        // Collected by neither BP-001 nor any current process; nullable in the schema.
        builder.Property(profile => profile.GivenName).HasColumnName("given_name").HasMaxLength(200);
        builder.Property(profile => profile.FamilyName).HasColumnName("family_name").HasMaxLength(200);
        builder.Property(profile => profile.JobFunction).HasColumnName("job_function").HasMaxLength(150);

        builder.Property(profile => profile.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(profile => profile.Email)
            .IsUnique()
            .HasDatabaseName("ux_profiles__email");
    }
}

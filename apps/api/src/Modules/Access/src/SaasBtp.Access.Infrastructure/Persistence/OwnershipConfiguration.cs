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
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;

namespace SaasBtp.Access.Infrastructure.Persistence;

/// <summary>
/// Maps <see cref="Ownership"/> onto <c>access.ownerships</c> (DbUp-owned).
/// </summary>
/// <remarks>
/// The unique index on <c>organization_id</c> is the SINGLE guarantor of "exactly one initial owner"
/// (INV-2/INV-3). It is declared here so EF knows about it, but the database is what enforces it —
/// the domain deliberately does not, and a second guard would be a race, not a safety net.
/// </remarks>
internal sealed class OwnershipConfiguration : IEntityTypeConfiguration<Ownership>
{
    public void Configure(EntityTypeBuilder<Ownership> builder)
    {
        builder.ToTable("ownerships");

        builder.HasKey(ownership => ownership.Id).HasName("pk_ownerships");

        builder.Property(ownership => ownership.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OwnershipId(value));

        builder.Property<OrganizationId>("_organizationId")
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property<Guid>("_ownerId").HasColumnName("user_id").IsRequired();

        builder.Property<DateTime>("_createdAtUtc")
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey("_organizationId")
            .HasConstraintName("fk_ownerships__organizations")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ProfileRecord>()
            .WithMany()
            .HasForeignKey("_ownerId")
            .HasConstraintName("fk_ownerships__profiles")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex("_organizationId")
            .IsUnique()
            .HasDatabaseName("ux_ownerships__organization_id");
    }
}
#endif

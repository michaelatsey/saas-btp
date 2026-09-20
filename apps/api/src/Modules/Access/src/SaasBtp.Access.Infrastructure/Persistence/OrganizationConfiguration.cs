using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Access.Domain.Organizations;

namespace SaasBtp.Access.Infrastructure.Persistence;

/// <summary>
/// Maps <see cref="Organization"/> onto <c>access.organizations</c> (DbUp-owned,
/// 0001_access_founding_model.sql). Must stay in lock-step with that DDL — the anti-drift
/// integration test is the guard (ADR-ARCH-006).
/// </summary>
/// <remarks>
/// The aggregate encapsulates its state in private fields, so every column but the key is mapped by
/// FIELD name. That is the point: persistence adapts to the model, the model does not open itself up
/// for persistence.
/// </remarks>
internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(organization => organization.Id).HasName("pk_organizations");

        builder.Property(organization => organization.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OrganizationId(value));

        builder.Property<string>("_displayName")
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        // Closed set persisted as its snake_case token via an EXPLICIT converter — never an implicit
        // ToString — so a C# rename can never silently drift from the stored token (sql.md §Closed
        // sets). FromToken fails loudly on a token the CHECK should never have allowed.
        builder.Property<OrganizationStatus>("_status")
            .HasColumnName("status")
            .HasMaxLength(32)
            .HasConversion(
                status => status.Token,
                token => OrganizationStatus.FromToken(token))
            .IsRequired();

        // NO database default: the domain stamps it (ADR-ARCH-005). A default would MASK a domain
        // that forgot to stamp instead of failing loudly.
        builder.Property<DateTime>("_createdAtUtc")
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}

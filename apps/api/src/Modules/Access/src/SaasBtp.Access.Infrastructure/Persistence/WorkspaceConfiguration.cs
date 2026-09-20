using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Infrastructure.Persistence;

/// <summary>
/// Maps <see cref="Workspace"/> onto <c>access.workspaces</c> (DbUp-owned).
/// </summary>
/// <remarks>
/// The organization link is declared to EF as a foreign key WITHOUT a navigation property: the two
/// are separate aggregate roots (ADR-ARCH-014), so no object graph may connect them. Declaring it
/// still matters — it is what makes EF order the INSERTs correctly inside the founding transaction
/// (organization before workspace), rather than leaving the order to chance and a FK violation.
/// </remarks>
internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");

        builder.HasKey(workspace => workspace.Id).HasName("pk_workspaces");

        builder.Property(workspace => workspace.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new WorkspaceId(value));

        builder.Property<OrganizationId>("_organizationId")
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property<string>("_displayName")
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property<DateTime>("_createdAtUtc")
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey("_organizationId")
            .HasConstraintName("fk_workspaces__organizations")
            .OnDelete(DeleteBehavior.Restrict);

        // Deliberately NO unique index on (organization_id, display_name): the FORM of workspace
        // resolution is left open by the business model, and the DDL carries no such constraint.
    }
}

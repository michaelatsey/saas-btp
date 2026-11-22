using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters.Persistence.Workspaces;

/// <summary>The mapping of a workspace to <c>access.workspaces</c>.</summary>
internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        AccessTable.Root(builder, "workspaces", "pk_workspaces");
        builder.Property(w => w.OrganizationId).HasColumnName("organization_id");
        builder.Property(w => w.Name).HasColumnName("name");
        builder.HasIndex(w => new { w.OrganizationId, w.Name }).IsUnique()
            .HasDatabaseName("ux_workspaces__organization_id_name");
        builder.HasOne<Organization>().WithMany().HasForeignKey(w => w.OrganizationId)
            .HasConstraintName("fk_workspaces__organizations");
    }
}

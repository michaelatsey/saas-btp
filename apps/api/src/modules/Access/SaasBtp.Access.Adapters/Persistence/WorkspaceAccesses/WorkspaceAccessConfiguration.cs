using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.WorkspaceAccesses;

namespace SaasBtp.Access.Adapters.Persistence.WorkspaceAccesses;

/// <summary>
/// The mapping of a workspace access to <c>access.workspace_accesses</c>. Its
/// <c>(workspace_id, organization_id)</c> are plain columns, so that EF Core sees no cycle
/// (ADR-ARCH-033 §5).
/// </summary>
internal sealed class WorkspaceAccessConfiguration : IEntityTypeConfiguration<WorkspaceAccess>
{
    public void Configure(EntityTypeBuilder<WorkspaceAccess> builder)
    {
        AccessTable.Root(builder, "workspace_accesses", "pk_workspace_accesses");
        builder.Property(a => a.ProfileId).HasColumnName("profile_id");
        builder.Property(a => a.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(a => a.OrganizationId).HasColumnName("organization_id");
        builder.Property(a => a.EndedAt).HasColumnName("ended_at");
        builder.HasIndex(a => new { a.ProfileId, a.WorkspaceId }).IsUnique().HasFilter(AccessTable.Current)
            .HasDatabaseName("ux_workspace_accesses__profile_id_workspace_id");
        builder.HasOne<Profile>().WithMany().HasForeignKey(a => a.ProfileId)
            .HasConstraintName("fk_workspace_accesses__profiles");
    }
}

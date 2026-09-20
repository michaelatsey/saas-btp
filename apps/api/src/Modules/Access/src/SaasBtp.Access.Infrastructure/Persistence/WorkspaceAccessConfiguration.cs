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
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Infrastructure.Persistence;

/// <summary>
/// Maps <see cref="WorkspaceAccess"/> onto <c>access.workspace_access</c> (DbUp-owned) — note the
/// table name is singular in the DDL, deliberately not pluralised by convention.
/// </summary>
/// <remarks>
/// This is the single access edge BP-001 produces (ADR-ARCH-013). The table has RLS ENABLED with NO
/// policy: the moment a non-privileged consumer (PowerSync) reads it, it returns ZERO ROWS silently.
/// The write path here uses the privileged owner connection, which bypasses RLS — the policy lands
/// with the slice that consumes the edge, together with its provider and the C#/SQL agreement test.
/// <para>
/// No revocation column yet: revocability without destroying history is a business need whose FORM is
/// deferred. Nothing here makes the edge irrevocable.
/// </para>
/// </remarks>
internal sealed class WorkspaceAccessConfiguration : IEntityTypeConfiguration<WorkspaceAccess>
{
    public void Configure(EntityTypeBuilder<WorkspaceAccess> builder)
    {
        builder.ToTable("workspace_access");

        builder.HasKey(access => access.Id).HasName("pk_workspace_access");

        builder.Property(access => access.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new WorkspaceAccessId(value));

        builder.Property<WorkspaceId>("_workspaceId")
            .HasColumnName("workspace_id")
            .HasConversion(id => id.Value, value => new WorkspaceId(value))
            .IsRequired();

        builder.Property<Guid>("_authorizedId").HasColumnName("user_id").IsRequired();

        builder.Property<DateTime>("_createdAtUtc")
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey("_workspaceId")
            .HasConstraintName("fk_workspace_access__workspaces")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ProfileRecord>()
            .WithMany()
            .HasForeignKey("_authorizedId")
            .HasConstraintName("fk_workspace_access__profiles")
            .OnDelete(DeleteBehavior.Restrict);

        // "Can act here" is binary: one fact per (person, workspace). Profile-first — the left prefix
        // is exactly the PowerSync parameter-query path ("which workspaces may this user sync?").
        builder.HasIndex("_authorizedId", "_workspaceId")
            .IsUnique()
            .HasDatabaseName("ux_workspace_access__user_id_workspace_id");
    }
}
#endif

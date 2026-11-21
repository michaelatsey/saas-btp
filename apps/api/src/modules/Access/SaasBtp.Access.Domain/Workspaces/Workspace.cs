using MicroKit.Domain.Aggregates;
using MicroKit.Domain.ValueObjects.Common;
using SaasBtp.Access.Domain.Organizations;

namespace SaasBtp.Access.Domain.Workspaces;

/// <summary>
/// An autonomous operational space of an organization, and the tenant of the other modules
/// (docs/domain/access.md, ADR-ARCH-031). It belongs to exactly one organization for its whole
/// life (INV-ACCESS-04). In BP-001 only the founding creates one, its first (INV-ACCESS-03).
/// </summary>
public sealed class Workspace : AuditableAggregateRoot<WorkspaceId>
{
    internal Workspace(
        WorkspaceId id, OrganizationId organizationId, WorkspaceName name, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy)
    {
        Identifier.Require(organizationId.Value, nameof(organizationId));
        ArgumentNullException.ThrowIfNull(name);

        OrganizationId = organizationId;
        Name = name;
    }

    /// <summary>The organization the workspace belongs to.</summary>
    public OrganizationId OrganizationId { get; private init; }

    /// <summary>The workspace name, as declared (BP001-R7).</summary>
    public WorkspaceName Name { get; private init; }
}

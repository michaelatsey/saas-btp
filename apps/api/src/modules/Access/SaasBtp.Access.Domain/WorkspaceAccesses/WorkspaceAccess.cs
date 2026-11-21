using MicroKit.Domain.Aggregates;
using MicroKit.Domain.ValueObjects.Common;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.WorkspaceAccesses;

/// <summary>
/// The relation saying who may act in a workspace: the only authorization relation of the domain
/// (docs/domain/access.md). It carries its workspace's organization (ADR-ARCH-033 §5). It is
/// current until its end is recorded (contract.md §5); no process of BP-001 ends it. In BP-001
/// only the founding creates one, to the first workspace (BP001-R4).
/// </summary>
public sealed class WorkspaceAccess : AuditableAggregateRoot<WorkspaceAccessId>
{
    internal WorkspaceAccess(
        WorkspaceAccessId id,
        ProfileId profileId,
        WorkspaceId workspaceId,
        OrganizationId organizationId,
        DateTimeOffset createdAt,
        Actor createdBy)
        : base(id, createdAt, createdBy)
    {
        Identifier.Require(profileId.Value, nameof(profileId));
        Identifier.Require(workspaceId.Value, nameof(workspaceId));
        Identifier.Require(organizationId.Value, nameof(organizationId));

        ProfileId = profileId;
        WorkspaceId = workspaceId;
        OrganizationId = organizationId;
    }

    /// <summary>The person who may act, through their profile.</summary>
    public ProfileId ProfileId { get; private init; }

    /// <summary>The workspace they may act in.</summary>
    public WorkspaceId WorkspaceId { get; private init; }

    /// <summary>The organization of that workspace.</summary>
    public OrganizationId OrganizationId { get; private init; }

    /// <summary>When the access ended; null while it is current (contract.md §5).</summary>
    public DateTimeOffset? EndedAt { get; private init; }
}

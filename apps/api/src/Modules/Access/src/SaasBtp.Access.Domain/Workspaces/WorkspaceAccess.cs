using MicroKit.Domain.Aggregates;
using SaasBtp.Access.Domain.Identity;

namespace SaasBtp.Access.Domain.Workspaces;

/// <summary>
/// The AUTHORIZATION axis, held INSIDE the <see cref="Workspace"/> aggregate: "this person may act
/// in this workspace's scope, since this instant".
/// </summary>
/// <remarks>
/// A component of the workspace, not a root of its own (ADR-ARCH-016): the invariant "one access per
/// person per workspace" spans the workspace AND its access edges, so the workspace is the boundary
/// that can enforce it.
/// <para>
/// Unlike the ownership and membership facts, this one keeps an IDENTITY of its own. The business
/// requires the edge to be revocable WITHOUT destroying history (Implementation Design §3.5), and a
/// fact that must outlive its own revocation cannot be defined by its current values alone. The FORM
/// of that revocability — soft flag, temporal window, controlled deletion — stays deferred to the
/// slice that consumes the edge; nothing here makes the edge irrevocable, and no revocation state is
/// modelled yet.
/// </para>
/// <para>
/// "In scope" is a participation boundary, never a set of rights: roles, permissions, inheritance
/// and delegation are outside BP-001. The edge is correlated with the founder's creation but derived
/// from nothing — neither ownership nor membership produces it (ADR-ARCH-013).
/// </para>
/// </remarks>
public sealed class WorkspaceAccess : Entity<WorkspaceAccessId>
{
    private WorkspaceAccess(WorkspaceAccessId id, PersonId person, DateTime grantedAtUtc)
        : base(id)
    {
        Person = person;
        GrantedAtUtc = grantedAtUtc;
    }

    // Only for EF: materialisation writes the members directly.
    private WorkspaceAccess()
        : base(default)
    {
    }

    /// <summary>The person authorized to act in the workspace's scope.</summary>
    public PersonId Person { get; private init; }

    /// <summary>The UTC instant the access was granted, as supplied by the caller.</summary>
    public DateTime GrantedAtUtc { get; private init; }

    /// <summary>
    /// Grants the edge, with an identity of its own. Internal on purpose: <see cref="Workspace"/> is
    /// the only thing entitled to create one, because it is the boundary that guarantees a person
    /// holds at most one.
    /// </summary>
    /// <param name="person">The authorized person.</param>
    /// <param name="grantedAtUtc">The UTC instant supplied by the caller.</param>
    /// <returns>The access edge.</returns>
    internal static WorkspaceAccess Grant(PersonId person, DateTime grantedAtUtc) =>
        new(WorkspaceAccessId.New(), person, grantedAtUtc);
}

using MicroKit.Domain.ValueObjects;
using SaasBtp.Access.Domain.Identity;

namespace SaasBtp.Access.Domain.Organizations;

/// <summary>
/// The BELONGING axis, held INSIDE the <see cref="Organization"/> aggregate: "this person is part of
/// this organization's human collective, since this instant".
/// </summary>
/// <remarks>
/// A component of the organization, not a root of its own (ADR-ARCH-016): the invariant "belonging
/// is binary — one membership per person" spans the organization AND its memberships, so the
/// organization is the boundary that can enforce it. Left outside, the model could only delegate
/// that rule to a database unique index.
/// <para>
/// No identity of its own: no process today produces a departure, an exclusion or an archival
/// (Implementation Design §3.5 leaves membership's revocability undecided), so the fact is defined
/// entirely by its values. Should a later process need to revoke a membership without destroying
/// history, this is where the identity would be added — and that decision is not taken here.
/// </para>
/// <para>
/// Membership is NOT authorization. <c>member_of → access</c> stays a forbidden derivation
/// (ADR-ARCH-013): a member with no access edge can act on nothing operational, and that is
/// coherent, not a defect.
/// </para>
/// </remarks>
public sealed record OrganizationMembership : IValueObject
{
    private OrganizationMembership(PersonId member, DateTime joinedAtUtc)
    {
        Member = member;
        JoinedAtUtc = joinedAtUtc;
    }

    /// <summary>The person who is part of the collective.</summary>
    public PersonId Member { get; }

    /// <summary>The UTC instant the person joined, as supplied by the caller.</summary>
    public DateTime JoinedAtUtc { get; }

    /// <summary>
    /// Establishes the fact. Internal on purpose: <see cref="Organization"/> is the only thing
    /// entitled to create one, because it is the boundary that guarantees uniqueness.
    /// </summary>
    /// <param name="member">The joining person.</param>
    /// <param name="joinedAtUtc">The UTC instant supplied by the caller.</param>
    /// <returns>The membership fact.</returns>
    internal static OrganizationMembership Join(PersonId member, DateTime joinedAtUtc) =>
        new(member, joinedAtUtc);
}

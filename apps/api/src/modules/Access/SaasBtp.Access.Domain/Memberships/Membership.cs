using MicroKit.Domain.Aggregates;
using MicroKit.Domain.ValueObjects.Common;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Domain.Memberships;

/// <summary>
/// The relation saying who belongs to an organization's people. It is not an authorization: it
/// lets nobody act in a workspace (INV-ACCESS-05). It is current until its end is recorded
/// (contract.md §5); no process of BP-001 ends it.
/// </summary>
public sealed class Membership : AuditableAggregateRoot<MembershipId>
{
    /// <summary>
    /// Creates a current membership of a person in an organization. Public, unlike the constructors
    /// of the founding's other aggregates: later processes create memberships too (BP-004), and the
    /// domain tests need a membership without ownership (BP001-AC-04). In BP-001, the founding
    /// creates it (<see cref="Founding.CompanyFounding"/>).
    /// </summary>
    /// <param name="id">The membership's identifier.</param>
    /// <param name="profileId">The member, through their profile.</param>
    /// <param name="organizationId">The organization.</param>
    /// <param name="createdAt">The instant of the creation, from the injected clock.</param>
    /// <param name="createdBy">Who creates it.</param>
    public Membership(
        MembershipId id, ProfileId profileId, OrganizationId organizationId, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy)
    {
        Identifier.Require(profileId.Value, nameof(profileId));
        Identifier.Require(organizationId.Value, nameof(organizationId));

        ProfileId = profileId;
        OrganizationId = organizationId;
    }

    /// <summary>The member, through their profile.</summary>
    public ProfileId ProfileId { get; private init; }

    /// <summary>The organization.</summary>
    public OrganizationId OrganizationId { get; private init; }

    /// <summary>When the membership ended; null while it is current (contract.md §5).</summary>
    public DateTimeOffset? EndedAt { get; private init; }
}

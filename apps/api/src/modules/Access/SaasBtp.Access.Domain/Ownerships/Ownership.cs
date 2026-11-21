using MicroKit.Domain.Aggregates;
using MicroKit.Domain.ValueObjects.Common;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Domain.Ownerships;

/// <summary>
/// The relation saying to whom an organization belongs. It is not an authorization: it lets
/// nobody act in a workspace (INV-ACCESS-05). In BP-001 only the founding creates one.
/// </summary>
public sealed class Ownership : AuditableAggregateRoot<OwnershipId>
{
    internal Ownership(
        OwnershipId id, ProfileId profileId, OrganizationId organizationId, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy)
    {
        Identifier.Require(profileId.Value, nameof(profileId));
        Identifier.Require(organizationId.Value, nameof(organizationId));

        ProfileId = profileId;
        OrganizationId = organizationId;
    }

    /// <summary>The owner, through their profile.</summary>
    public ProfileId ProfileId { get; private init; }

    /// <summary>The organization owned.</summary>
    public OrganizationId OrganizationId { get; private init; }
}

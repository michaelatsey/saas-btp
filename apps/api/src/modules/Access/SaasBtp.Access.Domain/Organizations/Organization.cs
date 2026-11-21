using MicroKit.Domain.Aggregates;
using MicroKit.Domain.ValueObjects.Common;
using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.Organizations;

/// <summary>
/// A client company, as declared by the person who founded it (docs/domain/access.md). Only the
/// founding creates one (<see cref="CompanyFounding"/>), Declared (INV-ACCESS-08); the domain
/// offers no operation that changes its status, which only verification (BP-009) will.
/// </summary>
public sealed class Organization : AuditableAggregateRoot<OrganizationId>
{
    private Organization(
        OrganizationId id, OrganizationName name, OwnershipId currentOwnershipId, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy)
    {
        ArgumentNullException.ThrowIfNull(name);
        Identifier.Require(currentOwnershipId.Value, nameof(currentOwnershipId));

        Name = name;
        Status = OrganizationStatus.Declared;
        CurrentOwnershipId = currentOwnershipId;
    }

    /// <summary>The company name, as declared (BP001-R7).</summary>
    public OrganizationName Name { get; private init; }

    /// <summary>The status: <see cref="OrganizationStatus.Declared"/> from the founding on (INV-ACCESS-08).</summary>
    public OrganizationStatus Status { get; private init; }

    /// <summary>
    /// The ownership that names the organization's one current owner (INV-ACCESS-02,
    /// ADR-ARCH-033 §6).
    /// </summary>
    public OwnershipId CurrentOwnershipId { get; private init; }

    /// <summary>
    /// The organization a founding creates under the request's reference, Declared, owned through
    /// <paramref name="currentOwnershipId"/>; raises <see cref="CompanyFounded"/>.
    /// </summary>
    internal static Organization Found(
        OrganizationId reference,
        OrganizationName name,
        OwnershipId currentOwnershipId,
        WorkspaceId firstWorkspaceId,
        ProfileId founderProfileId,
        DateTimeOffset at,
        Actor by)
    {
        var organization = new Organization(reference, name, currentOwnershipId, at, by);
        organization.RaiseDomainEvent(new CompanyFounded(reference, firstWorkspaceId, founderProfileId, at));
        return organization;
    }
}

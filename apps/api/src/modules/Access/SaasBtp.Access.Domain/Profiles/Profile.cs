using MicroKit.Domain.Aggregates;
using MicroKit.Domain.ValueObjects.Common;

namespace SaasBtp.Access.Domain.Profiles;

/// <summary>
/// How the product knows a person: the anchor every ownership, membership and access refers to
/// (INV-ACCESS-01). It records no business fact of its own.
/// </summary>
public sealed class Profile : AuditableAggregateRoot<ProfileId>
{
    /// <summary>
    /// Creates the profile of <paramref name="userId"/>. Public, unlike the constructors of the
    /// founding's other aggregates: later processes create profiles too (BP-004), and the domain
    /// tests need a profile that no founding created (BP001-AC-04). In BP-001, the founding creates
    /// it (<see cref="Founding.CompanyFounding"/>).
    /// </summary>
    /// <param name="id">The profile's identifier.</param>
    /// <param name="userId">The identity provider's identifier of the person.</param>
    /// <param name="createdAt">The instant of the creation, from the injected clock.</param>
    /// <param name="createdBy">Who creates it.</param>
    public Profile(ProfileId id, UserId userId, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy)
    {
        Identifier.Require(userId.Value, nameof(userId));
        UserId = userId;
    }

    /// <summary>The identity provider's identifier of the person.</summary>
    public UserId UserId { get; private init; }
}

using MicroKit.Domain.ValueObjects;
using SaasBtp.Access.Domain.Identity;

namespace SaasBtp.Access.Domain.Organizations;

/// <summary>
/// The PROPERTY axis, held INSIDE the <see cref="Organization"/> aggregate: "this organization
/// belongs to this person, since this instant".
/// </summary>
/// <remarks>
/// It is a component of the organization, not a root of its own (ADR-ARCH-016): the invariant it
/// participates in — exactly one initial owner — spans the organization AND this fact, so the
/// boundary that protects the invariant is the one that contains both.
/// <para>
/// It carries no identity of its own because it needs none: no process revokes an ownership or
/// keeps its history, so the fact is entirely defined by its values. That is the asymmetry with
/// <c>WorkspaceAccess</c>, which must survive revocation and therefore keeps an id.
/// </para>
/// <para>
/// Ownership is NOT authorization. <c>owns → access</c> stays a forbidden derivation
/// (ADR-ARCH-013): no code path may read this fact to decide where its owner may act. Absorbing the
/// fact into the aggregate changes where it is STORED, never what it MEANS.
/// </para>
/// </remarks>
public sealed record Ownership : IValueObject
{
    private Ownership(PersonId owner, DateTime establishedAtUtc)
    {
        Owner = owner;
        EstablishedAtUtc = establishedAtUtc;
    }

    /// <summary>The person the organization belongs to.</summary>
    public PersonId Owner { get; }

    /// <summary>The UTC instant the ownership was established, as supplied by the caller.</summary>
    public DateTime EstablishedAtUtc { get; }

    /// <summary>
    /// Establishes the fact. Internal on purpose: <see cref="Organization"/> is the only thing
    /// entitled to create one, because it is the boundary that guarantees there is exactly one.
    /// </summary>
    /// <param name="owner">The owning person.</param>
    /// <param name="establishedAtUtc">The UTC instant supplied by the caller.</param>
    /// <returns>The ownership fact.</returns>
    internal static Ownership Establish(PersonId owner, DateTime establishedAtUtc) =>
        new(owner, establishedAtUtc);
}

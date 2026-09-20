using MicroKit.Domain.Identifiers;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// Strongly-typed site (chantier) reference held by a <see cref="Constat"/> — the site the finding
/// belongs to (safety-domain-model.md §4: a constat is intrinsically attached to a chantier).
/// </summary>
/// <remarks>
/// Safety owns its OWN <see cref="SiteId"/> and never references the Site module (bounded-context
/// isolation, §8). It carries the site captured at observation time — not the caller's current
/// site — so an offline replay batch stays faithful to where each constat happened (§5). It is a
/// value here; site-belongs-to-tenant validation is a command-layer concern (branch 2), and
/// user-belongs-to-site is Membership (ADR-ARCH-004), out of scope.
/// </remarks>
/// <param name="Value">The underlying site UUID.</param>
public readonly record struct SiteId(Guid Value) : IEntityId
{
    /// <inheritdoc/>
    object IEntityId.Value => Value;

    /// <summary>Returns the underlying <see cref="Guid"/> as a string.</summary>
    public override string ToString() => Value.ToString();
}

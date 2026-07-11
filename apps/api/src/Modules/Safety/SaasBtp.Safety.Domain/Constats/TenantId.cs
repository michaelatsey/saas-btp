using MicroKit.Domain.Identifiers;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// Strongly-typed tenant reference held by a <see cref="Constat"/> (isolation boundary).
/// </summary>
/// <remarks>
/// Safety owns its OWN <see cref="TenantId"/> rather than referencing the Access/tenancy module:
/// bounded contexts do not share types (safety-domain-model.md §8). It is a foreign reference, not
/// this aggregate's identity; persisted as historical data, its trust boundary (deriving it from
/// the JWT) belongs to the command layer, not the aggregate (ADR-ARCH-005, branch 2).
/// </remarks>
/// <param name="Value">The underlying tenant UUID.</param>
public readonly record struct TenantId(Guid Value) : IEntityId
{
    /// <inheritdoc/>
    object IEntityId.Value => Value;

    /// <summary>Returns the underlying <see cref="Guid"/> as a string.</summary>
    public override string ToString() => Value.ToString();
}

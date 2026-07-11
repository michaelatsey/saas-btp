using MicroKit.Domain.Identifiers;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// Strongly-typed identifier for a <see cref="Constat"/>: a client-generated UUIDv7 (naming.md id
/// convention), wrapped so a raw <see cref="Guid"/> is never used as an identifier in the domain.
/// </summary>
/// <remarks>
/// Modeled as a <c>readonly record struct</c> implementing <see cref="IEntityId"/> — mirroring
/// MicroKit's own <c>GuidId</c> — which both honors the naming convention and satisfies the
/// <c>where TId : IEntityId</c> constraint on <see cref="MicroKit.Domain.Aggregates.AggregateRoot{TId}"/>.
/// The value doubles as the idempotency key on the write path (safety-domain-model.md §6). Empty is
/// rejected by the <see cref="Constat.Create"/> factory (as a Result failure), not here.
/// </remarks>
/// <param name="Value">The underlying UUIDv7 value.</param>
public readonly record struct ConstatId(Guid Value) : IEntityId
{
    /// <inheritdoc/>
    object IEntityId.Value => Value;

    /// <summary>Returns the underlying <see cref="Guid"/> as a string.</summary>
    public override string ToString() => Value.ToString();
}

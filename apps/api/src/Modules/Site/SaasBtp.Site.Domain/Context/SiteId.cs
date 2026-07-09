namespace SaasBtp.Site.Domain.Context;

/// <summary>Strongly-typed site identifier. Wraps a <see cref="Guid"/>.</summary>
/// <param name="Value">The underlying identifier value.</param>
public sealed record SiteId(Guid Value)
{
    /// <summary>Creates a new random <see cref="SiteId"/>.</summary>
    public static SiteId NewId() => new(Guid.NewGuid());

    /// <summary>Returns the string representation of the underlying <see cref="Guid"/>.</summary>
    public override string ToString() => Value.ToString();
}

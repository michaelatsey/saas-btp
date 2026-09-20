using MicroKit.Domain.ValueObjects;

namespace SaasBtp.Access.Domain.Organizations;

/// <summary>
/// The identity-maturity status of an <see cref="Organization"/> — a closed-set value object.
/// </summary>
/// <remarks>
/// The value object is the source of truth for validity; the database CHECK is defense-in-depth
/// (conventions/sql.md §Closed sets). The token is the stored form: lowercase snake_case, identical
/// end to end, mapped by an EXPLICIT converter so a C# rename can never silently drift from it.
/// <para>
/// Only <see cref="Declared"/> exists. BP-001 produces a DECLARED organization; a VERIFIED one is
/// the product of a separate verification process that has no writer today, so adding its token
/// here would be speculative — that process introduces it together with the CHECK it amends.
/// </para>
/// </remarks>
public sealed record OrganizationStatus : IValueObject
{
    private OrganizationStatus(string token) => Token = token;

    /// <summary>
    /// The organization was declared by its founder and has not been verified. The only status
    /// BP-001 produces.
    /// </summary>
    public static OrganizationStatus Declared { get; } = new("declared");

    /// <summary>The stored token for this status (lowercase snake_case).</summary>
    public string Token { get; }

    /// <summary>Rehydrates a status from its stored token.</summary>
    /// <param name="token">The stored token.</param>
    /// <returns>The status for that token.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The token is not a known status.</exception>
    public static OrganizationStatus FromToken(string token) =>
        token == Declared.Token
            ? Declared
            : throw new ArgumentOutOfRangeException(
                nameof(token), token, "Unknown organization status token.");

    /// <summary>Returns the stored token.</summary>
    public override string ToString() => Token;
}

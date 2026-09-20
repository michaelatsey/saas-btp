using System.Collections.Frozen;
using MicroKit.Domain.ValueObjects;
using MicroKit.Result;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// The kind of safety finding a <see cref="Constat"/> records. A closed set of three members.
/// </summary>
/// <remarks>
/// Modeled as a Value Object (not an open string) so it can carry behavior/localization later
/// without an API break (safety-domain-model.md §3). The persisted <see cref="Token"/> is lowercase
/// snake_case technical English, decoupled from this C# identifier and from any UI label (sql.md
/// §Closed sets); the domain is the source of truth for validity, the DB CHECK is defense-in-depth.
/// </remarks>
public sealed record ConstatType : IValueObject
{
    /// <summary>An event that caused, or could have caused, harm.</summary>
    public static readonly ConstatType Incident = new("incident");

    /// <summary>An event that caused injury or damage.</summary>
    public static readonly ConstatType Accident = new("accident");

    /// <summary>A hazardous condition, with no triggering event (yet).</summary>
    public static readonly ConstatType DangerousSituation = new("dangerous_situation");

    // Declared after the members (static field initialization is textual-order): the members must
    // exist before the lookup is built.
    private static readonly ConstatType[] Members = [Incident, Accident, DangerousSituation];

    // FrozenDictionary gives O(1) token resolution for the EF value converter and (branch 2) DTO
    // parsing — no linear scan, no exception-driven control flow on the map path.
    private static readonly FrozenDictionary<string, ConstatType> ByToken =
        Members.ToFrozenDictionary(type => type.Token, StringComparer.Ordinal);

    private ConstatType(string token) => Token = token;

    /// <summary>The stored, wire-stable token (lowercase snake_case technical English).</summary>
    public string Token { get; }

    /// <summary>All members of the closed set, in declaration order.</summary>
    public static IReadOnlyList<ConstatType> All => Members;

    /// <summary>Resolves a <see cref="ConstatType"/> from its persisted token.</summary>
    /// <param name="token">The stored token (e.g. <c>dangerous_situation</c>).</param>
    /// <returns>The matching member, or a validation failure for a null/unknown token.</returns>
    public static Result<ConstatType> FromToken(string? token) =>
        token is not null && ByToken.TryGetValue(token, out var type)
            ? Result<ConstatType>.Success(type)
            : Result<ConstatType>.Failure(ConstatErrors.UnknownConstatType(token));

    /// <summary>Returns the persisted <see cref="Token"/>.</summary>
    public override string ToString() => Token;
}

using System.Collections.Frozen;
using MicroKit.Domain.ValueObjects;
using MicroKit.Result;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// How serious a <see cref="Constat"/> is. A closed set of three members that drives the safety
/// dashboard and escalation.
/// </summary>
/// <remarks>
/// A standalone Value Object (safety-domain-model.md §3). It is NOT a placeholder for a future
/// Severity × Probability risk matrix — that was explicitly rejected as YAGNI (§9). Same
/// token/CHECK contract as <see cref="ConstatType"/> (sql.md §Closed sets).
/// </remarks>
public sealed record Severity : IValueObject
{
    /// <summary>Minor severity.</summary>
    public static readonly Severity Minor = new("minor");

    /// <summary>Major severity.</summary>
    public static readonly Severity Major = new("major");

    /// <summary>Critical severity (drives priority review / escalation).</summary>
    public static readonly Severity Critical = new("critical");

    // Declared after the members: static field initialization is textual-order.
    private static readonly Severity[] Members = [Minor, Major, Critical];

    // O(1) token resolution for the EF value converter and (branch 2) DTO parsing.
    private static readonly FrozenDictionary<string, Severity> ByToken =
        Members.ToFrozenDictionary(severity => severity.Token, StringComparer.Ordinal);

    private Severity(string token) => Token = token;

    /// <summary>The stored, wire-stable token (lowercase snake_case technical English).</summary>
    public string Token { get; }

    /// <summary>All members of the closed set, in ascending order of seriousness.</summary>
    public static IReadOnlyList<Severity> All => Members;

    /// <summary>Resolves a <see cref="Severity"/> from its persisted token.</summary>
    /// <param name="token">The stored token (e.g. <c>critical</c>).</param>
    /// <returns>The matching member, or a validation failure for a null/unknown token.</returns>
    public static Result<Severity> FromToken(string? token) =>
        token is not null && ByToken.TryGetValue(token, out var severity)
            ? Result<Severity>.Success(severity)
            : Result<Severity>.Failure(ConstatErrors.UnknownSeverity(token));

    /// <summary>Returns the persisted <see cref="Token"/>.</summary>
    public override string ToString() => Token;
}

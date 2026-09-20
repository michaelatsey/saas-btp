namespace SaasBtp.Access.Domain.Time;

/// <summary>
/// The one place the Access domain states its time contract: every instant entering an aggregate is
/// UTC, supplied by the caller — never taken from an ambient clock inside the domain, and never
/// from a database default (ADR-ARCH-005, conventions/sql.md).
/// </summary>
/// <remarks>
/// This is a caller PRECONDITION, not a business invariant: a non-UTC instant is a bug in the
/// calling code, so it throws <see cref="ArgumentException"/> rather than being reified as an
/// <c>IBusinessRule</c>. Business invariants live in each aggregate's <c>Rules/</c> folder.
/// <para>
/// It does NOT duplicate <see cref="System.TimeProvider"/> and could not be replaced by it: a
/// <c>TimeProvider</c> PRODUCES the current instant, which this domain deliberately never does —
/// the caller stamps the founding instant and passes it in. What this asserts is the opposite
/// direction, that an instant ARRIVING from outside is UTC, and no clock abstraction checks that.
/// </para>
/// <para>
/// It belongs to neither aggregate — both founding factories rely on it — so it lives in its own
/// folder for the shared time vocabulary, the same way <c>Identity/PersonId</c> holds the shared
/// identity vocabulary. It is not an aggregate and must not masquerade as one.
/// </para>
/// </remarks>
public static class DomainTime
{
    /// <summary>Asserts that an instant crossing into the domain is UTC.</summary>
    /// <param name="instant">The instant to check.</param>
    /// <param name="parameterName">The caller's parameter name, for the exception message.</param>
    /// <exception cref="ArgumentException">The instant's kind is not <see cref="DateTimeKind.Utc"/>.</exception>
    public static void MustBeUtc(
        DateTime instant,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(instant))] string? parameterName = null)
    {
        if (instant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Instants entering the domain must be UTC (DateTimeKind.Utc); the caller supplies the stamp.",
                parameterName);
        }
    }
}

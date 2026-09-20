using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Result;

namespace SaasBtp.Access.Application.Features.FoundCompanySpace;

/// <summary>
/// The identity boundary: establishes the founder's authenticated identity (<c>createUser</c> on the
/// GoTrue Admin API) BEFORE and OUTSIDE the business transaction.
/// </summary>
/// <remarks>
/// An APPLICATION port, and legitimately so: it abstracts an EXTERNAL SYSTEM the use case
/// orchestrates, not a collection of aggregates. No aggregate needs it to enforce an invariant, and
/// nothing in the domain model knows GoTrue exists — so placing it in the Domain would import a
/// foreign system into the model. This is the same category as an e-mail sender or a payment
/// gateway, which reference DDD implementations also keep at the application boundary.
/// <para>
/// It stays inside this slice while BP-001 is its only consumer; it moves to a module-level folder
/// the day a second use case (invitation acceptance) needs it — not before.
/// </para>
/// <para>
/// SECURITY — the adapter must implement ADR-ARCH-011 exactly: on "already exists" (422), resolve by
/// Admin API lookup and adopt the orphan by EXACT normalized-e-mail match. The Admin API filter is a
/// PREFIX search, so exactly one match adopts, zero fails, and more than one aborts loudly. Taking
/// the first match would attach the founding facts to the WRONG PERSON — an impersonation, not a bug.
/// </para>
/// </remarks>
public interface IIdentityProvisioner
{
    /// <summary>Establishes, or adopts, the founder's authenticated identity.</summary>
    /// <param name="email">The founder's e-mail (already normalized by the value object).</param>
    /// <param name="fullName">The founder's full name, forwarded as identity metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The established identity, or an expected failure —
    /// <see cref="FoundCompanySpaceErrors.IdentityNotEstablished"/> or
    /// <see cref="FoundCompanySpaceErrors.AmbiguousFounderIdentity"/>.
    /// </returns>
    ValueTask<Result<ProvisionedIdentity>> EstablishAsync(
        Email email, string fullName, CancellationToken ct = default);
}

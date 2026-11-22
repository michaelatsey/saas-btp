using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Features.Ports;

/// <summary>
/// The person who submits the current request, as the module reads them (ADR-ARCH-038 decision 5):
/// the module never reads a claim, only this port, which its Adapters implement from the principal
/// the host authenticated.
/// </summary>
internal interface ICaller
{
    /// <summary>The caller, or <see langword="null"/> when the request has no authenticated caller.</summary>
    Caller? Current { get; }
}

/// <summary>A caller: their identity provider's user identifier and whether they hold the confirmed-contact level.</summary>
/// <param name="Subject">The provider's user identifier, the token's <c>sub</c>.</param>
/// <param name="HasConfirmedContact">
/// Whether the provider attests a confirmed contact (BP001-D1), as the token's <c>contact_confirmed</c> states it.
/// </param>
internal sealed record Caller(UserId Subject, bool HasConfirmedContact);

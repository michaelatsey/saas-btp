using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Features.Ports;

namespace SaasBtp.Access.Adapters.Caller;

/// <summary>
/// The caller port, from the principal the host authenticated from the validated access token,
/// read from <c>sub</c> and <c>contact_confirmed</c> alone (ADR-ARCH-038 decision 5).
/// </summary>
/// <remarks>
/// The host reads claims without mapping them (ADR-ARCH-038 decision 3), so their types are the
/// token's names. A JSON <c>true</c> reaches the principal as the value <c>true</c> of type
/// boolean; nothing else holds the level, a string <c>"true"</c> included, so that a claim the hook
/// did not set as it sets it fails closed.
/// </remarks>
internal sealed class TokenCaller(IHttpContextAccessor requests) : ICaller
{
    /// <summary>The token's subject, the provider's user identifier.</summary>
    public const string SubjectClaim = "sub";

    /// <summary>The claim the provider's access token hook sets (ADR-ARCH-038 decision 5).</summary>
    public const string ContactConfirmedClaim = "contact_confirmed";

    public Features.Ports.Caller? Current => From(requests.HttpContext?.User);

    /// <summary>
    /// The caller of an authenticated principal whose <c>sub</c> is a UUID; otherwise none. The level
    /// is held only when <c>contact_confirmed</c> is the boolean <c>true</c>.
    /// </summary>
    public static Features.Ports.Caller? From(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParseExact(principal.FindFirst(SubjectClaim)?.Value, "D", out var subject))
        {
            return null;
        }

        var confirmed = principal.FindFirst(ContactConfirmedClaim) is { } claim
            && claim.ValueType == ClaimValueTypes.Boolean
            && string.Equals(claim.Value, "true", StringComparison.Ordinal);

        return new Features.Ports.Caller(new UserId(subject), confirmed);
    }
}

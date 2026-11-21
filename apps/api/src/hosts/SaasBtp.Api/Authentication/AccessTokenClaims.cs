using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace SaasBtp.Api.Authentication;

/// <summary>
/// The checks of ADR-ARCH-038 decision 1 the JWT bearer handler has no parameter for, run once it
/// has validated the signature, the issuer, the audience and the expiry.
/// </summary>
internal static class AccessTokenClaims
{
    /// <summary>The subject, read without claim mapping (decision 3).</summary>
    public const string Subject = "sub";

    public const string IssuedAt = "iat";

    public const string Anonymous = "is_anonymous";

    public static Task CheckAsync(TokenValidatedContext context)
    {
        var clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
        var refusal = Refusal(context.SecurityToken as JsonWebToken, context.Principal, clock.GetUtcNow());
        if (refusal is not null)
        {
            context.Fail(new AccessTokenRefusedException(refusal));
        }

        return Task.CompletedTask;
    }

    /// <summary>The <c>sub</c> of an authenticated principal, when it is a UUID.</summary>
    public static Guid? SubjectOf(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
        && Guid.TryParseExact(principal.FindFirst(Subject)?.Value, "D", out var subject)
            ? subject
            : null;

    /// <summary>The <c>iat</c> of a principal, when it carries one in whole seconds.</summary>
    public static DateTimeOffset? IssuedAtOf(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirst(IssuedAt)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
        && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    private static string? Refusal(JsonWebToken? token, ClaimsPrincipal? principal, DateTimeOffset now)
    {
        if (token is null || principal is null)
        {
            return "not a JSON web token";
        }

        if (!token.TryGetPayloadValue<long>(IssuedAt, out var issuedAt))
        {
            return "no iat";
        }

        // A token issued later than now, beyond the skew, would make the lifetime bound and the
        // recent-token bound of decision 6 meaningless: its exp could be any time after now.
        if (issuedAt > now.ToUnixTimeSeconds() + (long)AccessTokenAuthentication.ClockSkew.TotalSeconds)
        {
            return "iat in the future";
        }

        // The handler has already required exp. Int128, so that no pair of claims overflows the
        // difference past the bound.
        if (!token.TryGetPayloadValue<long>("exp", out var expires)
            || (Int128)expires - issuedAt > (long)AccessTokenAuthentication.MaximumLifetime.TotalSeconds)
        {
            return "lifetime over one hour";
        }

        if (SubjectOf(principal) is null)
        {
            return "sub not a UUID";
        }

        if (string.Equals(principal.FindFirst(Anonymous)?.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "anonymous session";
        }

        return null;
    }
}

/// <summary>A token refused by a check of <see cref="AccessTokenClaims"/>, with its fixed reason.</summary>
internal sealed class AccessTokenRefusedException : Exception
{
    public AccessTokenRefusedException()
        : this("refused")
    {
    }

    public AccessTokenRefusedException(string reason)
        : base(reason) => Reason = reason;

    public AccessTokenRefusedException(string reason, Exception innerException)
        : base(reason, innerException) => Reason = reason;

    public string Reason { get; }
}

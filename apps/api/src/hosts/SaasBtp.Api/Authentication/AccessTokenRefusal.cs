using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace SaasBtp.Api.Authentication;

/// <summary>
/// Every 401 of the host: a bare <c>WWW-Authenticate: Bearer</c> challenge, since
/// <c>IncludeErrorDetails</c> is off, and one log entry with its reason (ADR-ARCH-038 decision 4).
/// </summary>
/// <remarks>
/// The reason is, in this order: the one a refusal passed to the challenge (the recent-token
/// refusal, <see cref="RecentTokenResultHandler"/>); the failure of the token's validation, by
/// the fixed reason of <see cref="AccessTokenClaims"/> or the type of the handler's exception;
/// otherwise, no bearer token. Never the token, a header value or an exception message.
/// </remarks>
internal static partial class AccessTokenRefusal
{
    /// <summary>The key under which a refusal passes its reason to the challenge.</summary>
    public const string ReasonKey = "SaasBtp.AccessToken.RefusalReason";

    public const string NoBearerToken = "no bearer token";

    public static AuthenticationProperties WithReason(string reason)
    {
        var properties = new AuthenticationProperties();
        properties.Items[ReasonKey] = reason;
        return properties;
    }

    public static Task LogAsync(JwtBearerChallengeContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AccessTokenRefusal).FullName!);

        var reason = ReasonOf(context);
        Refused(logger, reason);

        return Task.CompletedTask;
    }

    private static string ReasonOf(JwtBearerChallengeContext context)
    {
        if (context.Properties.Items.TryGetValue(ReasonKey, out var reason) && !string.IsNullOrEmpty(reason))
        {
            return reason;
        }

        return context.AuthenticateFailure switch
        {
            null => NoBearerToken,
            AccessTokenRefusedException refused => refused.Reason,
            var failure => failure.GetType().Name,
        };
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Access token refused, answered 401: {Reason}")]
    private static partial void Refused(ILogger logger, string reason);
}

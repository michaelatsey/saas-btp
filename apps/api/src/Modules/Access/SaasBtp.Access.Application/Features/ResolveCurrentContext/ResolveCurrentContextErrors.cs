using MicroKit.Result;

namespace SaasBtp.Access.Application.Features.ResolveCurrentContext;

/// <summary>
/// Typed errors returned by <see cref="ResolveCurrentContextHandler"/>. The driving adapter maps
/// their <see cref="ErrorCategory"/> to HTTP status codes (Unauthorized to 401, Forbidden to 403).
/// </summary>
public static class ResolveCurrentContextErrors
{
    /// <summary>No authenticated user is present in the current context (maps to HTTP 401).</summary>
    public static readonly IError NotAuthenticated = new NotAuthenticatedError();

    /// <summary>No tenant scope could be resolved for the current context (maps to HTTP 403).</summary>
    public static readonly IError TenantNotResolved = new TenantNotResolvedError();

    /// <summary>The caller is not authenticated.</summary>
    public sealed record NotAuthenticatedError()
        : Error(
            ErrorCode.From("ACCESS.CONTEXT.NOT_AUTHENTICATED"),
            "No authenticated user is present in the current context.")
    {
        /// <inheritdoc/>
        public override ErrorCategory Category => ErrorCategory.Unauthorized;
    }

    /// <summary>No tenant scope could be resolved for the caller.</summary>
    public sealed record TenantNotResolvedError()
        : Error(
            ErrorCode.From("ACCESS.CONTEXT.TENANT_NOT_RESOLVED"),
            "No tenant scope could be resolved for the current context.")
    {
        /// <inheritdoc/>
        public override ErrorCategory Category => ErrorCategory.Forbidden;
    }
}

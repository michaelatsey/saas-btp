using MicroKit.Result;

namespace SaasBtp.Site.Application.Features.ResolveCurrentSite;

/// <summary>
/// Typed errors for the <c>ResolveCurrentSite</c> slice. The driving adapter maps their
/// <see cref="ErrorCategory"/> to HTTP status codes (Unauthorized to 401, Forbidden to 403).
/// </summary>
/// <remarks>
/// The handler emits <see cref="NotAuthenticated"/> and <see cref="TenantNotResolved"/>.
/// <see cref="SiteNotInTenant"/> is emitted by the driving adapter (the <c>/context</c> endpoint)
/// when the CurrentSite middleware rejected a forged / cross-tenant <c>X-Site-Id</c>: that
/// resolution state is an Infrastructure detail kept off the Domain interface, so the gate lives in
/// the adapter, not the handler.
/// </remarks>
public static class ResolveCurrentSiteErrors
{
    /// <summary>No authenticated user is present in the current context (maps to HTTP 401).</summary>
    public static readonly IError NotAuthenticated = new NotAuthenticatedError();

    /// <summary>No tenant scope could be resolved for the current context (maps to HTTP 403).</summary>
    public static readonly IError TenantNotResolved = new TenantNotResolvedError();

    /// <summary>The requested site does not belong to the current tenant (maps to HTTP 403).</summary>
    public static readonly IError SiteNotInTenant = new SiteNotInTenantError();

    /// <summary>The caller is not authenticated.</summary>
    public sealed record NotAuthenticatedError()
        : Error(
            ErrorCode.From("SITE.CONTEXT.NOT_AUTHENTICATED"),
            "No authenticated user is present in the current context.")
    {
        /// <inheritdoc/>
        public override ErrorCategory Category => ErrorCategory.Unauthorized;
    }

    /// <summary>No tenant scope could be resolved for the caller.</summary>
    public sealed record TenantNotResolvedError()
        : Error(
            ErrorCode.From("SITE.CONTEXT.TENANT_NOT_RESOLVED"),
            "No tenant scope could be resolved for the current context.")
    {
        /// <inheritdoc/>
        public override ErrorCategory Category => ErrorCategory.Forbidden;
    }

    /// <summary>The client-provided <c>X-Site-Id</c> is not among the current tenant's available sites.</summary>
    public sealed record SiteNotInTenantError()
        : Error(
            ErrorCode.From("SITE.CONTEXT.SITE_NOT_IN_TENANT"),
            "The requested site does not belong to the current tenant.")
    {
        /// <inheritdoc/>
        public override ErrorCategory Category => ErrorCategory.Forbidden;
    }
}

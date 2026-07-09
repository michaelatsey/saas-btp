namespace SaasBtp.Site.Infrastructure.Context;

/// <summary>
/// Outcome of resolving the client-provided <c>X-Site-Id</c> for the current request. An
/// Infrastructure detail: it lives on <see cref="CurrentSiteContext"/> and is read only by the
/// driving adapter to choose a status code. It is deliberately absent from the
/// <c>ICurrentSiteContext</c> Domain interface — HTTP rejection is not a Domain concern.
/// </summary>
public enum SiteResolution
{
    /// <summary>No <c>X-Site-Id</c> header was provided; no active site (HTTP 200, currentSite null).</summary>
    NotRequested,

    /// <summary>The provided <c>X-Site-Id</c> belongs to the tenant's available sites (HTTP 200, currentSite populated).</summary>
    Resolved,

    /// <summary>An <c>X-Site-Id</c> was provided but is not in the tenant (forged / cross-tenant / unparseable) (HTTP 403).</summary>
    Rejected,
}

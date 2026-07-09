namespace SaasBtp.Site.Domain.Context;

/// <summary>
/// The site scope resolved for the current request: the active site (if the client asserted one)
/// and the current tenant's catalogue of available sites.
/// </summary>
/// <remarks>
/// A pure read-only projection. It carries no HTTP/rejection state — how a forged or cross-tenant
/// <c>X-Site-Id</c> maps to a status code is an Infrastructure concern, deliberately kept off this
/// interface. <see cref="AvailableSites"/> is context data (the tenant catalogue), NOT a membership
/// list and NOT an authorization source: feature modules (Safety, CorrectiveActions, ...) must not
/// use it to make access decisions (issue #17 / ADR-ARCH-004).
/// </remarks>
public interface ICurrentSiteContext
{
    /// <summary>The active site for the request, or <see langword="null"/> when the client asserted none.</summary>
    SiteScope? CurrentSite { get; }

    /// <summary>The sites available in the current tenant; empty when none, never <see langword="null"/>.</summary>
    IReadOnlyList<SiteScope> AvailableSites { get; }
}

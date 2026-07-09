namespace SaasBtp.Site.Application.Features.ResolveCurrentSite;

/// <summary>
/// Query to resolve the current caller's execution context: identity, tenant, org-level roles,
/// the active site (if any), and the tenant's available sites.
/// </summary>
/// <remarks>
/// Carries no client input: the "input" is the ambient security/tenant context established by the
/// Access pipeline plus the site scope resolved by the CurrentSite middleware. Mirrors
/// <c>ResolveCurrentContextQuery</c>.
/// </remarks>
public sealed record ResolveCurrentSiteQuery;

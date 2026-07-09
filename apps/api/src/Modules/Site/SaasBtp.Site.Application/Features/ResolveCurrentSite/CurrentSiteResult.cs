namespace SaasBtp.Site.Application.Features.ResolveCurrentSite;

/// <summary>
/// The execution context returned by <c>GET /context</c>: identity, tenant, org-level roles, the
/// active site (if any), and the tenant's available sites.
/// </summary>
/// <remarks>
/// Nested by bounded context (identity / tenant / site) rather than flattened, and serialized to
/// camelCase on the wire by the ASP.NET Core System.Text.Json default (naming.md).
/// <paramref name="Roles"/> are org-level roles from claims — identical to what <c>GET /me</c>
/// returns; site-level roles do not exist in Story A (they belong to Membership / Story B).
/// <paramref name="AvailableSites"/> is the tenant catalogue — NOT a membership list and NOT an
/// authorization source.
/// </remarks>
/// <param name="User">The authenticated caller's identity.</param>
/// <param name="Tenant">The resolved tenant.</param>
/// <param name="Roles">Org-level role names; empty when none, never <see langword="null"/>.</param>
/// <param name="CurrentSite">The active site, or <see langword="null"/> when no site was asserted.</param>
/// <param name="AvailableSites">The current tenant's site catalogue; empty when none, never <see langword="null"/>.</param>
public sealed record CurrentSiteResult(
    UserView User,
    TenantView Tenant,
    IReadOnlyList<string> Roles,
    SiteView? CurrentSite,
    IReadOnlyList<SiteView> AvailableSites);

/// <summary>Identity projection for the authenticated caller.</summary>
/// <param name="UserId">The user's identifier (JWT <c>sub</c>).</param>
/// <param name="Email">The user's email when present on the token; otherwise <see langword="null"/>.</param>
public sealed record UserView(Guid UserId, string? Email);

/// <summary>Tenant projection.</summary>
/// <param name="TenantId">The resolved tenant identifier (from the tenancy pipeline, store-validated).</param>
public sealed record TenantView(Guid TenantId);

/// <summary>Site projection on the wire: identifier and display name (tenant omitted — carried once at the root).</summary>
/// <param name="SiteId">The site identifier.</param>
/// <param name="Name">Human-readable site (chantier) name.</param>
public sealed record SiteView(Guid SiteId, string Name);

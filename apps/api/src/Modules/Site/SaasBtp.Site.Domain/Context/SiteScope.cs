namespace SaasBtp.Site.Domain.Context;

/// <summary>
/// Read-only projection of a site within the current execution context. Used both for the
/// resolved <see cref="ICurrentSiteContext.CurrentSite"/> and for each entry of
/// <see cref="ICurrentSiteContext.AvailableSites"/>.
/// </summary>
/// <remarks>
/// <paramref name="TenantId"/> is a raw <see cref="Guid"/> (not the MicroKit tenant value object)
/// so the Site domain references nothing. It scopes the site to its tenant for validation and
/// filtering and is never serialized on the wire (the tenant is carried once at the context root).
/// This is an execution-context value, NOT a Membership relation (ADR-ARCH-004).
/// </remarks>
/// <param name="SiteId">The site identifier.</param>
/// <param name="TenantId">The tenant the site belongs to (isolation boundary; not serialized).</param>
/// <param name="Name">Human-readable site (chantier) name.</param>
public sealed record SiteScope(SiteId SiteId, Guid TenantId, string Name);

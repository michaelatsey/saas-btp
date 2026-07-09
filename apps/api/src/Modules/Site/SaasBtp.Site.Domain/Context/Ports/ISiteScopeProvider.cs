namespace SaasBtp.Site.Domain.Context.Ports;

/// <summary>
/// Driven port returning the sites available within a tenant. This is the single seam Story B
/// (Membership) replaces with a membership-backed implementation, without changing the
/// <c>GET /context</c> contract.
/// </summary>
/// <remarks>
/// "Available" means tenant-catalogue availability, NOT user authorization: the only input is the
/// tenant. There is deliberately no user parameter — per-user site filtering requires Membership
/// and is out of scope for Story A (ADR-ARCH-004).
/// </remarks>
public interface ISiteScopeProvider
{
    /// <summary>Returns the sites belonging to <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The current tenant's identifier.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The tenant's site catalogue; empty when none, never <see langword="null"/>.</returns>
    ValueTask<IReadOnlyList<SiteScope>> GetAvailableSitesAsync(Guid tenantId, CancellationToken ct = default);
}

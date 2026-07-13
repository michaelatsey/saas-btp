namespace SaasBtp.Site.Domain.Context.Ports;

/// <summary>
/// Driven port answering whether a user may act within the scope of a given site — the module's
/// relationship-based authorization check (ADR-ARCH-009). The answer is DATA, resolved per request from
/// the two authorization edges (<c>site.site_memberships</c> and <c>access.memberships</c>), never a
/// token claim; revocation therefore takes effect on the next request, not at token expiry.
/// </summary>
/// <remarks>
/// Arguments are EXPLICIT — no ambient state, no header, no claim, no middleware resolution
/// (ADR-ARCH-005): <c>uploadData()</c> replays an offline batch captured across SEVERAL sites in ONE HTTP
/// request, so the site travels in the command payload and is passed here directly. Resolving the site in
/// middleware would be the <c>X-Site-Id</c> header under another name. <c>tenantId</c> is required
/// because cross-site scope (an owner-style tenant role) is a tenant-level fact that cannot be evaluated
/// from the site alone.
/// </remarks>
public interface ISiteScopeProvider
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="userId"/> may act within the scope of
    /// <paramref name="siteId"/> in tenant <paramref name="tenantId"/>: either an active, window-valid
    /// site membership on the site (this is the branch that covers people EXTERNAL to the tenant), or an
    /// active tenant membership whose role confers cross-site scope.
    /// </summary>
    /// <param name="userId">The acting user's identifier (JWT <c>sub</c>).</param>
    /// <param name="tenantId">The tenant the action is scoped to.</param>
    /// <param name="siteId">The site the action targets.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> if the user is authorized for the site's scope; otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> CanActInSiteScopeAsync(Guid userId, Guid tenantId, Guid siteId, CancellationToken ct = default);
}

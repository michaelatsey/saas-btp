using MicroKit.Auth;
using MicroKit.Result;
using MicroKit.Tenancy;
using SaasBtp.Site.Domain.Context;

namespace SaasBtp.Site.Application.Features.ResolveCurrentSite;

/// <summary>
/// Resolves the current caller's execution context from the ambient security/tenant context and the
/// request-scoped site context.
/// </summary>
/// <remarks>
/// Identity and org-level roles come from <see cref="ICurrentUserAccessor"/>; the tenant from
/// <see cref="ITenantContext"/> (store-validated, as in <c>GET /me</c>); the site scope from
/// <see cref="ICurrentSiteContext"/> (populated by the CurrentSite middleware). The handler reads
/// only the two-member Domain projection and never sees how a forged <c>X-Site-Id</c> maps to a
/// status code — that gate lives in the driving adapter.
/// </remarks>
public sealed class ResolveCurrentSiteHandler
{
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentSiteContext _currentSiteContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="currentUserAccessor">Accessor for the authenticated user in the current scope.</param>
    /// <param name="tenantContext">The tenant resolved for the current request.</param>
    /// <param name="currentSiteContext">The site scope resolved for the current request.</param>
    public ResolveCurrentSiteHandler(
        ICurrentUserAccessor currentUserAccessor,
        ITenantContext tenantContext,
        ICurrentSiteContext currentSiteContext)
    {
        _currentUserAccessor = currentUserAccessor;
        _tenantContext = tenantContext;
        _currentSiteContext = currentSiteContext;
    }

    /// <summary>
    /// Resolves the current execution context, or a typed failure when identity or tenant scope is absent.
    /// </summary>
    /// <param name="query">The (input-less) query.</param>
    /// <returns>
    /// <see cref="CurrentSiteResult"/> on success;
    /// <see cref="ResolveCurrentSiteErrors.NotAuthenticated"/> when there is no authenticated user;
    /// <see cref="ResolveCurrentSiteErrors.TenantNotResolved"/> when no tenant was resolved.
    /// </returns>
    public Result<CurrentSiteResult> Handle(ResolveCurrentSiteQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = _currentUserAccessor.Get();
        if (user is null || !user.IsAuthenticated)
            return Result<CurrentSiteResult>.Failure(ResolveCurrentSiteErrors.NotAuthenticated);

        var tenant = _tenantContext.CurrentTenant;
        if (tenant is null)
            return Result<CurrentSiteResult>.Failure(ResolveCurrentSiteErrors.TenantNotResolved);

        var result = new CurrentSiteResult(
            User: new UserView(user.UserId, user.Email),
            Tenant: new TenantView(tenant.Id.Value),
            Roles: user.Roles.Select(role => role.Name).ToArray(),
            CurrentSite: ToViewOrNull(_currentSiteContext.CurrentSite),
            AvailableSites: _currentSiteContext.AvailableSites.Select(ToView).ToArray());

        return Result<CurrentSiteResult>.Success(result);
    }

    private static SiteView ToView(SiteScope scope) => new(scope.SiteId.Value, scope.Name);

    private static SiteView? ToViewOrNull(SiteScope? scope) => scope is null ? null : ToView(scope);
}

using MicroKit.Auth;
using MicroKit.Result;
using MicroKit.Tenancy;

namespace SaasBtp.Access.Application.Features.ResolveCurrentContext;

/// <summary>
/// Resolves the current caller's org-level context from the ambient security and tenant context.
/// </summary>
/// <remarks>
/// Identity and roles come from <see cref="ICurrentUserAccessor"/>; the tenant comes from
/// <see cref="ITenantContext"/>. Reading the tenant from the tenancy pipeline (store-validated)
/// rather than the raw JWT claim is deliberate: <c>GET /me</c> exists to prove the full
/// MicroKit.Auth + MicroKit.Tenancy resolution chain end to end.
/// </remarks>
public sealed class ResolveCurrentContextHandler
{
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="currentUserAccessor">Accessor for the authenticated user in the current scope.</param>
    /// <param name="tenantContext">The tenant resolved for the current request.</param>
    public ResolveCurrentContextHandler(
        ICurrentUserAccessor currentUserAccessor,
        ITenantContext tenantContext)
    {
        _currentUserAccessor = currentUserAccessor;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Resolves the current context, or a typed failure when identity or tenant scope is absent.
    /// </summary>
    /// <param name="query">The (input-less) query.</param>
    /// <returns>
    /// <see cref="CurrentContextResult"/> on success;
    /// <see cref="ResolveCurrentContextErrors.NotAuthenticated"/> when there is no authenticated user;
    /// <see cref="ResolveCurrentContextErrors.TenantNotResolved"/> when no tenant was resolved.
    /// </returns>
    public Result<CurrentContextResult> Handle(ResolveCurrentContextQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = _currentUserAccessor.Get();
        if (user is null || !user.IsAuthenticated)
            return Result<CurrentContextResult>.Failure(ResolveCurrentContextErrors.NotAuthenticated);

        var tenant = _tenantContext.CurrentTenant;
        if (tenant is null)
            return Result<CurrentContextResult>.Failure(ResolveCurrentContextErrors.TenantNotResolved);

        var result = new CurrentContextResult(
            UserId: user.UserId,
            Email: user.Email,
            TenantId: tenant.Id.Value,
            Roles: user.Roles.Select(role => role.Name).ToArray());

        return Result<CurrentContextResult>.Success(result);
    }
}

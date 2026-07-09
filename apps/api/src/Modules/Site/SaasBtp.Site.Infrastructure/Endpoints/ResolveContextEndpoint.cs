using MicroKit.Result;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SaasBtp.Site.Application.Features.ResolveCurrentSite;
using SaasBtp.Site.Infrastructure.Context;

namespace SaasBtp.Site.Infrastructure.Endpoints;

/// <summary>
/// Driving adapter for the <c>ResolveCurrentSite</c> slice: the <c>GET /context</c> endpoint.
/// </summary>
public static class ResolveContextEndpoint
{
    /// <summary>
    /// Maps <c>GET /context</c>, which returns the current caller's execution context. Requires
    /// authorization (401 for anonymous) and maps handler failures plus a rejected site scope to
    /// HTTP status codes (401 unauthenticated, 403 no tenant, 403 forged/cross-tenant site).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapResolveContext(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/context",
            (ResolveCurrentSiteHandler handler, CurrentSiteContext siteContext) =>
                MapContextResult(handler.Handle(new ResolveCurrentSiteQuery()), siteContext.Resolution))
            .RequireAuthorization()
            .WithName("ResolveCurrentSite");

        return endpoints;
    }

    /// <summary>
    /// Maps the handler outcome and the site-resolution outcome to an HTTP result. Extracted from the
    /// endpoint lambda so the three-state mapping is unit-testable without an HTTP server.
    /// </summary>
    /// <param name="result">The handler result (identity/tenant checks + context projection).</param>
    /// <param name="resolution">How the request's <c>X-Site-Id</c> was resolved.</param>
    /// <returns>
    /// 200 with the context on success and a non-rejected site; 401 when unauthenticated; 403 when no
    /// tenant resolved or the site scope was rejected (forged / cross-tenant).
    /// </returns>
    public static IResult MapContextResult(Result<CurrentSiteResult> result, SiteResolution resolution)
    {
        if (result.IsFailure)
            return ToStatus(result.Error);

        if (resolution == SiteResolution.Rejected)
            return ToStatus(ResolveCurrentSiteErrors.SiteNotInTenant);

        return Results.Ok(result.Value);
    }

    private static IResult ToStatus(IError error) => error.Category switch
    {
        ErrorCategory.Unauthorized => Results.Unauthorized(),
        ErrorCategory.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ => Results.Problem(error.Message),
    };
}

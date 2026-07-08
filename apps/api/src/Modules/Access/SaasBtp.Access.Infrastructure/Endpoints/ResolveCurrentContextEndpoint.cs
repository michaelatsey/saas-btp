using MicroKit.Result;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SaasBtp.Access.Application.Features.ResolveCurrentContext;

namespace SaasBtp.Access.Infrastructure.Endpoints;

/// <summary>
/// Driving adapter for the <c>ResolveCurrentContext</c> slice: the <c>GET /me</c> endpoint.
/// </summary>
public static class ResolveCurrentContextEndpoint
{
    /// <summary>
    /// Maps <c>GET /me</c>, which returns the current caller's org-level context. Requires
    /// authorization; maps handler failures to HTTP status codes (401 for unauthenticated, 403
    /// when no tenant scope resolved).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapResolveCurrentContext(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/me", (ResolveCurrentContextHandler handler) =>
        {
            var result = handler.Handle(new ResolveCurrentContextQuery());

            if (result.IsSuccess)
                return Results.Ok(result.Value);

            return result.Error.Category switch
            {
                ErrorCategory.Unauthorized => Results.Unauthorized(),
                ErrorCategory.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                _ => Results.Problem(result.Error.Message),
            };
        })
        .RequireAuthorization()
        .WithName("ResolveCurrentContext");

        return endpoints;
    }
}

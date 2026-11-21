using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SaasBtp.Access.Adapters;

/// <summary>
/// The single entry point of the Access module (ADR-ARCH-034 §2): a host registers the module
/// and maps its routes through these two methods, called by type, and through nothing else.
/// </summary>
public static class AccessModule
{
    /// <summary>
    /// Registers everything the Access module needs, explicitly: no assembly scanning and no
    /// discovery by reflection.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAccessModule(this IServiceCollection services, IConfiguration configuration) =>
        services;

    /// <summary>
    /// Maps the routes of the Access module on the given route builder.
    /// </summary>
    /// <param name="endpoints">The route builder of the API's major version.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints;
}

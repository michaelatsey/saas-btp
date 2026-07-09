using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Site.Application.Features.ResolveCurrentSite;
using SaasBtp.Site.Domain.Context;
using SaasBtp.Site.Domain.Context.Ports;
using SaasBtp.Site.Infrastructure.Context;
using SaasBtp.Site.Infrastructure.Endpoints;
using SaasBtp.Site.Infrastructure.Middleware;

namespace SaasBtp.Site.Infrastructure;

/// <summary>
/// Composition surface for the Site module. Keeps the host thin: service registration, request
/// pipeline wiring, and endpoint mapping all live here. The module composes NO auth/tenancy — it
/// consumes the context the Access module already populated, so the Host must call
/// <see cref="UseSiteModule"/> AFTER <c>UseAccessModule</c>.
/// </summary>
public static class SiteModuleExtensions
{
    /// <summary>
    /// Registers the Site module: the provisional configuration-backed site catalogue, the
    /// request-scoped CurrentSite context, and the <c>ResolveCurrentSite</c> handler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration (the <c>SiteCatalogue</c> section).</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddSiteModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SiteCatalogueOptions>(
            configuration.GetSection(SiteCatalogueOptions.SectionKey));

        // Provisional Story A source (tenant catalogue). Story B swaps this single registration for
        // a membership-backed provider behind ISiteScopeProvider without touching the API.
        services.AddScoped<ISiteScopeProvider, ConfigurationSiteScopeProvider>();

        // One request-scoped instance behind both the concrete type (written by the middleware, read
        // by the endpoint) and the Domain interface (read by the handler).
        services.AddScoped<CurrentSiteContext>();
        services.AddScoped<ICurrentSiteContext>(sp => sp.GetRequiredService<CurrentSiteContext>());

        services.AddScoped<ResolveCurrentSiteHandler>();

        return services;
    }

    /// <summary>
    /// Adds the CurrentSite resolution middleware. Must run AFTER <c>UseAccessModule</c> so the
    /// tenant context is populated before the site is validated against it.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder for chaining.</returns>
    public static IApplicationBuilder UseSiteModule(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CurrentSiteMiddleware>();

        return app;
    }

    /// <summary>Maps the Site module's endpoints (currently only <c>GET /context</c>).</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapSiteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapResolveContext();

        return endpoints;
    }
}

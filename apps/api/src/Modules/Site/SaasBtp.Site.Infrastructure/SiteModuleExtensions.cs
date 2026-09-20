using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SaasBtp.Site.Application.Features.ResolveCurrentSite;
using SaasBtp.Site.Domain.Context;
using SaasBtp.Site.Domain.Context.Ports;
using SaasBtp.Site.Infrastructure.Authorization;
using SaasBtp.Site.Infrastructure.Context;
using SaasBtp.Site.Infrastructure.Endpoints;

namespace SaasBtp.Site.Infrastructure;

/// <summary>
/// Composition surface for the Site module. Keeps the host thin: service registration, request pipeline
/// wiring, and endpoint mapping all live here. The module composes NO auth/tenancy — it consumes the
/// context the Access module already populated, so the Host must call <see cref="UseSiteModule"/> AFTER
/// <c>UseAccessModule</c>.
/// </summary>
public static class SiteModuleExtensions
{
    /// <summary>
    /// Registers the Site module: the owner-role data source, the membership-backed
    /// <see cref="ISiteScopeProvider"/> (ADR-ARCH-009), and the (dormant until #49) CurrentSite context +
    /// <c>ResolveCurrentSite</c> handler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Application configuration; the owner connection string is read from
    /// <c>ConnectionStrings:SiteDb</c> (owner role, direct Postgres — it bypasses RLS).
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">The <c>SiteDb</c> connection string is missing.</exception>
    public static IServiceCollection AddSiteModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("SiteDb")
            ?? throw new InvalidOperationException(
                "Missing connection string 'ConnectionStrings:SiteDb' for the Site module.");

        // Non-ambient, Site-owned data source: wrapped in SiteScopeDataSource so no bare NpgsqlDataSource
        // singleton (a last-wins global for every future module) is registered. The connection is the
        // privileged owner role (ADR-ARCH-005) so the scope provider's reads bypass the no-policy tables'
        // RLS (#50). Constructing the data source opens no connection — that happens on first query.
        services.AddSingleton(new SiteScopeDataSource(NpgsqlDataSource.Create(connectionString)));

        // Membership-backed scope provider (ADR-ARCH-009): relationship authorization read directly over
        // the two edge tables. Replaces the provisional config-backed catalogue provider (Story A).
        services.AddScoped<ISiteScopeProvider, SiteMembershipScopeProvider>();

        // DORMANT until #49. CurrentSiteMiddleware is gone (ADR-ARCH-005 forbids resolving the site from a
        // header), so nothing populates this context today and GET /context stays 403 until tenant
        // resolution is repointed (#49). Kept compiling so #49 inherits a working seam rather than a gap.
        services.AddScoped<CurrentSiteContext>();
        services.AddScoped<ICurrentSiteContext>(sp => sp.GetRequiredService<CurrentSiteContext>());

        services.AddScoped<ResolveCurrentSiteHandler>();

        return services;
    }

    /// <summary>
    /// Site module request-pipeline hook. Currently a passthrough: the provisional CurrentSite middleware
    /// (X-Site-Id resolution) was removed — ADR-ARCH-005 forbids resolving the site from a header — and
    /// request-context wiring is rebuilt in #49. Kept so the Host's <c>UseSiteModule</c> call is stable.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder for chaining.</returns>
    public static IApplicationBuilder UseSiteModule(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app;
    }

    /// <summary>Maps the Site module's endpoints (currently only <c>GET /context</c>, dormant until #49).</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapSiteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapResolveContext();

        return endpoints;
    }
}

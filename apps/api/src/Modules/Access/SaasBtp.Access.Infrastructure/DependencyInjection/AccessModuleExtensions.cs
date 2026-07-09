using MicroKit.Auth.AspNetCore;
using MicroKit.Auth.Multitenancy;
using MicroKit.Auth.Supabase;
using MicroKit.Tenancy;
using MicroKit.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Access.Application.Features.ResolveCurrentContext;
using SaasBtp.Access.Infrastructure.Authentication;
using SaasBtp.Access.Infrastructure.Endpoints;

namespace SaasBtp.Access.Infrastructure.DependencyInjection;

/// <summary>
/// Composition surface for the Access module. Keeps the host thin: service registration, request
/// pipeline wiring, and endpoint mapping all live here.
/// </summary>
public static class AccessModuleExtensions
{
    /// <summary>
    /// Registers the Access module: Supabase JWT auth (MicroKit.Auth.Supabase), the custom
    /// authentication scheme bridging it to ASP.NET Core, claims-based tenant resolution (a single
    /// strategy, with no HTTP fallback strategies), a configuration-backed tenant store, and the
    /// <c>ResolveCurrentContext</c> handler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration (Supabase and Multitenancy sections).</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddAccessModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var supabase = configuration.GetSection("Supabase");

        // MicroKit.Auth core + Supabase ES256/JWKS validator + Supabase claims mapper.
        // Internally calls AddMicroKitAuth(), which also registers IHttpContextAccessor.
        services.AddMicroKitAuthSupabase(options =>
        {
            options.ProjectUrl = supabase["ProjectUrl"] ?? string.Empty;
            options.Audience = supabase["Audience"] ?? "authenticated";
            options.Issuer = supabase["Issuer"] ?? string.Empty;
        });

        // ASP.NET Core authentication scheme that hands the bearer token to MicroKit's IJwtValidator.
        services
            .AddAuthentication(SupabaseAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SupabaseAuthenticationHandler>(
                SupabaseAuthenticationHandler.SchemeName,
                static _ => { });

        services.AddAuthorization();

        // Tenancy pipeline + configuration-backed tenant store. A store is mandatory even for
        // claims-only resolution: a store miss rejects a forged/unknown tenant_id (security by
        // design, not persistence — no DbContext, no EF in #4).
        services.Configure<MultitenancyOptions>(
            configuration.GetSection(MultitenancyOptions.SectionKey));
        services.AddMicroKitMultitenancy(builder => builder.UseConfigurationStore());

        // Register ONLY the auth-claims resolution strategy (reads ICurrentUser.TenantId). We do NOT
        // call AddAspNetCoreResolution(): it would add Header/Route/flat-Claims strategies, and the
        // flat claims strategy would fail on every Supabase request (tenant_id is nested inside
        // app_metadata, never a top-level claim). Single-strategy pipeline = no per-request noise.
        services.AddMicroKitAuthMultitenancy();

        services.AddScoped<ResolveCurrentContextHandler>();

        return services;
    }

    /// <summary>
    /// Wires the Access request pipeline. Order is load-bearing: authentication populates
    /// <c>HttpContext.User</c>; <c>UseMicroKitAuth</c> maps it into the current-user accessor;
    /// tenant resolution reads that accessor; authorization runs last.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder for chaining.</returns>
    public static IApplicationBuilder UseAccessModule(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseMicroKitAuth();
        app.UseMultitenancy();
        app.UseAuthorization();

        return app;
    }

    /// <summary>Maps the Access module's endpoints (currently only <c>GET /me</c>).</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapResolveCurrentContext();

        return endpoints;
    }
}

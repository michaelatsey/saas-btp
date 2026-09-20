using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Safety.Infrastructure.Persistence;

namespace SaasBtp.Safety.Infrastructure;

/// <summary>
/// Composition surface for the Safety module (mirrors AddSiteModule / AddAccessModule).
/// </summary>
/// <remarks>
/// Registers <see cref="SafetyDbContext"/> via MicroKit.Persistence on the Npgsql provider.
/// Deliberately NOT invoked by the Host in this (foundation) branch: there is no endpoint yet, and
/// <c>UsePostgreSQL</c> requires a non-empty owner connection string at startup — the Host composes
/// Safety in branch 2 together with POST/GET. Exists now so the persistence wiring is authored and
/// reviewable. The connection must use the privileged owner role (ADR-ARCH-005).
/// </remarks>
public static class SafetyModuleExtensions
{
    /// <summary>
    /// Registers the Safety persistence: the <see cref="SafetyDbContext"/> on PostgreSQL, wired
    /// through MicroKit.Persistence.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Application configuration; the connection string is read from
    /// <c>ConnectionStrings:SafetyDb</c> (owner role, direct Postgres).
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The <c>SafetyDb</c> connection string is missing.
    /// </exception>
    public static IServiceCollection AddSafetyModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("SafetyDb")
            ?? throw new InvalidOperationException(
                "Missing connection string 'ConnectionStrings:SafetyDb' for the Safety module.");

        services.AddMicroKitPersistence(persistence =>
            persistence.AddEntityFrameworkCore(efCore =>
                efCore.UsePostgreSQL<SafetyDbContext>(connectionString)));

        return services;
    }
}

using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasBtp.Access.Adapters.Persistence;
using SaasBtp.Access.Adapters.Persistence.Founding;
using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters;

/// <summary>
/// The single entry point of the Access module (ADR-ARCH-034 §2): a host registers the module
/// and maps its routes through these two methods, called by type, and through nothing else.
/// </summary>
public static class AccessModule
{
    /// <summary>The connection string the module's context connects with, as <c>saasbtp_api</c> (ADR-ARCH-028).</summary>
    private const string ConnectionStringName = "SaasBtp";

    /// <summary>
    /// Registers everything the Access module needs, explicitly: no assembly scanning and no
    /// discovery by reflection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With the connection string <c>SaasBtp</c> set, it registers the module's context on the
    /// <c>access</c> schema and its unit of work through the copied PostgreSQL provider, which
    /// disables EF Core's automatic transactions, sets each transaction's subject first and refuses a
    /// command outside a transaction (ADR-ARCH-033 §4, ADR-ARCH-034 §3, ADR-ARCH-035 §3); then the
    /// repositories of the domain's ports and the founding's unit of work. The context is not pooled
    /// and its failures are not retried.
    /// </para>
    /// <para>
    /// Without it, the module registers no persistence: the API starts and answers its health
    /// endpoints without a database (<c>apps/api/CLAUDE.md</c>), and the provider refuses an empty
    /// connection string.
    /// </para>
    /// <para>
    /// The subject port, <see cref="IUnitOfWorkSubject"/>, is the host's, from the validated token
    /// (ADR-ARCH-038): the module registers none. It registers no HTTP client either, so nothing it
    /// does can write to the identity provider (contract.md §4).
    /// </para>
    /// </remarks>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAccessModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        services.AddMicroKitPersistence(persistence => persistence.AddEntityFrameworkCore()
            .UsePostgreSQL<AccessDbContext>(connectionString)
            .AddUnitOfWork<AccessDbContext>());

        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IOwnershipRepository, OwnershipRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IWorkspaceAccessRepository, WorkspaceAccessRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<FoundingUnitOfWork>();

        // The injected clock of every creation instant (sql.md, Audit / lifecycle); a host that
        // registers its own keeps it.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Maps the routes of the Access module on the given route builder.
    /// </summary>
    /// <param name="endpoints">The route builder of the API's major version.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints;
}

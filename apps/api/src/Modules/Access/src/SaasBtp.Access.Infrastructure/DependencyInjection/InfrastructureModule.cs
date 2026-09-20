using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.MediatR.DependencyInjection;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Access.Application;
using SaasBtp.Access.Application.DependencyInjection;
using SaasBtp.Access.Application.Features.FoundCompanySpace;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;
using SaasBtp.Access.Infrastructure.Identity;
using SaasBtp.Access.Infrastructure.Persistence;

namespace SaasBtp.Access.Infrastructure.DependencyInjection;

/// <summary>Composition root for the <c>SaasBtp.Access</c> module.</summary>
public static class InfrastructureModule
{
    /// <summary>
    /// Registers the whole Access module: the CQRS pipeline, the persistence adapters over the
    /// DbUp-owned <c>access</c> schema, and the identity boundary.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Application configuration. Reads <c>ConnectionStrings:AccessDb</c> (the privileged owner
    /// connection — ADR-ARCH-005) and the <c>Supabase</c> section.
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">A required configuration value is missing.</exception>
    public static IServiceCollection AddAccessModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAccessApplication();

        // CQRS pipeline, built from MicroKit's own behaviors (MicroKit.MediatR.Behaviors). Order is
        // DI REGISTRATION order — the Order / PipelineOrder property is metadata and nothing sorts
        // on it — so the sequence written here IS the sequence that runs. It follows MicroKit's
        // prescribed gradient: Logging (100) outermost, then Validation (300).
        //
        // Only two of the seven are registered, because only two have something to do in this module:
        //   - Logging (100)     always active, no marker (MicroKit ADR-004). BP-001 calls an EXTERNAL
        //                       system (GoTrue createUser) before its transaction; a founding attempt
        //                       that dies there must leave a trace, and this is the only place that
        //                       observes every dispatch including the failures.
        //   - Validation (300)  opt-in by registering an IValidator<TRequest>, which
        //                       FoundCompanySpaceCommandValidator already is. It short-circuits with
        //                       Result.Failure because the response is Result<T> — malformed input
        //                       never reaches the identity boundary.
        // Deliberately NOT registered (registering a behavior nothing needs is cost with no effect):
        //   - Authorization (200)  no request here implements IAuthorizedRequest; the founding call is
        //                          unauthenticated by design, and the behavior would additionally
        //                          demand IAuthorizationService + ICurrentUserAccessor in DI.
        //   - Idempotency (400)    no IIdempotentCommand. It IS the mechanism BP-001's RULE-4 would
        //                          need, but RULE-4 is an open gap in the design contract — closing it
        //                          here would be a decision this composition is not entitled to take.
        //   - Caching (500)        queries only; this module exposes none.
        //   - Retry (600)          no IRetryableRequest, and it would be actively wrong: the founding
        //                          command is not idempotent (RULE-4 again), so a replay could
        //                          provision twice.
        //   - Transaction (700)    BLOCKED, not skipped. It injects IDomainEventsDispatcher, whose
        //                          only implementation (DomainEventDispatcher) needs an
        //                          IDomainEventsProvider — an interface MicroKit implements on
        //                          AggregateRoot<TId> itself and registers nowhere. Resolving it
        //                          throws. See AccessPipelineTests for the executable statement of
        //                          this, and note that the behavior would not commit anyway: its
        //                          constructor takes (ITransactionalContext, IDomainEventsDispatcher)
        //                          and never calls IUnitOfWork.CommitAsync.
        services.AddMicroKitMediatR(mediatr => mediatr
            .FromAssemblyContaining<AccessApplicationAssemblyMarker>()
            .AddLoggingBehavior()
            .AddValidationBehavior());

        AddPersistence(services, configuration);
        AddIdentityBoundary(services, configuration);

        // The single UTC founding instant comes from here, never from an ambient clock inside an
        // aggregate — which is what makes the founding handler deterministic under test.
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AccessDb")
            ?? throw new InvalidOperationException(
                "Missing connection string 'ConnectionStrings:AccessDb' for the Access module.");

        // EF Core is the RUNTIME ORM only: it maps a schema DbUp owns and creates (ADR-ARCH-006).
        // AddUnitOfWork binds ONE scoped EfUnitOfWork to IUnitOfWork / ITransactionalContext /
        // ITransactionalUnitOfWork — its CommitAsync is a single SaveChangesAsync, so the founding
        // writes commit as one transaction with no hand-rolled transaction code.
        services.AddMicroKitPersistence(persistence =>
            persistence.AddEntityFrameworkCore(efCore => efCore
                .UsePostgreSQL<AccessDbContext>(connectionString)
                .AddUnitOfWork<AccessDbContext>()));

        // Two repositories, because the Access domain has two aggregate roots (ADR-ARCH-016). The
        // ownership, membership and access edges have no port of their own: they are components of
        // the roots and are persisted through them.
        services.AddScoped<IOrganizationRepository, EfOrganizationRepository>();
        services.AddScoped<IWorkspaceRepository, EfWorkspaceRepository>();
        services.AddScoped<IProfileProjection, EfProfileProjection>();
    }

    private static void AddIdentityBoundary(IServiceCollection services, IConfiguration configuration)
    {
        var supabase = configuration.GetSection("Supabase");

        var projectUrl = supabase["ProjectUrl"]
            ?? throw new InvalidOperationException("Missing configuration 'Supabase:ProjectUrl'.");
        var serviceRoleKey = supabase["ServiceRoleKey"]
            ?? throw new InvalidOperationException("Missing configuration 'Supabase:ServiceRoleKey'.");

        // The service-role key authenticates on the `apikey` header ALONE: adding an Authorization
        // Bearer header makes GoTrue reject the call as an invalid JWT (measured, #48 spike).
        services.AddHttpClient<IIdentityProvisioner, GoTrueIdentityProvisioner>(client =>
        {
            client.BaseAddress = new Uri(projectUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("apikey", serviceRoleKey);
        });
    }
}

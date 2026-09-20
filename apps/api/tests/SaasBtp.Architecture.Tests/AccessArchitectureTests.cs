using SaasBtp.Access.Application;
using SaasBtp.Access.Domain;
using SaasBtp.Access.Infrastructure;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// Enforces the hexagonal + modular-monolith boundaries for the Access context, re-established with the
/// module at BP-001 plan slice S1 (structure only, zero business logic). The rules asserted here are the
/// archetype's own layer-dependency contract: dependencies point INWARD only — Domain references nothing
/// outward, Application references Domain but never Infrastructure, and EF Core (if it ever appears) is
/// confined to Infrastructure. Layer checks read each assembly's manifest (direct references only), the
/// same technique as <see cref="SiteArchitectureTests"/>.
/// </summary>
/// <remarks>
/// Unlike Site, the Access Domain legitimately references <c>MicroKit.Domain</c>: this module is built on
/// MicroKit's DDD primitives (aggregates, entities, value objects, repository ports), so a blanket
/// "no MicroKit in Domain" rule would be wrong here. What stays forbidden in the Domain is the OUTWARD
/// direction — the host framework, the ORM, and the module's own outer layers.
/// </remarks>
public sealed class AccessArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(AccessDomainAssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(AccessApplicationAssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(AccessInfrastructureAssemblyMarker).Assembly;

    private static readonly Assembly[] AccessAssemblies =
        [DomainAssembly, ApplicationAssembly, InfrastructureAssembly];

    private static IReadOnlyCollection<string> Refs(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(name => name.Name!)];

    // 1. Domain does not depend on Application or Infrastructure — the innermost hexagon.
    [Fact]
    public void Domain_DoesNotDependOn_ApplicationOrInfrastructure()
    {
        Refs(DomainAssembly).ShouldNotContain("SaasBtp.Access.Application");
        Refs(DomainAssembly).ShouldNotContain("SaasBtp.Access.Infrastructure");
    }

    // 2. Domain is host- and persistence-agnostic: no ASP.NET Core, no EF Core, no MicroKit.Persistence.
    //    MicroKit.Domain is allowed (see the remarks above) — it IS the domain modelling primitive set.
    [Fact]
    public void Domain_DependsOnNoHostOrPersistenceConcern()
    {
        Refs(DomainAssembly).ShouldNotContain(name =>
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            name.StartsWith("MicroKit.Persistence", StringComparison.Ordinal) ||
            name.Contains("EntityFrameworkCore", StringComparison.Ordinal));
    }

    // 3. Application does not depend on Infrastructure.
    [Fact]
    public void Application_DoesNotDependOn_Infrastructure()
    {
        Refs(ApplicationAssembly).ShouldNotContain("SaasBtp.Access.Infrastructure");
    }

    // 4. Application is host-agnostic: no ASP.NET Core.
    [Fact]
    public void Application_DoesNotDependOn_AspNetCore()
    {
        Refs(ApplicationAssembly).ShouldNotContain(name =>
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    // 5. Application takes MicroKit ABSTRACTIONS only — never a concrete MicroKit module. The concrete
    //    MicroKit.MediatR (pipeline + composition) belongs to Infrastructure's composition root.
    //    MicroKit.Persistence.Abstractions is allowed HERE and forbidden in the Domain (rule 2): it
    //    carries IUnitOfWork, which MicroKit's own ADR-001 moved out of MicroKit.Domain because
    //    "committing is an infrastructure concern", and which it prescribes injecting in command
    //    handlers only. It is a contracts-only package — no EF, no provider.
    [Fact]
    public void Application_DependsOnlyOnMicroKitAbstractionsAndResult()
    {
        string[] allowed =
        [
            "MicroKit.Domain",
            "MicroKit.MediatR.Abstractions",
            "MicroKit.Result",
            "MicroKit.Persistence.Abstractions",
        ];

        var forbidden = Refs(ApplicationAssembly)
            .Where(name => name.StartsWith("MicroKit.", StringComparison.Ordinal))
            .Where(name => !allowed.Contains(name))
            .ToArray();

        forbidden.ShouldBeEmpty();
    }

    // 6. Inner layers never depend on Infrastructure (dependencies point inward).
    [Fact]
    public void InnerLayers_DoNotDependOn_Infrastructure()
    {
        Refs(DomainAssembly).ShouldNotContain("SaasBtp.Access.Infrastructure");
        Refs(ApplicationAssembly).ShouldNotContain("SaasBtp.Access.Infrastructure");
    }

    // 7. EF Core is confined to Infrastructure: it must never reach Domain or Application. Asserted as a
    //    DIRECTION rule, not an absence rule — Access will legitimately gain persistence in a later slice,
    //    and when it does, this test must still pass unchanged.
    [Fact]
    public void EntityFramework_IsConfinedTo_Infrastructure()
    {
        foreach (var assembly in (Assembly[])[DomainAssembly, ApplicationAssembly])
        {
            Refs(assembly).ShouldNotContain(name =>
                name.Contains("EntityFrameworkCore", StringComparison.Ordinal));

            var dbContexts = assembly.GetTypes()
                .Where(type => type.Name.Contains("DbContext", StringComparison.Ordinal))
                .ToArray();
            dbContexts.ShouldBeEmpty();
        }
    }

    // 8. Repository contracts live in the DOMAIN, never in the Application layer. Microsoft's DDD
    //    guidance is explicit ("define and place the repository interfaces in the domain model layer"),
    //    and a repository is the collection of an AGGREGATE — so an I*Repository in Application is
    //    either a misplaced domain contract or a use-case-shaped port wearing the wrong name. Both are
    //    defects this test exists to prevent from coming back.
    [Fact]
    public void RepositoryContracts_LiveIn_TheDomain_NotTheApplicationLayer()
    {
        var applicationRepositories = ApplicationAssembly.GetTypes()
            .Where(type => type.IsInterface)
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToArray();

        applicationRepositories.ShouldBeEmpty();
    }

    // 9. Every aggregate root that BP-001 persists has exactly ONE repository contract, in the Domain
    //    (Microsoft: "define one repository per aggregate"; never one per table, never a shared one).
    [Fact]
    public void EveryPersistedAggregateRoot_HasExactlyOne_DomainRepositoryContract()
    {
        var aggregateRoots = DomainAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.BaseType is { IsGenericType: true }
                && type.BaseType.GetGenericTypeDefinition().Name.StartsWith("AggregateRoot", StringComparison.Ordinal))
            .ToArray();

        aggregateRoots.ShouldNotBeEmpty();

        var repositoryNames = DomainAssembly.GetTypes()
            .Where(type => type.IsInterface)
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToArray();

        foreach (var aggregateRoot in aggregateRoots)
        {
            repositoryNames.ShouldContain(
                $"I{aggregateRoot.Name}Repository",
                $"aggregate root '{aggregateRoot.Name}' has no repository contract in the Domain");
        }
    }

    // 10. Modules do not reference each other: no Access assembly references a SaasBtp.* assembly outside
    //    SaasBtp.Access.* (architecture.md §2-3 — contexts communicate by events, never by direct
    //    reference). In particular, no reference to Site or Safety.
    [Fact]
    public void AccessModule_DoesNotReference_OtherModules()
    {
        foreach (var assembly in AccessAssemblies)
        {
            var crossModule = Refs(assembly)
                .Where(name => name.StartsWith("SaasBtp.", StringComparison.Ordinal))
                .Where(name => !name.StartsWith("SaasBtp.Access.", StringComparison.Ordinal))
                .ToArray();

            crossModule.ShouldBeEmpty();
        }
    }
}

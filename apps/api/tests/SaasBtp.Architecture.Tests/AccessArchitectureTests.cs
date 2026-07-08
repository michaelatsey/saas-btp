using SaasBtp.Access.Application.Features.ResolveCurrentContext;
using SaasBtp.Access.Domain;
using SaasBtp.Access.Infrastructure.DependencyInjection;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// Enforces the hexagonal + modular-monolith boundaries for the Access context, plus the
/// Story #4 guardrails: no EF (rule 7) and no Membership model per ADR-ARCH-004 (rule 9).
/// Layer dependency checks read each assembly's manifest (direct references only).
/// </summary>
public sealed class AccessArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(AccessDomainAssembly).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ResolveCurrentContextHandler).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(AccessModuleExtensions).Assembly;

    private static readonly Assembly[] AccessAssemblies =
        [DomainAssembly, ApplicationAssembly, InfrastructureAssembly];

    private static IReadOnlyCollection<string> Refs(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(name => name.Name!)];

    // 1. Domain does not depend on Application or Infrastructure.
    [Fact]
    public void Domain_DoesNotDependOn_ApplicationOrInfrastructure()
    {
        Refs(DomainAssembly).ShouldNotContain("SaasBtp.Access.Application");
        Refs(DomainAssembly).ShouldNotContain("SaasBtp.Access.Infrastructure");
    }

    // 2. Domain is a pure center: no MicroKit.Auth/Tenancy, ASP.NET Core, or EF.
    [Fact]
    public void Domain_HasNoInfrastructuralDependencies()
    {
        Refs(DomainAssembly).ShouldNotContain(name =>
            name.StartsWith("MicroKit.Auth", StringComparison.Ordinal) ||
            name.StartsWith("MicroKit.Tenancy", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
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

    // 5. Application depends only on MicroKit *.Abstractions + Result (no concrete MicroKit modules).
    [Fact]
    public void Application_DependsOnlyOnMicroKitAbstractionsAndResult()
    {
        string[] allowed = ["MicroKit.Auth.Abstractions", "MicroKit.Tenancy.Abstractions", "MicroKit.Result"];

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

    // 7. No EF Core anywhere in Access (no EF in Story #4).
    [Fact]
    public void Access_HasNoEntityFrameworkDependency()
    {
        foreach (var assembly in AccessAssemblies)
        {
            Refs(assembly).ShouldNotContain(name =>
                name.Contains("EntityFrameworkCore", StringComparison.Ordinal) ||
                name.StartsWith("MicroKit.Persistence", StringComparison.Ordinal));
        }
    }

    // 8. Modules do not reference each other: no Access assembly references a SaasBtp.* assembly
    //    outside SaasBtp.Access.* (the Host composes modules; modules must not cross-reference).
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

    // 9. ADR-ARCH-004: no Membership model in Access — no type named Membership*, no *.Membership namespace.
    [Fact]
    public void Access_ContainsNoMembershipModel()
    {
        foreach (var assembly in AccessAssemblies)
        {
            var namedMembership = Types.InAssembly(assembly)
                .That().HaveNameStartingWith("Membership")
                .GetTypes();
            namedMembership.ShouldBeEmpty();

            var inMembershipNamespace = assembly.GetTypes()
                .Where(type => type.Namespace is not null &&
                    type.Namespace.Contains(".Membership", StringComparison.Ordinal))
                .ToArray();
            inMembershipNamespace.ShouldBeEmpty();
        }
    }
}

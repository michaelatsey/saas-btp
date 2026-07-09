using SaasBtp.Site.Application.Features.ResolveCurrentSite;
using SaasBtp.Site.Domain.Context;
using SaasBtp.Site.Domain.Context.Ports;
using SaasBtp.Site.Infrastructure;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// Enforces the hexagonal + modular-monolith boundaries for the Site context, plus the Story A
/// (#17) guardrails: no EF / no DbContext, no Membership model (ADR-ARCH-004 extended to Site), no
/// cross-module reference, and no per-user filtering on the site-scope port. Layer dependency checks
/// read each assembly's manifest (direct references only).
/// </summary>
public sealed class SiteArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(SiteId).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ResolveCurrentSiteHandler).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(SiteModuleExtensions).Assembly;

    private static readonly Assembly[] SiteAssemblies =
        [DomainAssembly, ApplicationAssembly, InfrastructureAssembly];

    private static IReadOnlyCollection<string> Refs(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(name => name.Name!)];

    // 1. Domain does not depend on Application or Infrastructure.
    [Fact]
    public void Domain_DoesNotDependOn_ApplicationOrInfrastructure()
    {
        Refs(DomainAssembly).ShouldNotContain("SaasBtp.Site.Application");
        Refs(DomainAssembly).ShouldNotContain("SaasBtp.Site.Infrastructure");
    }

    // 2. Domain is a pure center: depends on nothing external — no MicroKit, ASP.NET Core, or EF.
    [Fact]
    public void Domain_DependsOnNothingExternal()
    {
        Refs(DomainAssembly).ShouldNotContain(name =>
            name.StartsWith("MicroKit", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            name.Contains("EntityFrameworkCore", StringComparison.Ordinal));
    }

    // 3. Application does not depend on Infrastructure.
    [Fact]
    public void Application_DoesNotDependOn_Infrastructure()
    {
        Refs(ApplicationAssembly).ShouldNotContain("SaasBtp.Site.Infrastructure");
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
        Refs(DomainAssembly).ShouldNotContain("SaasBtp.Site.Infrastructure");
        Refs(ApplicationAssembly).ShouldNotContain("SaasBtp.Site.Infrastructure");
    }

    // 7. No EF Core / DbContext anywhere in Site (no persistence in Story A).
    [Fact]
    public void Site_HasNoEntityFrameworkOrDbContext()
    {
        foreach (var assembly in SiteAssemblies)
        {
            Refs(assembly).ShouldNotContain(name =>
                name.Contains("EntityFrameworkCore", StringComparison.Ordinal) ||
                name.StartsWith("MicroKit.Persistence", StringComparison.Ordinal));

            var dbContexts = assembly.GetTypes()
                .Where(type => type.Name.Contains("DbContext", StringComparison.Ordinal))
                .ToArray();
            dbContexts.ShouldBeEmpty();
        }
    }

    // 8. Modules do not reference each other: no Site assembly references a SaasBtp.* assembly
    //    outside SaasBtp.Site.* (in particular, no reference to the Access module).
    [Fact]
    public void SiteModule_DoesNotReference_OtherModules()
    {
        foreach (var assembly in SiteAssemblies)
        {
            var crossModule = Refs(assembly)
                .Where(name => name.StartsWith("SaasBtp.", StringComparison.Ordinal))
                .Where(name => !name.StartsWith("SaasBtp.Site.", StringComparison.Ordinal))
                .ToArray();

            crossModule.ShouldBeEmpty();
        }
    }

    // 9. ADR-ARCH-004: no Membership model in Site — no type named Membership*, no *.Membership namespace.
    [Fact]
    public void Site_ContainsNoMembershipModel()
    {
        foreach (var assembly in SiteAssemblies)
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

    // 10. No per-user site filtering: the site-scope port takes a tenant identifier and a cancellation
    //     token only — never a user identity (that would require Membership, out of scope in Story A).
    [Fact]
    public void SiteScopeProvider_TakesTenantOnly_NoUserIdentityParameter()
    {
        var method = typeof(ISiteScopeProvider).GetMethod(nameof(ISiteScopeProvider.GetAvailableSitesAsync));
        method.ShouldNotBeNull();

        var parameters = method!.GetParameters();
        parameters.Length.ShouldBe(2);
        parameters[0].ParameterType.ShouldBe(typeof(Guid));
        parameters[1].ParameterType.ShouldBe(typeof(CancellationToken));

        parameters.ShouldNotContain(parameter =>
            parameter.Name!.Contains("user", StringComparison.OrdinalIgnoreCase) ||
            parameter.ParameterType.Name.Contains("User", StringComparison.Ordinal));
    }
}

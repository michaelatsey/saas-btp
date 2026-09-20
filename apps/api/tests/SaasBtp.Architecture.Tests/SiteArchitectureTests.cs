using SaasBtp.Site.Application.Features.ResolveCurrentSite;
using SaasBtp.Site.Domain.Context;
using SaasBtp.Site.Domain.Context.Ports;
using SaasBtp.Site.Infrastructure;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// Enforces the hexagonal + modular-monolith boundaries for the Site context: no EF / no DbContext, no
/// cross-module reference, and dependencies pointing inward. The site-scope port is now a
/// relationship-based authorization check taking explicit (userId, tenantId, siteId) — ADR-ARCH-009
/// supersedes the Story-A (#17) guardrails that forbade a user parameter and a Membership model (Site
/// now legitimately owns the site edge). Layer dependency checks read each assembly's manifest (direct
/// references only).
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

    // 9. Relationship-based authorization (ADR-ARCH-009): the site-scope port answers "may this user act
    //    in this site's scope" from EXPLICIT (userId, tenantId, siteId) arguments plus a cancellation
    //    token — no ambient/header/claim resolution (ADR-ARCH-005). This REPLACES the Story-A tenant-only
    //    catalogue signature, and inverts the superseded ADR-ARCH-004 rule that forbade a user parameter.
    //    (The old "no Membership model in Site" guardrail is retired with ADR-ARCH-004: Site now owns the
    //    site edge, read by SiteMembershipScopeProvider.)
    [Fact]
    public void SiteScopeProvider_TakesUserTenantAndSite_ForRelationshipAuthorization()
    {
        var method = typeof(ISiteScopeProvider).GetMethod(nameof(ISiteScopeProvider.CanActInSiteScopeAsync));
        method.ShouldNotBeNull();

        method!.ReturnType.ShouldBe(typeof(Task<bool>));

        var parameters = method.GetParameters();
        parameters.Length.ShouldBe(4);
        parameters[0].ParameterType.ShouldBe(typeof(Guid));
        parameters[0].Name.ShouldBe("userId");
        parameters[1].ParameterType.ShouldBe(typeof(Guid));
        parameters[1].Name.ShouldBe("tenantId");
        parameters[2].ParameterType.ShouldBe(typeof(Guid));
        parameters[2].Name.ShouldBe("siteId");
        parameters[3].ParameterType.ShouldBe(typeof(CancellationToken));

        // The user identity is now REQUIRED — the exact inverse of the superseded Story-A guardrail.
        parameters.ShouldContain(parameter =>
            parameter.Name!.Contains("user", StringComparison.OrdinalIgnoreCase));
    }
}

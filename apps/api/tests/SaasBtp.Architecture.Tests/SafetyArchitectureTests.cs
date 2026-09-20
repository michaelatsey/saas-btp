using MicroKit.Domain.Events;
using SaasBtp.Safety.Domain.Constats;
using SaasBtp.Safety.Infrastructure;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// Enforces the hexagonal + modular-monolith boundaries for the Safety context and the Story 1
/// guardrails (safety-domain-model.md §8, amended 2026-07-10): the Domain builds on the shared DDD
/// kernel (MicroKit.Domain + MicroKit.Result) ONLY — no infrastructure, no other bounded context —
/// no forbidden treatment/Membership/Media types, and no domain event this story. Layer dependency
/// checks read each assembly's manifest (direct references only).
/// </summary>
public sealed class SafetyArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Constat).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(SafetyModuleExtensions).Assembly;

    private static readonly Assembly[] SafetyAssemblies = [DomainAssembly, InfrastructureAssembly];

    private static IReadOnlyCollection<string> Refs(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(name => name.Name!)];

    // 1. Domain does not depend on Infrastructure (dependencies point inward).
    [Fact]
    public void Domain_DoesNotDependOn_Infrastructure()
    {
        Refs(DomainAssembly).ShouldNotContain("SaasBtp.Safety.Infrastructure");
    }

    // 2. Amended §8: the Domain depends on the shared DDD kernel ONLY — exactly MicroKit.Domain and
    //    MicroKit.Result among MicroKit packages (unlike Site/Access, which reference no MicroKit).
    [Fact]
    public void Domain_DependsOnly_OnMicroKitDomainAndResult()
    {
        var microkitRefs = Refs(DomainAssembly)
            .Where(name => name.StartsWith("MicroKit", StringComparison.Ordinal))
            .ToArray();

        microkitRefs.ShouldBe(["MicroKit.Domain", "MicroKit.Result"], ignoreOrder: true);
    }

    // 3. Amended §8: the Domain references NO infrastructure — no EF Core, ASP.NET Core, or the
    //    outbound MicroKit modules (Persistence / AspNetCore / Messaging).
    [Fact]
    public void Domain_HasNoInfrastructureDependency()
    {
        Refs(DomainAssembly).ShouldNotContain(name =>
            name.Contains("EntityFrameworkCore", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            name.StartsWith("MicroKit.Persistence", StringComparison.Ordinal) ||
            name.StartsWith("MicroKit.AspNetCore", StringComparison.Ordinal) ||
            name.StartsWith("MicroKit.Messaging", StringComparison.Ordinal));
    }

    // 4. No cross-context reference: no Safety assembly references a SaasBtp.* assembly outside
    //    SaasBtp.Safety.* (in particular, not Site, Access, CorrectiveActions, Media, Notifications).
    [Fact]
    public void SafetyModule_DoesNotReference_OtherBoundedContexts()
    {
        foreach (var assembly in SafetyAssemblies)
        {
            var crossContext = Refs(assembly)
                .Where(name => name.StartsWith("SaasBtp.", StringComparison.Ordinal))
                .Where(name => !name.StartsWith("SaasBtp.Safety.", StringComparison.Ordinal))
                .ToArray();

            crossContext.ShouldBeEmpty();
        }
    }

    // 5. §8 anti-corrective / anti-Membership / anti-Media: none of these concepts leak into Safety
    //    as a type name (treatment lives in CorrectiveActions; media is referenced by id only, and
    //    not at all this story; Membership is deferred).
    [Fact]
    public void Safety_ContainsNoForbiddenTreatmentMembershipOrMediaTypes()
    {
        string[] forbiddenPrefixes =
            ["Responsable", "Assignee", "Deadline", "Validation", "Closure", "Membership", "Media", "Evidence", "Photo"];

        foreach (var assembly in SafetyAssemblies)
        {
            var offendingNames = assembly.GetTypes()
                .Where(type => forbiddenPrefixes.Any(prefix =>
                    type.Name.StartsWith(prefix, StringComparison.Ordinal)))
                .ToArray();
            offendingNames.ShouldBeEmpty();

            // No status/lifecycle concept on a constat this story (existence means recorded, §9).
            var statusTypes = assembly.GetTypes()
                .Where(type => type.Name.Contains("Status", StringComparison.Ordinal) ||
                    type.Name.Contains("Statut", StringComparison.Ordinal))
                .ToArray();
            statusTypes.ShouldBeEmpty();

            var forbiddenNamespaces = assembly.GetTypes()
                .Where(type => type.Namespace is not null && (
                    type.Namespace.Contains(".Membership", StringComparison.Ordinal) ||
                    type.Namespace.Contains(".Media", StringComparison.Ordinal) ||
                    type.Namespace.Contains(".Corrective", StringComparison.Ordinal)))
                .ToArray();
            forbiddenNamespaces.ShouldBeEmpty();
        }
    }

    // 6. §8 no event emission this story: the Safety domain defines no IDomainEvent type (the
    //    SafetyConstatRaised seam arrives with the CorrectiveActions slice, not here).
    [Fact]
    public void SafetyDomain_DefinesNoDomainEvent()
    {
        var domainEvents = DomainAssembly.GetTypes()
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type) && type != typeof(IDomainEvent))
            .ToArray();

        domainEvents.ShouldBeEmpty();
    }
}

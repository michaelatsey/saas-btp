using System.Reflection;
using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using Mono.Cecil;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchType = ArchUnitNET.Domain.IType;

namespace MicroKit.Domain.Architecture.Tests;

public sealed class DomainArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Entity<>).Assembly;

    private static readonly string[] ForbiddenFrameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.DependencyInjection",
    ];

    [Fact]
    public void Domain_ReferencesNoForbiddenAssembly()
    {
        using var module = ModuleDefinition.ReadModule(DomainAssembly.Location);

        var references = module.AssemblyReferences.Select(a => a.Name).ToArray();
        var forbidden = references
            .Where(name => name.StartsWith("MicroKit.", StringComparison.Ordinal)
                           || ForbiddenFrameworks.Any(f => name.StartsWith(f, StringComparison.Ordinal)))
            .ToArray();

        Assert.Contains("System.Runtime", references);
        Assert.Empty(forbidden);
    }

    [Fact]
    public void Domain_TypesDependOnNoForbiddenFramework()
    {
        var result = Evaluate(DomainAssembly, Types().That().ResideInAssembly(DomainAssembly).Should()
            .FollowCustomCondition(
                type => !type.Dependencies.Any(d => ForbiddenFrameworks.Any(f => d.Target.FullName.StartsWith(f, StringComparison.Ordinal))),
                "depend on no forbidden framework",
                "depends on a forbidden framework"));

        Assert.Null(result.FailingTypeNames);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Domain_DeclaresNoPersistenceContract()
    {
        // MicroKit.Persistence.Abstractions owns repositories and the unit of work (MicroKit ADR-003 D1 (michaelatsey/microkit@a12097b)).
        // The names are read with Cecil, never resolved to runtime types: a coverage collector
        // injects types into the instrumented assembly that reflection cannot resolve.
        using var module = ModuleDefinition.ReadModule(DomainAssembly.Location);
        var names = module.GetTypes().Select(t => t.FullName).ToArray();

        Assert.Contains(typeof(Entity<>).FullName!, names);
        Assert.DoesNotContain(names, name => name.StartsWith("MicroKit.Domain.Repositories", StringComparison.Ordinal)
                                             || name.Contains(".IRepository", StringComparison.Ordinal)
                                             || name.Contains(".IReadRepository", StringComparison.Ordinal)
                                             || name.Contains(".IUnitOfWork", StringComparison.Ordinal));
    }

    [Fact]
    public void GuardedMembers_HavePrivateSetters()
    {
        // A derived type must not be able to overwrite what the constructors guard (#166).
        System.Reflection.PropertyInfo[] properties =
        [
            typeof(Entity<GuidId>).GetProperty(nameof(Entity<GuidId>.Id))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.CreatedAt))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.CreatedBy))!,
            typeof(AuditableAggregateRoot<GuidId>).GetProperty(nameof(IAuditableEntity.CreatedAt))!,
            typeof(AuditableAggregateRoot<GuidId>).GetProperty(nameof(IAuditableEntity.CreatedBy))!,
        ];

        Assert.Empty(properties.Where(p => p.SetMethod is not { IsPrivate: true })
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}"));
    }

    [Theory]
    [InlineData("MicroKit.Domain.Aggregates.AuditableEntity`1")]
    [InlineData("MicroKit.Domain.Aggregates.AuditableAggregateRoot`1")]
    public void AuditableBases_DelegateMutateToAuditRules(string typeName)
    {
        // The Mutate sequence lives once, in AuditRules (#179); a base must not grow its own copy.
        using var module = ModuleDefinition.ReadModule(DomainAssembly.Location);
        var mutate = module.GetType(typeName).Methods.Single(m => m.Name == "Mutate");

        Assert.False(mutate.Body.HasExceptionHandlers);
        Assert.Contains(mutate.Body.Instructions
            .Select(i => i.Operand)
            .OfType<MethodReference>(), m => m.DeclaringType.FullName == "MicroKit.Domain.Aggregates.AuditRules" && m.Name == "Mutate");
    }

    [Fact]
    public void ValueObjects_AreSealed()
    {
        var result = ValueObjectsAreSealed(DomainAssembly);

        Assert.Null(result.FailingTypeNames);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void DomainEvents_AreRecords()
    {
        var result = DomainEventsAreRecords(DomainAssembly);

        Assert.Null(result.FailingTypeNames);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void DomainEvents_RuleCoversTheDomainEventBase()
    {
        var architecture = Load(DomainAssembly);

        Assert.Contains(Types().That().ResideInAssembly(DomainAssembly).And().ImplementInterface(typeof(IDomainEvent))
            .GetObjects(architecture), t => t.FullName == typeof(DomainEvent).FullName);
    }

    // The two tests below run each rule against this test assembly, whose fixtures break it on
    // purpose, to prove the rule can fail.

    [Fact]
    public void ValueObjectRule_RejectsAnUnsealedValueObject()
    {
        var result = ValueObjectsAreSealed(typeof(DomainArchitectureTests).Assembly);

        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.FailingTypeNames);
        Assert.Equal([typeof(UnsealedValueObject).FullName], result.FailingTypeNames);
    }

    [Fact]
    public void DomainEventRule_RejectsANonRecordEvent()
    {
        var result = DomainEventsAreRecords(typeof(DomainArchitectureTests).Assembly);

        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.FailingTypeNames);
        Assert.Equal([typeof(ClassEvent).FullName], result.FailingTypeNames);
    }

    // Rewritten on ArchUnitNET (ADR-ARCH-035 §7). A struct is sealed, as NetArchTest's BeSealed
    // held: ArchUnitNET's BeSealed applies to classes only.
    private static RuleResult ValueObjectsAreSealed(Assembly assembly) =>
        Evaluate(assembly, Types().That().ResideInAssembly(assembly).And().ImplementInterface(typeof(IValueObject))
            .Should().FollowCustomCondition(
                type => type is not ArchUnitNET.Domain.Class { IsSealed: not true },
                "be sealed",
                "is not sealed"));

    private static RuleResult DomainEventsAreRecords(Assembly assembly) =>
        Evaluate(assembly, Types().That().ResideInAssembly(assembly).And().ImplementInterface(typeof(IDomainEvent))
            .Should().FollowCustomCondition(IsRecord, "be a record", "is not a record"));

    // A record class: ArchUnitNET's IsRecord (it reads the compiler's unspeakable <Clone>$, which
    // it leaves out of the members). A record struct: the compiler-generated PrintMembers.
    private static bool IsRecord(ArchType type) =>
        type is ArchUnitNET.Domain.Class { IsRecord: true }
        || (type is ArchUnitNET.Domain.Struct && type.Members.Any(m =>
            m.Name.StartsWith("PrintMembers(", StringComparison.Ordinal)
            && m.Attributes.Any(a => a.FullName == typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).FullName)));

    // The assembly under test with MicroKit.Domain, so that the domain interfaces resolve.
    private static ArchUnitNET.Domain.Architecture Load(Assembly assembly) =>
        new ArchLoader().LoadAssemblies(assembly == DomainAssembly ? [assembly] : [assembly, DomainAssembly]).Build();

    // NetArchTest's result shape, which the assertions above keep: no failing type names on success.
    private static RuleResult Evaluate(Assembly assembly, ArchUnitNET.Fluent.Syntax.Elements.Types.TypesShouldConjunction rule)
    {
        var failing = rule.Evaluate(Load(assembly))
            .Where(r => !r.Passed)
            .Select(r => r.EvaluatedObject is ArchType type ? type.FullName : r.Description)
            .ToArray();
        return new RuleResult(failing.Length == 0, failing.Length == 0 ? null : failing);
    }

    private sealed record RuleResult(bool IsSuccessful, string[]? FailingTypeNames);
}

public sealed record SealedValueObject(int Value) : IValueObject;

public readonly record struct StructValueObject(int Value) : IValueObject;

#pragma warning disable CA1852 // deliberately unsealed: the fixture the sealed rule must reject
public class UnsealedValueObject : IValueObject;
#pragma warning restore CA1852

public sealed record RecordEvent(DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public readonly record struct RecordStructEvent(Guid EventId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class ClassEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UnixEpoch;
}

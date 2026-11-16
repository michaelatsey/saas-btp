using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

// Rewritten from NetArchTest on ArchUnitNET (ADR-ARCH-035 §7). NetArchTest's ResideInNamespace()
// was a prefix match; InNamespaceOrBelow keeps it. All Abstractions types use the flat root
// namespace, so there it behaves as an exact match.

namespace MicroKit.Persistence.Architecture.Tests;

public sealed class NamespaceConventionTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(MicroKit.Persistence.Abstractions.IUnitOfWork).Assembly;

    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly;

    private static readonly Assembly EfCoreAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly;

    private static readonly Assembly PostgreSqlAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlEfCoreBuilderExtensions).Assembly;

    [Fact]
    public void AbstractionsPublicTypes_ResideInCorrectNamespace()
    {
        Assert.Empty(PublicTypesOutside(AbstractionsAssembly, "MicroKit.Persistence.Abstractions"));
    }

    [Fact]
    public void CorePublicTypes_ResideInCorrectNamespace()
    {
        Assert.Empty(PublicTypesOutside(CoreAssembly, "MicroKit.Persistence"));
    }

    [Fact]
    public void EntityFrameworkCorePublicTypes_ResideInCorrectNamespace()
    {
        Assert.Empty(PublicTypesOutside(EfCoreAssembly, "MicroKit.Persistence.EntityFrameworkCore"));
    }

    [Fact]
    public void PostgreSqlPublicTypes_ResideInCorrectNamespace()
    {
        Assert.Empty(PublicTypesOutside(PostgreSqlAssembly, "MicroKit.Persistence.EntityFrameworkCore.PostgreSql"));
    }

    [Fact]
    public void ExtensionClasses_InCoreAssembly_AreStatic()
    {
        var failing = Failing(CoreAssembly, Types().That().ResideInAssembly(CoreAssembly).And().HaveNameEndingWith("Extensions")
            .Should().FollowCustomCondition(IsStatic, "be static", "is not static"));

        Assert.Empty(failing);
    }

    private static string[] PublicTypesOutside(Assembly assembly, string ns) =>
        Failing(assembly, Types().That().ResideInAssembly(assembly).And().ArePublic()
            .Should().FollowCustomCondition(t => InNamespaceOrBelow(t, ns), "reside in " + ns, "resides elsewhere"));

    // A static class is abstract and sealed in IL.
    private static bool IsStatic(ArchUnitNET.Domain.IType type) =>
        type is ArchUnitNET.Domain.Class { IsAbstract: true, IsSealed: true };

    private static bool InNamespaceOrBelow(ArchUnitNET.Domain.IType type, string ns) =>
        type.Namespace.FullName == ns || type.Namespace.FullName.StartsWith(ns + ".", StringComparison.Ordinal);

    // The full names of the types that break the rule.
    private static string[] Failing(Assembly assembly, TypesShouldConjunction rule) =>
        rule.Evaluate(new ArchLoader().LoadAssemblies(assembly).Build())
            .Where(r => !r.Passed)
            .Select(r => r.EvaluatedObject is ArchUnitNET.Domain.IType type ? type.FullName : r.Description)
            .ToArray();
}

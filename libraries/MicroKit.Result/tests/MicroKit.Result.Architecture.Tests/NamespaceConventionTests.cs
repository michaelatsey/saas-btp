using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

// Rewritten from NetArchTest on ArchUnitNET (ADR-ARCH-035 §7). NetArchTest's ResideInNamespace()
// was a prefix match, so "MicroKit.Result" also covered sub-namespaces such as
// MicroKit.Result.Serialization; InNamespaceOrBelow keeps that.

namespace MicroKit.Result.Architecture.Tests;

public sealed class NamespaceConventionTests
{
    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Result.Result).Assembly;

    private static readonly Assembly AspNetCoreAssembly =
        typeof(MicroKit.Result.AspNetCore.ResultHttpExtensions).Assembly;

    [Fact]
    public void CorePublicTypes_ResideInCorrectNamespace()
    {
        var failing = Failing(CoreAssembly, Types().That().ResideInAssembly(CoreAssembly).And().ArePublic()
            .Should().FollowCustomCondition(t => InNamespaceOrBelow(t, "MicroKit.Result"), "reside in MicroKit.Result", "resides elsewhere"));

        Assert.Empty(failing);
    }

    [Fact]
    public void AspNetCorePublicTypes_ResideInCorrectNamespace()
    {
        var failing = Failing(AspNetCoreAssembly, Types().That().ResideInAssembly(AspNetCoreAssembly).And().ArePublic()
            .Should().FollowCustomCondition(t => InNamespaceOrBelow(t, "MicroKit.Result.AspNetCore"), "reside in MicroKit.Result.AspNetCore", "resides elsewhere"));

        Assert.Empty(failing);
    }

    [Fact]
    public void ExtensionClasses_InCoreAssembly_AreStatic()
    {
        var failing = Failing(CoreAssembly, Types().That().ResideInAssembly(CoreAssembly).And().HaveNameEndingWith("Extensions")
            .Should().FollowCustomCondition(IsStatic, "be static", "is not static"));

        Assert.Empty(failing);
    }

    [Fact]
    public void ExtensionClasses_InAspNetCoreAssembly_AreStatic()
    {
        var failing = Failing(AspNetCoreAssembly, Types().That().ResideInAssembly(AspNetCoreAssembly).And().HaveNameEndingWith("Extensions")
            .Should().FollowCustomCondition(IsStatic, "be static", "is not static"));

        Assert.Empty(failing);
    }

    [Fact]
    public void ResultFactory_IsStatic()
    {
        // ResultFactory is a pure static helper — never instantiated.
        // Note: ResultJsonConverterFactory is NOT static; it extends JsonConverterFactory and must be
        // instantiated by the JSON serializer infrastructure. No blanket "Factory == static" rule applies here.
        var t = typeof(ResultFactory);
        Assert.NotEqual((TypeAttributes)0, (t.Attributes & TypeAttributes.Abstract));
        Assert.NotEqual((TypeAttributes)0, (t.Attributes & TypeAttributes.Sealed));
    }

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

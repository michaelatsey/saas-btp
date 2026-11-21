using System.Reflection;
using System.Runtime.CompilerServices;

namespace SaasBtp.Access.Domain.Tests;

/// <summary>
/// Reflection over the members a domain type declares itself, compiler-generated ones skipped,
/// for the tests that prove the domain offers no operation it must not offer.
/// </summary>
internal static class Members
{
    public const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Every constructor and method <paramref name="type"/> declares, public or not.</summary>
    public static IEnumerable<MethodBase> Callables(Type type) =>
        type.GetConstructors(Declared).Cast<MethodBase>()
            .Concat(type.GetMethods(Declared))
            .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false));

    /// <summary>
    /// The instance methods <paramref name="type"/> declares that code outside it can call, other
    /// than property getters: what could change its state.
    /// </summary>
    public static IEnumerable<string> Operations(Type type) =>
        type.GetMethods(Declared)
            .Where(method => !method.IsStatic && !method.IsPrivate)
            .Where(method => !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(method => !(method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal)))
            .Where(method => method.GetBaseDefinition().DeclaringType == type)
            .Select(method => $"{type.Name}.{method.Name}");

    /// <summary>Whether the property can be set from outside the type (a setter or init that is not private).</summary>
    public static bool IsSettableOutside(PropertyInfo property) =>
        property.SetMethod is { IsPrivate: false };
}

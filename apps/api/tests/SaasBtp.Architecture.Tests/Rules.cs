using System.Reflection;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace SaasBtp.Architecture.Tests;

/// <summary>The vocabulary the rules share: assemblies, type names and the packages they name.</summary>
internal static class Rules
{
    /// <summary>
    /// The data access, web and hosting namespaces and packages of ADR-ARCH-034 §7, matched as
    /// prefixes (<c>Npgsql</c> covers <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>).
    /// </summary>
    public static readonly string[] Infrastructure =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.DependencyInjection",
    ];

    /// <summary>The ASP.NET Core shared framework, the form the web packages take in .NET 10.</summary>
    public const string AspNetCoreFramework = "Microsoft.AspNetCore.App";

    public static bool StartsWithAny(string name, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>The types of the given assemblies, as an ArchUnitNET selection.</summary>
    public static GivenTypesConjunction TypesIn(IEnumerable<System.Reflection.Assembly> assemblies)
    {
        var list = assemblies.ToArray();
        return Types().That().ResideInAssembly(list[0], list[1..]);
    }

    public static IEnumerable<System.Reflection.Assembly> ModuleAssemblies =>
        Solution.Modules.SelectMany(module => module.Layers).Select(layer => layer.Assembly);

    public static IEnumerable<System.Reflection.Assembly> HostAssemblies =>
        Solution.Hosts.Select(host => host.Assembly);

    /// <summary>The simple name of the assembly a type belongs to.</summary>
    public static string AssemblyOf(IType type) => type.Assembly.FullName.Split(',')[0];

    /// <summary>The module whose assemblies hold <paramref name="type"/>, if any.</summary>
    public static Module? ModuleOf(IType type)
    {
        var assembly = AssemblyOf(type);
        return Solution.Modules.FirstOrDefault(module => module.Layers.Any(layer => layer.Name == assembly));
    }

    public static IEnumerable<IType> DependencyTargets(IType type) =>
        type.Dependencies.Select(dependency => dependency.Target);

    /// <summary>
    /// The simple names of a type and of the types it is nested in, outermost first, without
    /// generic arity: a closure the compiler generates inside an endpoint belongs to the endpoint.
    /// </summary>
    public static IEnumerable<string> NameChain(IType type)
    {
        var name = type.Namespace.FullName.Length == 0 || !type.FullName.StartsWith(type.Namespace.FullName + ".", StringComparison.Ordinal)
            ? type.FullName
            : type.FullName[(type.Namespace.FullName.Length + 1)..];
        return name.Split('+', '/').Select(part => part.Split('`')[0]);
    }

    public static bool IsInModulesDirectory(string path) =>
        Path.GetFullPath(path).StartsWith(Solution.ModulesDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    public static string Relative(string path) => Path.GetRelativePath(Solution.RepositoryRoot, path);

    public static BindingFlags PublicStatic => BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
}

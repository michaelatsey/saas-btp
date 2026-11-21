using System.Reflection;
using System.Text.RegularExpressions;
using ArchUnitNET.Loader;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// The solution as the architecture tests see it, built once: the modules found by listing the
/// folders under <c>apps/api/src/modules/</c> and the hosts under <c>apps/api/src/hosts/</c>
/// (ADR-ARCH-034 §7), their project files in the working tree, and their assemblies as built
/// into this project's output. A module that lacks one of its three projects, or whose assemblies
/// this project does not load, fails every test that reads the solution.
/// </summary>
internal static partial class Solution
{
    private static readonly Lazy<Loaded> Instance = new(Load);

    public static string RepositoryRoot => Instance.Value.Root;

    public static string ModulesDirectory => Path.Combine(RepositoryRoot, "apps", "api", "src", "modules");

    public static IReadOnlyList<Module> Modules => Instance.Value.Modules;

    public static IReadOnlyList<Host> Hosts => Instance.Value.Hosts;

    /// <summary>One ArchUnitNET architecture over every module and host assembly.</summary>
    public static ArchUnitNET.Domain.Architecture Architecture => Instance.Value.Architecture;

    private static Loaded Load()
    {
        var root = Path.GetFullPath(typeof(Solution).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepositoryRoot")
            .Value!);

        var modulesDirectory = Path.Combine(root, "apps", "api", "src", "modules");
        var contexts = Directory.Exists(modulesDirectory)
            ? Directory.GetDirectories(modulesDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList()
            : [];
        if (contexts.Count == 0)
        {
            throw new InvalidOperationException($"No module folder under {modulesDirectory}.");
        }

        // Loaded means referenced by this project's file, not only present in its output, where a
        // module's assemblies can outlive the reference that brought them.
        var testProject = Path.Combine(root, "apps", "api", "tests", "SaasBtp.Architecture.Tests", "SaasBtp.Architecture.Tests.csproj");
        var referenced = ProjectFile.ProjectReferences(testProject).ToHashSet(StringComparer.Ordinal);

        var modules = contexts.Select(context => LoadModule(modulesDirectory, context!, referenced)).ToList();

        var hostsDirectory = Path.Combine(root, "apps", "api", "src", "hosts");
        var hosts = Directory.GetDirectories(hostsDirectory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                var message = $"Host {name} is not loaded by SaasBtp.Architecture.Tests: reference its project.";
                var project = RequireFile(Path.Combine(hostsDirectory, name!, $"{name}.csproj"), $"Host {name} has no {name}.csproj.");
                return referenced.Contains(project)
                    ? new Host(name!, project, LoadAssembly(name!, message))
                    : throw new InvalidOperationException(message);
            })
            .ToList();

        var assemblies = modules.SelectMany(module => module.Layers)
            .Select(layer => layer.Assembly)
            .Concat(hosts.Select(host => host.Assembly))
            .ToArray();

        return new Loaded(root, modules, hosts, new ArchLoader().LoadAssemblies(assemblies).Build());
    }

    private static Module LoadModule(string modulesDirectory, string context, HashSet<string> referenced)
    {
        var directory = Path.Combine(modulesDirectory, context);
        string[] suffixes = ["Domain", "Features", "Adapters"];
        string Name(string suffix) => $"SaasBtp.{context}.{suffix}";
        string Project(string suffix) => Path.Combine(directory, Name(suffix), $"{Name(suffix)}.csproj");

        // Each of the three projects first, so that a missing one is named as missing.
        foreach (var suffix in suffixes)
        {
            RequireFile(Project(suffix), $"Module {context} lacks its project {Name(suffix)} (ADR-ARCH-034 §1).");
        }

        Layer Layer(string suffix)
        {
            var name = Name(suffix);
            var project = Project(suffix);
            if (!referenced.Contains(project))
            {
                throw new InvalidOperationException(
                    $"Module {context} is not loaded by SaasBtp.Architecture.Tests: reference {name} from its project file.");
            }

            var assembly = LoadAssembly(
                name,
                $"Module {context} is not loaded by SaasBtp.Architecture.Tests: reference {name} from its project file.");
            return new Layer(name, project, assembly);
        }

        return new Module(context, directory, Schema(context), Layer(suffixes[0]), Layer(suffixes[1]), Layer(suffixes[2]));
    }

    // A module's schema is its bounded-context name in snake_case (sql.md).
    private static string Schema(string context) => UpperCaseBoundary().Replace(context, "$1_$2").ToLowerInvariant();

    private static string RequireFile(string path, string message) =>
        File.Exists(path) ? path : throw new InvalidOperationException(message);

    private static Assembly LoadAssembly(string name, string message)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{name}.dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : throw new InvalidOperationException(message);
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex UpperCaseBoundary();

    private sealed record Loaded(string Root, IReadOnlyList<Module> Modules, IReadOnlyList<Host> Hosts, ArchUnitNET.Domain.Architecture Architecture);
}

/// <summary>A module, <c>apps/api/src/modules/&lt;Context&gt;/</c>, and its three projects.</summary>
internal sealed record Module(string Context, string Directory, string Schema, Layer Domain, Layer Features, Layer Adapters)
{
    public IEnumerable<Layer> Layers => [Domain, Features, Adapters];

    /// <summary>The name of the module's entry point, <c>&lt;Context&gt;Module</c> (ADR-ARCH-034 §2).</summary>
    public string EntryPointFullName => $"{Adapters.Name}.{Context}Module";

    /// <summary>Every project file under the module's folder, its tests included.</summary>
    public IEnumerable<string> ProjectFiles =>
        System.IO.Directory.EnumerateFiles(Directory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    /// <summary>The project files under the module's <c>tests/</c> folder (ADR-ARCH-034 §6).</summary>
    public IEnumerable<string> TestProjectFiles =>
        ProjectFiles.Where(path => path.StartsWith(Path.Combine(Directory, "tests") + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    public bool Contains(string path) =>
        Path.GetFullPath(path).StartsWith(Directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}

/// <summary>One of a module's three projects.</summary>
internal sealed record Layer(string Name, string ProjectFile, Assembly Assembly);

/// <summary>A host under <c>apps/api/src/hosts/</c>.</summary>
internal sealed record Host(string Name, string ProjectFile, Assembly Assembly);

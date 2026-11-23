using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static SaasBtp.Architecture.Tests.Rules;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// How the repository holds and uses its copy of MicroKit (ADR-ARCH-035 §9): no MicroKit package,
/// no nested build settings, copied test projects on the stack of ADR-ARCH-021, the persistence
/// analyzer on every module and on no test project, and every module context registered through
/// the copied PostgreSQL provider. The project-file rules read the files as written in the working
/// tree (<see cref="Solution.SourceFiles"/>); they evaluate no condition and no import, and they
/// do not read a folder that holds a <c>.git</c> entry, so a library copied with its clone's
/// <c>.git</c> folder is not read.
/// </summary>
public sealed class MicroKitCopyTests
{
    private const string PostgreSqlProvider = "MicroKit.Persistence.EntityFrameworkCore.PostgreSql";

    /// <summary>The options extension <c>UseMicroKitPersistence</c> adds; internal to the copied library.</summary>
    private const string PersistenceOptionsExtension = "MicroKit.Persistence.EntityFrameworkCore.MicroKitPersistenceOptionsExtension";

    /// <summary>The interceptors the copied provider's <c>UsePostgreSQL</c> adds (ADR-ARCH-035 §8); internal to it.</summary>
    private static readonly string[] ProviderInterceptors =
    [
        "NoAutomaticTransactionInterceptor",
        "SubjectTransactionInterceptor",
        "UsedTransactionInterceptor",
        "TransactionRequiredCommandInterceptor",
    ];

    /// <summary>The packages of the VSTest platform ADR-ARCH-035 §7 removes from the copied tests (ADR-ARCH-021 §3).</summary>
    private static readonly string[] VsTestPackages = ["Microsoft.NET.Test.Sdk", "xunit.runner.visualstudio", "coverlet.collector"];

    /// <summary>Assertion libraries known by name (ADR-ARCH-021 §2), so that a breach names them as such.</summary>
    private static readonly string[] AssertionLibraries = ["Shouldly", "FluentAssertions", "AwesomeAssertions", "NFluent"];

    /// <summary>
    /// The packages a copied test project may reference: exactly those they reference today, none of
    /// them a VSTest package nor an assertion library. No list of forbidden names can be complete, so
    /// a package is added here only once a review has found it to be neither.
    /// </summary>
    private static readonly string[] CopiedTestPackages =
    [
        "xunit.v3",
        "FsCheck.Xunit.v3",
        "TngTech.ArchUnitNET.xUnitV3",
        "Mono.Cecil",
        "Microsoft.CodeAnalysis.CSharp.Analyzer.Testing",
        "Microsoft.CodeAnalysis.CSharp.Workspaces",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.Extensions.TimeProvider.Testing",
        "Npgsql",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "SQLitePCLRaw.bundle_e_sqlite3",
        "SQLitePCLRaw.core",
        "SQLitePCLRaw.lib.e_sqlite3",
        "SQLitePCLRaw.provider.e_sqlite3",
        "Testcontainers.PostgreSql",
    ];

    private static readonly string[] PackageReferenceItems = ["PackageReference", "GlobalPackageReference"];

    private static readonly string[] MsBuildFilePatterns = ["*.csproj", "*.props", "*.targets"];

    /// <summary>The metadata that make a project reference an analyzer reference (ADR-ARCH-035 §5).</summary>
    private static readonly string[] AnalyzerReferenceMetadata = ["OutputItemType", "ReferenceOutputAssembly"];

    private static string AnalyzerProject => Path.Combine(
        Solution.LibrariesDirectory, "MicroKit.Persistence", "src", "MicroKit.Persistence.Analyzers", "MicroKit.Persistence.Analyzers.csproj");

    /// <summary>
    /// Reads every <c>PackageReference</c> and <c>GlobalPackageReference</c> of every project,
    /// <c>.props</c> and <c>.targets</c> file, by its <c>Include</c> and its <c>Update</c>. A
    /// package id is compared case-insensitively, as NuGet compares it; a value holding an MSBuild
    /// expression (<c>$(</c>, <c>@(</c>, <c>%(</c>) is a breach whatever it would expand to, since
    /// it cannot be read without MSBuild.
    /// </summary>
    [Fact]
    public void NoMicroKitPackageReference()
    {
        var breaches = MsBuildFiles()
            .SelectMany(file => PackageReferenceItems
                .SelectMany(item => ProjectFile.Items(file, item)
                    .SelectMany(reference => reference.Include.Concat(reference.Update))
                    .Select(package => IsMicroKitPackage(package)
                        ? $"{Relative(file)} has a {item} to {package}"
                        : IsExpression(package)
                            ? $"{Relative(file)} has a {item} whose package is the expression {package}, which this test cannot read"
                            : null)))
            .OfType<string>()
            .ToList();

        Breaches.None(breaches);
    }

    /// <summary>
    /// Reports every <c>Directory.*.props</c> below the root. A nested <c>Directory.*.targets</c> is
    /// not reported: the rule names the props files (ADR-ARCH-035 §6, §9).
    /// </summary>
    [Fact]
    public void NoNestedBuildProps()
    {
        var nested = Solution.SourceFiles("Directory.*.props")
            .Where(file => Path.GetDirectoryName(file) != Solution.RepositoryRoot)
            .Select(file => $"{Relative(file)} is a build settings file below the root")
            .ToList();

        Breaches.None(nested);
    }

    /// <summary>
    /// Reads the <c>PackageReference</c> items of every project file under a library's
    /// <c>tests/</c> folder: each must name a package of <see cref="CopiedTestPackages"/>. A package
    /// added by a <c>.props</c> or <c>.targets</c> file, such as a <c>GlobalPackageReference</c>, is
    /// not read: the rule reads the project files (ADR-ARCH-035 §9).
    /// </summary>
    [Fact]
    public void TestProjectsFollowAdr021()
    {
        var forbidden = VsTestPackages.Concat(AssertionLibraries).ToList();

        // The allow-list holds no package the rule forbids.
        Breaches.None(CopiedTestPackages
            .Where(package => forbidden.Contains(package, StringComparer.OrdinalIgnoreCase))
            .Select(package => $"{package} is allowed to the copied test projects and forbidden by ADR-ARCH-021")
            .ToList());

        var breaches = Solution.SourceFiles("*.csproj")
            .Where(IsCopiedTestProject)
            .SelectMany(project => ProjectFile.Items(project, "PackageReference")
                .SelectMany(reference => reference.Include)
                .Select(package =>
                    VsTestPackages.Contains(package, StringComparer.OrdinalIgnoreCase)
                        ? $"{Relative(project)} references the VSTest package {package}"
                    : AssertionLibraries.Contains(package, StringComparer.OrdinalIgnoreCase)
                        ? $"{Relative(project)} references the assertion library {package}"
                    : CopiedTestPackages.Contains(package, StringComparer.OrdinalIgnoreCase)
                        ? null
                        : $"{Relative(project)} references {package}, which is not a known test package: add it to "
                            + $"{nameof(CopiedTestPackages)} only once it is found to be neither a VSTest package nor an assertion library"))
            .OfType<string>()
            .ToList();

        Breaches.None(breaches);
    }

    /// <summary>
    /// Reads the <c>ProjectReference</c> and <c>Analyzer</c> items of project files: each module's
    /// Adapters project references the analyzer with <c>OutputItemType="Analyzer"</c> and
    /// <c>ReferenceOutputAssembly="false"</c> (ADR-ARCH-035 §5); no project whose name ends in
    /// <c>Tests</c> (ADR-ARCH-021 §5) references it as an analyzer, by its path, by its file name, or
    /// by a path holding an MSBuild expression the test cannot read; an <c>Analyzer</c> item naming
    /// it, or holding an expression, is a breach there. A plain project reference, by which
    /// <c>MicroKit.Persistence.Analyzers.Tests</c> tests the analyzer, is allowed. In both kinds of
    /// project, what would change the reference after its <c>Include</c> is a breach: a
    /// <c>ProjectReference</c> <c>Update</c> or <c>Remove</c> naming the analyzer, and an
    /// <c>ItemDefinitionGroup</c> default of <c>ProjectReference</c> setting <c>OutputItemType</c> or
    /// <c>ReferenceOutputAssembly</c>. Metadata is read whether written as an attribute or as a child
    /// element, its name compared case-insensitively (<see cref="ProjectFile.Items"/>). A reference
    /// added by a <c>.props</c> or <c>.targets</c> file is not read.
    /// </summary>
    [Fact]
    public void ModulesReferenceThePersistenceAnalyzer()
    {
        var breaches = new List<string>();
        var adapters = Solution.Modules.Select(module => module.Adapters.ProjectFile).ToList();

        foreach (var project in adapters)
        {
            var references = AnalyzerReferences(project).ToList();
            if (references.Count == 0)
            {
                breaches.Add($"{Relative(project)} does not reference {Relative(AnalyzerProject)}");
            }

            breaches.AddRange(references
                .Where(reference => !(reference.IsAnalyzerOutput && reference.ReferencesNoOutputAssembly))
                .Select(_ => $"{Relative(project)} references the persistence analyzer without OutputItemType=\"Analyzer\" and ReferenceOutputAssembly=\"false\""));
        }

        var testProjects = Solution.SourceFiles("*.csproj")
            .Where(file => Path.GetFileNameWithoutExtension(file).EndsWith("Tests", StringComparison.Ordinal))
            .ToList();

        foreach (var project in testProjects)
        {
            // By file name, or by any path the test cannot read: a reference that may be the
            // analyzer is reported, rather than passed unseen.
            breaches.AddRange(ProjectFile.Items(project, "ProjectReference")
                .Where(reference => reference.IsAnalyzerOutput && reference.Include.Any(include => MayNameAnalyzer(project, include)))
                .Select(reference => $"{Relative(project)} references {string.Join(";", reference.Include)} as an analyzer, which is or may be the persistence analyzer"));

            breaches.AddRange(ProjectFile.Items(project, "Analyzer")
                .SelectMany(item => item.Include)
                .Where(include => include.Contains("MicroKit.Persistence.Analyzers", StringComparison.OrdinalIgnoreCase) || IsExpression(include))
                .Select(include => $"{Relative(project)} adds an Analyzer item that is or may be the persistence analyzer: {include}"));
        }

        foreach (var project in adapters.Concat(testProjects).Distinct(StringComparer.Ordinal))
        {
            foreach (var reference in ProjectFile.Items(project, "ProjectReference"))
            {
                breaches.AddRange(reference.Update
                    .Where(update => MayNameAnalyzer(project, update))
                    .Select(update => $"{Relative(project)} updates the ProjectReference {update}, which is or may be the persistence analyzer"));
                breaches.AddRange(reference.Remove
                    .Where(remove => MayNameAnalyzer(project, remove))
                    .Select(remove => $"{Relative(project)} removes the ProjectReference {remove}, which is or may be the persistence analyzer"));
            }

            breaches.AddRange(ProjectFile.ItemDefinitions(project, "ProjectReference")
                .SelectMany(definition => AnalyzerReferenceMetadata.Where(name => definition[name] is not null))
                .Select(name => $"{Relative(project)} gives every ProjectReference a default {name}, which applies to the persistence analyzer's reference"));
        }

        Breaches.None(breaches);
    }

    /// <summary>
    /// Builds each module's service collection through its entry point, with the connection string
    /// that makes it register its persistence (<c>ConnectionStrings:SaasBtp</c>, the API's one,
    /// <c>apps/api/CLAUDE.md</c>; every module is expected to register its persistence from it, so a
    /// module reading another setting registers nothing and is reported) pointing at no server, and
    /// resolves the options of every
    /// <c>DbContext</c> type of the module's assemblies, which opens no connection. A context is
    /// registered through the copied provider when its options hold the MicroKit options extension
    /// and the provider's four interceptors, which only <c>UsePostgreSQL</c> adds together, and when
    /// the service collection configures it once, so that no other registration there overrides the
    /// provider's. An abstract context is checked through the concrete contexts that derive from it.
    /// A module with no <c>DbContext</c> type passes. What a context does in its own code, such as
    /// an <c>OnConfiguring</c> override, is not on the service collection and is not read. The unit
    /// of work is not checked: the rule's check (ADR-ARCH-035 §9) names the context only. Not
    /// turned into breaches: an exception other than <see cref="InvalidOperationException"/> or
    /// <see cref="ArgumentException"/> while resolving a context's options, and a type of a module
    /// assembly that cannot be loaded (<see cref="ReflectionTypeLoadException"/>); either fails the
    /// test with the exception itself.
    /// </summary>
    [Fact]
    public void ModuleContextsUseMicroKitPersistence()
    {
        var breaches = new List<string>();

        foreach (var module in Solution.Modules)
        {
            var types = module.Layers
                .SelectMany(layer => layer.Assembly.GetTypes())
                .Where(type => typeof(DbContext).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();
            if (types.Count == 0)
            {
                continue;
            }

            // An abstract context cannot be registered itself: it is checked through the concrete
            // contexts of the module that derive from it, and is a breach when there is none.
            var contexts = types.Where(type => !type.IsAbstract).ToList();
            breaches.AddRange(types
                .Where(type => type.IsAbstract && !contexts.Any(type.IsAssignableFrom))
                .Select(type => $"Module {module.Context} has the abstract {type.FullName}, from which no context of the module derives: it is registered through the copied provider by none"));

            var register = module.Adapters.Assembly.GetType(module.EntryPointFullName)?
                .GetMethod($"Add{module.Context}Module", PublicStatic, [typeof(IServiceCollection), typeof(IConfiguration)]);
            if (register is null)
            {
                breaches.Add($"Module {module.Context} has no {module.EntryPointFullName}.Add{module.Context}Module(IServiceCollection, IConfiguration) to register its contexts with");
                continue;
            }

            var services = new ServiceCollection();
            try
            {
                register.Invoke(null, [services, ModuleConfiguration()]);
            }
            catch (TargetInvocationException exception)
            {
                breaches.Add($"Module {module.Context}: Add{module.Context}Module threw {exception.InnerException}");
                continue;
            }

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            foreach (var context in contexts)
            {
                DbContextOptions? options;
                try
                {
                    options = (DbContextOptions?)scope.ServiceProvider.GetService(typeof(DbContextOptions<>).MakeGenericType(context));
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    breaches.Add($"Module {module.Context}: the options of {context.FullName} cannot be resolved: {exception}");
                    continue;
                }

                if (options is null)
                {
                    breaches.Add($"Module {module.Context} does not register {context.FullName}");
                    continue;
                }

                var missing = MissingProviderParts(options).ToList();
                if (missing.Count > 0)
                {
                    breaches.Add($"Module {module.Context} registers {context.FullName} outside the copied provider's UsePostgreSQL: its options lack {string.Join(", ", missing)}");
                }

                // Every AddDbContext or ConfigureDbContext call adds one configuration, and EF Core
                // applies them all in order: a second one, after UsePostgreSQL's, keeps the parts
                // read above while it replaces the provider's connection or settings.
                var configurations = services.Count(descriptor =>
                    descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<>).MakeGenericType(context));
                if (configurations != 1)
                {
                    breaches.Add($"Module {module.Context} configures {context.FullName} {configurations} times: only the copied provider's UsePostgreSQL may configure it");
                }
            }
        }

        Breaches.None(breaches);
    }

    private static IEnumerable<string> MsBuildFiles() =>
        MsBuildFilePatterns.SelectMany(Solution.SourceFiles).Order(StringComparer.Ordinal);

    private static bool IsMicroKitPackage(string package) =>
        package.Equals("MicroKit", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("MicroKit.", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpression(string value) =>
        value.Contains("$(", StringComparison.Ordinal)
        || value.Contains("@(", StringComparison.Ordinal)
        || value.Contains("%(", StringComparison.Ordinal);

    /// <summary>A project file under <c>libraries/&lt;Lib&gt;/tests/</c> (ADR-ARCH-035 §4).</summary>
    private static bool IsCopiedTestProject(string projectFile)
    {
        var relative = Path.GetRelativePath(Solution.LibrariesDirectory, projectFile);
        var parts = relative.Split(Path.DirectorySeparatorChar);
        return !relative.StartsWith("..", StringComparison.Ordinal) && parts.Length > 2 && parts[1] == "tests";
    }

    /// <summary>
    /// Whether a path an item of <paramref name="projectFile"/> names is, or may be, the analyzer's
    /// project: by its resolved path, by its file name, or as an MSBuild expression.
    /// </summary>
    private static bool MayNameAnalyzer(string projectFile, string value) =>
        ProjectFile.Resolve(projectFile, value) == AnalyzerProject
        || string.Equals(Path.GetFileName(value.Replace('\\', '/')), Path.GetFileName(AnalyzerProject), StringComparison.OrdinalIgnoreCase)
        || IsExpression(value);

    private static IEnumerable<Item> AnalyzerReferences(string projectFile) =>
        ProjectFile.Items(projectFile, "ProjectReference")
            .Where(reference => reference.Include.Any(include => ProjectFile.Resolve(projectFile, include) == AnalyzerProject));

    private static IConfiguration ModuleConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // A server that does not exist: resolving a context's options connects to nothing.
                ["ConnectionStrings:SaasBtp"] = "Host=architecture-tests.invalid;Database=none",
            })
            .Build();

    private static IEnumerable<string> MissingProviderParts(DbContextOptions options)
    {
        if (!options.Extensions.Any(extension => extension.GetType().FullName == PersistenceOptionsExtension))
        {
            yield return PersistenceOptionsExtension;
        }

        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        foreach (var name in ProviderInterceptors.Where(name => !interceptors.Any(interceptor =>
            interceptor.GetType().Name == name && interceptor.GetType().Assembly.GetName().Name == PostgreSqlProvider)))
        {
            yield return $"{PostgreSqlProvider}'s {name}";
        }
    }
}

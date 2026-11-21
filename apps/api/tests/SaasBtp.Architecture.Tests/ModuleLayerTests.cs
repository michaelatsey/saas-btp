using ArchUnitNET.xUnitV3;
using static SaasBtp.Architecture.Tests.Rules;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// The rules inside one module (ADR-ARCH-034 §1, §6, §7): what Domain, Features and the module's
/// unit-test projects may depend on.
/// </summary>
public sealed class ModuleLayerTests
{
    [Fact]
    public void DomainDependsOnNoInfrastructure()
    {
        // Allowed to pass on an empty set: on the skeleton of #214, Domain holds no type yet.
        TypesIn(Solution.Modules.Select(module => module.Domain.Assembly))
            .Should().FollowCustomCondition(
                type => !DependencyTargets(type).Any(target => StartsWithAny(target.FullName, Infrastructure)),
                "depend on no data access, web or hosting type",
                "depends on a data access, web or hosting type")
            .WithoutRequiringPositiveResults()
            .Check(Solution.Architecture);

        var references = Solution.Modules
            .SelectMany(module => ProjectFile.PackageReferences(module.Domain.ProjectFile)
                .Where(package => StartsWithAny(package, Infrastructure))
                .Concat(ProjectFile.FrameworkReferences(module.Domain.ProjectFile)
                    .Where(framework => framework == AspNetCoreFramework))
                .Select(reference => $"{module.Domain.Name} references {reference}"))
            .ToList();

        Breaches.None(references);
    }

    [Fact]
    public void FeaturesDependOnNoAdapters()
    {
        foreach (var module in Solution.Modules)
        {
            // Allowed to pass on an empty set: on the skeleton of #214, Features holds no type yet.
            TypesIn([module.Features.Assembly])
                .Should().FollowCustomCondition(
                    type => !DependencyTargets(type).Any(target => AssemblyOf(target) == module.Adapters.Name),
                    $"depend on no type of {module.Adapters.Name}",
                    $"depends on a type of {module.Adapters.Name}")
                .WithoutRequiringPositiveResults()
                .Check(Solution.Architecture);
        }
    }

    [Fact]
    public void OnlyEndpointsDependOnAspNetCore()
    {
        // Allowed to pass on an empty set: on the skeleton of #214, Features holds no type yet, so
        // neither an endpoint, nor a handler, nor any other type.
        TypesIn(Solution.Modules.Select(module => module.Features.Assembly))
            .Should().FollowCustomCondition(
                type =>
                {
                    var names = NameChain(type).ToList();
                    var isEndpoint = names.Any(name => name.EndsWith("Endpoint", StringComparison.Ordinal));
                    var isHandler = names.Any(name => name.EndsWith("Handler", StringComparison.Ordinal));
                    var usesAspNetCore = DependencyTargets(type)
                        .Any(target => target.FullName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
                    return !usesAspNetCore || (isEndpoint && !isHandler);
                },
                "depend on Microsoft.AspNetCore only when an *Endpoint type, never when a *Handler type",
                "depends on Microsoft.AspNetCore without being an *Endpoint type, or is a *Handler type")
            .WithoutRequiringPositiveResults()
            .Check(Solution.Architecture);
    }

    /// <summary>
    /// Reads the project files under each module's <c>tests/</c> folder whose name ends in
    /// <c>.Tests</c>: their own references, and the projects through which data access or test
    /// containers arrive by reference (the module's Adapters, the copied EF Core libraries,
    /// <c>SaasBtp.Testing</c>); not what any other referenced project brings transitively.
    /// </summary>
    [Fact]
    public void UnitTestsUseNoInfrastructure()
    {
        string[] forbidden = [.. Infrastructure, "Testcontainers"];
        string[] forbiddenProjects = ["MicroKit.Persistence.EntityFrameworkCore", "SaasBtp.Testing"];

        // Allowed to pass on an empty set: on the skeleton of #214, no module has a test project.
        var references = Solution.Modules
            .SelectMany(module => module.TestProjectFiles
                .Where(project => project.EndsWith(".Tests.csproj", StringComparison.Ordinal))
                .SelectMany(project => ProjectFile.PackageReferences(project)
                    .Where(package => StartsWithAny(package, forbidden))
                    .Concat(ProjectFile.FrameworkReferences(project).Where(framework => framework == AspNetCoreFramework))
                    .Concat(ProjectFile.ProjectReferences(project)
                        .Where(reference => reference == module.Adapters.ProjectFile
                            || StartsWithAny(Path.GetFileNameWithoutExtension(reference), forbiddenProjects))
                        .Select(Path.GetFileNameWithoutExtension)
                        .OfType<string>())
                    .Select(reference => $"{Relative(project)} references {reference}")))
            .ToList();

        Breaches.None(references);
    }
}

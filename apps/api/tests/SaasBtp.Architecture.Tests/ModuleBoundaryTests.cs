using System.Reflection;
using System.Runtime.CompilerServices;
using ArchUnitNET.xUnitV3;
using static SaasBtp.Architecture.Tests.Rules;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// The rules at a module's edge (ADR-ARCH-020, ADR-ARCH-034 §2, §4, §7): modules do not reach one
/// another, and a host reaches a module only through its entry point.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private const string ServiceCollection = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string Configuration = "Microsoft.Extensions.Configuration.IConfiguration";
    private const string EndpointRouteBuilder = "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";

    /// <summary>
    /// With a single module there is no other module to reach: the type dependencies are read
    /// over the module's types, and none of them can target another module.
    /// </summary>
    [Fact]
    public void ModulesDoNotDependOnOneAnother()
    {
        TypesIn(ModuleAssemblies)
            .Should().FollowCustomCondition(
                type => !DependencyTargets(type).Any(target =>
                    ModuleOf(target) is { } other && other != ModuleOf(type)),
                "depend on no type of another module",
                "depends on a type of another module")
            .Check(Solution.Architecture);

        var references = Solution.Modules
            .SelectMany(module => module.ProjectFiles
                .SelectMany(project => ProjectFile.ProjectReferences(project)
                    .Where(reference => IsInModulesDirectory(reference) && !module.Contains(reference))
                    .Select(reference => $"{Relative(project)} references {Relative(reference)}")))
            .ToList();

        Breaches.None(references);
    }

    [Fact]
    public void HostsUseOnlyModuleEntryPoints()
    {
        TypesIn(HostAssemblies)
            .Should().FollowCustomCondition(
                type => !DependencyTargets(type).Any(target =>
                    ModuleOf(target) is { } module && target.FullName != module.EntryPointFullName),
                "use a module only through its <Context>Module",
                "uses a module type other than its <Context>Module")
            .Check(Solution.Architecture);

        // The same rule on the host's metadata: ArchUnitNET does not read Program.cs's top-level
        // statements (Il.TypeReferences).
        var used = Solution.Hosts
            .SelectMany(host => Il.TypeReferences(host.Assembly.Location)
                .Select(type => (host, type, module: Solution.Modules.FirstOrDefault(module => module.Layers.Any(layer => layer.Name == type.Assembly)))))
            .Where(use => use.module is not null && use.type.FullName != use.module.EntryPointFullName)
            .Select(use => $"{use.host.Name} uses {use.type.FullName}")
            .ToList();

        Breaches.None(used);

        var adapters = Solution.Modules.Select(module => module.Adapters.ProjectFile).ToHashSet(StringComparer.Ordinal);
        var references = Solution.Hosts
            .SelectMany(host => ProjectFile.ProjectReferences(host.ProjectFile)
                .Where(reference => IsInModulesDirectory(reference) && !adapters.Contains(reference))
                .Select(reference => $"{host.Name} references {Relative(reference)}"))
            .ToList();

        Breaches.None(references);
    }

    [Fact]
    public void ApiHostLoadsEveryModule()
    {
        var api = Assert.Single(Solution.Hosts, host => host.Name == "SaasBtp.Api");
        var calls = Il.Bodies(api.Assembly.Location).SelectMany(body => body.Calls).Select(Il.Definition).ToList();

        bool Calls(Module module, string method, params string[] parameters) => calls.Any(call =>
            call.DeclaringType.FullName == module.EntryPointFullName
            && call.Name == method
            && call.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters));

        var missing = Solution.Modules
            .SelectMany(module => new[]
            {
                Calls(module, $"Add{module.Context}Module", ServiceCollection, Configuration) ? null : $"{api.Name} does not call {module.Context}Module.Add{module.Context}Module",
                Calls(module, $"Map{module.Context}Endpoints", EndpointRouteBuilder) ? null : $"{api.Name} does not call {module.Context}Module.Map{module.Context}Endpoints",
            })
            .OfType<string>()
            .ToList();

        Breaches.None(missing);
    }

    [Fact]
    public void ModuleSurfaceIsItsEntryPoint()
    {
        var breaches = new List<string>();

        foreach (var module in Solution.Modules)
        {
            var exported = module.Adapters.Assembly.GetExportedTypes();
            breaches.AddRange(exported
                .Where(type => type.FullName != module.EntryPointFullName)
                .Select(type => $"{type.FullName} is public in {module.Adapters.Name}"));

            var entryPoint = exported.SingleOrDefault(type => type.FullName == module.EntryPointFullName);
            if (entryPoint is null)
            {
                breaches.Add($"{module.Adapters.Name} has no public {module.EntryPointFullName}");
            }
            else
            {
                if (!(entryPoint.IsAbstract && entryPoint.IsSealed))
                {
                    breaches.Add($"{entryPoint.FullName} is not a static class");
                }

                if (!Offers(entryPoint, $"Add{module.Context}Module", ServiceCollection, Configuration))
                {
                    breaches.Add($"{entryPoint.FullName} offers no public static Add{module.Context}Module(IServiceCollection, IConfiguration)");
                }

                if (!Offers(entryPoint, $"Map{module.Context}Endpoints", EndpointRouteBuilder))
                {
                    breaches.Add($"{entryPoint.FullName} offers no public static Map{module.Context}Endpoints(IEndpointRouteBuilder)");
                }
            }

            // Allowed to pass on an empty set: on the skeleton of #214, Features holds no type yet.
            breaches.AddRange(module.Features.Assembly.GetExportedTypes()
                .Select(type => $"{type.FullName} is public in {module.Features.Name}"));
        }

        Breaches.None(breaches);
    }

    [Fact]
    public void InternalsVisibleToStaysInsideTheModule()
    {
        var breaches = new List<string>();

        foreach (var module in Solution.Modules)
        {
            string Test(string suffix) => $"SaasBtp.{module.Context}.{suffix}";
            var allowed = new Dictionary<Layer, string[]>
            {
                [module.Domain] = [],
                [module.Features] = [module.Adapters.Name, Test("Domain.Tests"), Test("Features.Tests"), Test("Adapters.IntegrationTests")],
                [module.Adapters] = [Test("Adapters.IntegrationTests")],
            };

            foreach (var (layer, friends) in allowed)
            {
                breaches.AddRange(layer.Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
                    .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim())
                    .Where(friend => !friends.Contains(friend, StringComparer.Ordinal))
                    .Select(friend => $"{layer.Name} grants InternalsVisibleTo to {friend}"));
            }
        }

        Breaches.None(breaches);
    }

    private static bool Offers(Type entryPoint, string name, params string[] parameters) =>
        entryPoint.GetMethods(PublicStatic).Any(method =>
            method.Name == name
            && method.GetParameters().Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters));
}

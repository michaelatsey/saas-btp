using ArchUnitNET.xUnitV3;
using static SaasBtp.Architecture.Tests.Rules;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// The names of a module's ports and of their adapters (naming.md, Ports and their adapters): a
/// port is named after the role it plays for its use case, never after the unit of work it runs
/// in, and a persistence port of the Features project is a store or a queries port, implemented by
/// the class of its name. Where the ports and their adapters live is ADR-ARCH-034 decision 1.
/// </summary>
public sealed class ModuleNamingTests
{
    private const string UnitOfWork = "UnitOfWork";

    private static readonly string[] PersistencePortSuffixes = ["Store", "Queries"];

    /// <summary>
    /// A unit of work is one transaction of a module (ADR-ARCH-033 §4), carried out by MicroKit's
    /// <c>IUnitOfWork</c>: no type of a module is named after it. Only the types declared in the
    /// module assemblies are judged, so a dependency such as <c>EfUnitOfWork&lt;TContext&gt;</c> is
    /// not; each type by its own simple name, so that a type nested in a misnamed one is not a
    /// second breach.
    /// </summary>
    [Fact]
    public void NoModuleTypeIsNamedAfterAUnitOfWork()
    {
        TypesIn(ModuleAssemblies)
            .Should().FollowCustomCondition(
                type => !NameChain(type).Last().EndsWith(UnitOfWork, StringComparison.Ordinal),
                $"have a name that does not end in {UnitOfWork}",
                $"has a name ending in {UnitOfWork}")
            .Check(Solution.Architecture);
    }

    /// <summary>
    /// Every interface of a Features <c>Ports</c> namespace that a class of an Adapters
    /// <c>Persistence</c> namespace implements is <c>I&lt;Name&gt;Store</c> or
    /// <c>I&lt;Name&gt;Queries</c>, and the class is named after it without <c>I</c>. A class
    /// outside <c>Persistence</c>, such as the caller adapter, is not judged; nor is a repository,
    /// whose port is the Domain's.
    /// </summary>
    [Fact]
    public void PersistencePortsAreStoresOrQueriesNamedLikeTheirAdapter()
    {
        var breaches = new List<string>();

        foreach (var module in Solution.Modules)
        {
            var persistence = $"{module.Adapters.Name}.Persistence";
            var ports = $"{module.Features.Name}.Ports";

            var adapters = module.Adapters.Assembly.GetTypes()
                .Where(type => type.IsClass && InNamespace(type, persistence));

            foreach (var adapter in adapters)
            {
                var implemented = adapter.GetInterfaces()
                    .Where(port => port.Assembly == module.Features.Assembly && InNamespace(port, ports));

                foreach (var port in implemented)
                {
                    var portName = SimpleName(port);
                    var adapterName = SimpleName(adapter);

                    // The class is named after the port only once the port's own name is valid: the
                    // name it is expected to carry is the port's without its I prefix.
                    if (!IsPersistencePortName(portName))
                    {
                        breaches.Add($"{port.FullName}, implemented by {adapter.FullName}, is not named I<Name>Store or I<Name>Queries");
                    }
                    else if (adapterName != portName[1..])
                    {
                        breaches.Add($"{adapter.FullName} implements {port.FullName} but is not named {portName[1..]}");
                    }
                }
            }
        }

        Breaches.None(breaches);
    }

    private static bool InNamespace(Type type, string name) =>
        type.Namespace is { } own && (own == name || own.StartsWith(name + ".", StringComparison.Ordinal));

    private static string SimpleName(Type type) => type.Name.Split('`')[0];

    // The I is a prefix only when an upper-case letter follows it: IdentityStore is not I + dentityStore.
    private static bool IsPersistencePortName(string name) =>
        name.Length > 1
        && name[0] == 'I'
        && char.IsUpper(name[1])
        && PersistencePortSuffixes.Any(suffix =>
            name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > 1 + suffix.Length);
}

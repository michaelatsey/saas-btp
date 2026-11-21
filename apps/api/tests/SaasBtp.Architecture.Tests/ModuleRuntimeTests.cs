using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.xUnitV3;
using Mono.Cecil;
using static SaasBtp.Architecture.Tests.Rules;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// The rules on what module and host code does at run time (ADR-ARCH-034 §2, §3, §7): no
/// mediator nor scanning, no interceptor nor connection of a module's own, and no SQL naming
/// another module's schema.
/// </summary>
public sealed class ModuleRuntimeTests
{
    private const string Assembly = "System.Reflection.Assembly";

    private static readonly string[] Interceptors =
    [
        "Microsoft.EntityFrameworkCore.Diagnostics.IDbTransactionInterceptor",
        "Microsoft.EntityFrameworkCore.Diagnostics.IDbCommandInterceptor",
        "Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor",
        "Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor",
    ];

    /// <summary>In modules and hosts; not the migration runner, whose embedded scripts need <c>Assembly</c>.</summary>
    [Fact]
    public void NoMediatorNorAssemblyScanning()
    {
        TypesIn(ModuleAssemblies.Concat(HostAssemblies))
            .Should().FollowCustomCondition(
                type => !DependencyTargets(type).Any(target =>
                    target.FullName.StartsWith("MediatR", StringComparison.Ordinal)
                    || target.FullName.StartsWith("Scrutor", StringComparison.Ordinal)
                    || target.FullName == Assembly),
                "depend on no MediatR, Scrutor or System.Reflection.Assembly type",
                "depends on MediatR, Scrutor or System.Reflection.Assembly")
            .Check(Solution.Architecture);

        // The same rule on the metadata: ArchUnitNET does not read a host's top-level statements
        // (Il.TypeReferences).
        var used = ModuleAssemblies.Concat(HostAssemblies)
            .SelectMany(assembly => Il.TypeReferences(assembly.Location)
                .Where(type => type.FullName.StartsWith("MediatR", StringComparison.Ordinal)
                    || type.FullName.StartsWith("Scrutor", StringComparison.Ordinal)
                    || type.FullName == Assembly)
                .Select(type => $"{assembly.GetName().Name} uses {type.FullName}"))
            .ToList();

        Breaches.None(used);

        var calls = ModuleAssemblies.Concat(HostAssemblies)
            .SelectMany(assembly => Il.Bodies(assembly.Location))
            .SelectMany(body => body.Calls.Select(Il.Definition)
                .Where(call => call.DeclaringType.FullName == Assembly
                    || (call.DeclaringType.FullName == "System.Type" && call.Name == "get_Assembly"))
                .Select(call => $"{body.Location} calls {call.DeclaringType.FullName}::{call.Name}"))
            .ToList();

        Breaches.None(calls);
    }

    [Fact]
    public void ModulesDefineNoInterceptorNorConnection()
    {
        TypesIn(ModuleAssemblies)
            .Should().FollowCustomCondition(
                type => !Ancestors(type).Any(ancestor => Interceptors.Contains(ancestor, StringComparer.Ordinal)),
                "define no transaction or command interceptor",
                "is a transaction or command interceptor")
            .Check(Solution.Architecture);

        var connections = ModuleAssemblies
            .SelectMany(assembly => Il.Bodies(assembly.Location))
            .SelectMany(body => body.Calls.Select(Il.Definition)
                .Where(OpensAConnection)
                .Select(call => $"{body.Location} calls {call.DeclaringType.FullName}::{call.Name}"))
            .ToList();

        Breaches.None(connections);

        // Allowed to pass on an empty set: on the skeleton of #214, no module defines a database
        // context yet. The copied provider's registration calls UseNpgsql itself
        // (PostgreSqlEfCoreBuilderExtensions).
        var unconfigured = Solution.Modules
            .Where(module => Solution.Architecture.Types
                .Where(type => ModuleOf(type) == module)
                .Any(type => Ancestors(type).Contains("Microsoft.EntityFrameworkCore.DbContext", StringComparer.Ordinal)))
            .Where(module => !module.Layers
                .SelectMany(layer => Il.Bodies(layer.Assembly.Location))
                .SelectMany(body => body.Calls.Select(Il.Definition))
                .Any(call => call.Name == "UseNpgsql"
                    || call.DeclaringType.FullName == "MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlEfCoreBuilderExtensions"))
            .Select(module => $"Module {module.Context} defines a DbContext and never configures it with UseNpgsql")
            .ToList();

        Breaches.None(unconfigured);
    }

    /// <summary>
    /// Reads, in each method that passes SQL (to <c>FromSql*</c>, <c>SqlQuery*</c>,
    /// <c>ExecuteSql*</c>, a command's <c>CommandText</c> or the constructor of an Npgsql or
    /// System.Data <c>*Command</c>), the strings it loads and those the fields it loads are
    /// assigned in a constructor; prose elsewhere is not SQL and is not read. SQL a method
    /// receives already built by another method is not traced.
    /// </summary>
    [Fact]
    public void ModuleSqlNamesOnlyItsSchema()
    {
        var breaches = new List<string>();

        foreach (var module in Solution.Modules)
        {
            // Allowed to pass on an empty set: on the skeleton of #214 there is no other module,
            // and no module passes SQL yet.
            var others = Solution.Modules
                .Where(other => other != module)
                .Select(other => SchemaReference(other.Schema))
                .ToList();

            foreach (var layer in module.Layers)
            {
                var fieldStrings = Il.FieldStrings(layer.Assembly.Location);

                foreach (var body in Il.Bodies(layer.Assembly.Location).Where(body => body.Calls.Select(Il.Definition).Any(PassesSql)))
                {
                    var strings = body.Strings.Concat(body.LoadedFields
                        .SelectMany(field => fieldStrings.TryGetValue(field.FullName, out var values) ? values : []));

                    breaches.AddRange(strings
                        .Where(sql => others.Any(schema => schema.IsMatch(sql)))
                        .Select(sql => $"{body.Location} passes SQL naming another module's schema: {sql}"));
                }
            }
        }

        Breaches.None(breaches);
    }

    /// <summary>
    /// <c>\b&lt;schema&gt;\.[a-z_][a-z0-9_]*\b</c> (ADR-ARCH-034 §7), as PostgreSQL reads a
    /// name: an unquoted schema or table in any case, since PostgreSQL folds it to lower case
    /// (<c>Access.People</c> is <c>access.people</c>), or a quoted one exactly
    /// (<c>"access"."people"</c>). A dot followed by a space, as in prose, is no reference.
    /// </summary>
    private static Regex SchemaReference(string schema)
    {
        var name = Regex.Escape(schema);
        return new Regex(
            $@"(?<![A-Za-z0-9_$""])(?:(?i:{name})(?![A-Za-z0-9_$])|""{name}"")\.(?:""[^""]+""|[A-Za-z_][A-Za-z0-9_$]*)",
            RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> Ancestors(IType type)
    {
        var classes = type is Class @class ? @class.InheritedClasses.Select(inherited => inherited.FullName) : [];
        return classes.Concat(type.ImplementedInterfaces.Select(implemented => implemented.FullName));
    }

    private static bool OpensAConnection(MethodReference call) =>
        (call.Name == ".ctor" && call.DeclaringType.FullName == "Npgsql.NpgsqlConnection")
        || (call.DeclaringType.FullName == "Npgsql.NpgsqlDataSource" && call.Name.StartsWith("Create", StringComparison.Ordinal))
        || call.Name.StartsWith("OpenConnection", StringComparison.Ordinal)
        || call.Name == "CreateConnection";

    private static bool PassesSql(MethodReference call) =>
        call.Name.StartsWith("FromSql", StringComparison.Ordinal)
        || call.Name.StartsWith("SqlQuery", StringComparison.Ordinal)
        || call.Name.StartsWith("ExecuteSql", StringComparison.Ordinal)
        || call.Name == "set_CommandText"
        // A database command, not a use case's command record.
        || (call.Name == ".ctor"
            && (call.DeclaringType.Namespace == "Npgsql" || call.DeclaringType.Namespace.StartsWith("System.Data", StringComparison.Ordinal))
            && call.DeclaringType.Name.EndsWith("Command", StringComparison.Ordinal)
            && call.Parameters.Count > 0
            && call.Parameters[0].ParameterType.FullName == "System.String");
}

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// What an assembly's IL holds and type dependencies cannot tell: the methods its bodies call
/// and the strings they load. Every type is read, nested and compiler-generated ones included, so
/// that calls compiled into lambdas and async state machines are seen.
/// </summary>
internal static class Il
{
    /// <summary>A method body, located as <c>Type::Method</c>, and what it calls and loads.</summary>
    internal sealed record Body(string Location, IReadOnlyList<MethodReference> Calls, IReadOnlyList<string> Strings, IReadOnlyList<FieldReference> LoadedFields);

    public static IReadOnlyList<Body> Bodies(string assemblyPath)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);
        var bodies = new List<Body>();

        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                var calls = new List<MethodReference>();
                var strings = new List<string>();
                var fields = new List<FieldReference>();

                foreach (var instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MethodReference target when instruction.OpCode.Code is Code.Call or Code.Callvirt
                            or Code.Newobj or Code.Ldftn or Code.Ldvirtftn or Code.Ldtoken:
                            calls.Add(target);
                            break;
                        case string value when instruction.OpCode.Code is Code.Ldstr:
                            strings.Add(value);
                            break;
                        case FieldReference field when instruction.OpCode.Code is Code.Ldsfld or Code.Ldfld:
                            fields.Add(field);
                            break;
                    }
                }

                bodies.Add(new Body($"{type.FullName}::{method.Name}", calls, strings, fields));
            }
        }

        return bodies;
    }

    /// <summary>
    /// The strings each field of <paramref name="assemblyPath"/> is assigned in a constructor or a
    /// static constructor of its type, keyed by the field's full name: a <c>static readonly</c>
    /// string, unlike a <c>const</c>, is not inlined where it is used.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> FieldStrings(string assemblyPath)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);
        var strings = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var type in module.GetTypes())
        {
            foreach (var constructor in type.Methods.Where(method => method.IsConstructor && method.HasBody))
            {
                // A string is a field's value only when stored right after it is loaded: one
                // passed to a call first is the call's argument, not the field's value.
                string? pending = null;
                foreach (var instruction in constructor.Body.Instructions)
                {
                    if (instruction.OpCode.Code is Code.Stsfld or Code.Stfld && pending is not null)
                    {
                        var field = (FieldReference)instruction.Operand;
                        if (!strings.TryGetValue(field.FullName, out var values))
                        {
                            strings[field.FullName] = values = [];
                        }

                        values.Add(pending);
                    }

                    pending = instruction.OpCode.Code is Code.Ldstr ? (string)instruction.Operand : null;
                }
            }
        }

        return strings.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value, StringComparer.Ordinal);
    }

    /// <summary>A type of another assembly that an assembly's metadata references.</summary>
    internal sealed record ReferencedType(string FullName, string Assembly);

    /// <summary>
    /// Every type of another assembly that <paramref name="assemblyPath"/> references anywhere: in
    /// a signature, an attribute or a method body, compiled code included. ArchUnitNET 0.13.5 does
    /// not read the body of the method the compiler generates for top-level statements
    /// (<c>Program.&lt;Main&gt;$</c>), which a host's <c>Program.cs</c> compiles to; this does.
    /// </summary>
    public static IReadOnlyList<ReferencedType> TypeReferences(string assemblyPath)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);
        return module.GetTypeReferences()
            .Select(type => new ReferencedType(type.FullName, type.Scope is AssemblyNameReference assembly ? assembly.Name : type.Scope.Name))
            .ToList();
    }

    /// <summary>The method a call targets, without its generic instantiation.</summary>
    public static MethodReference Definition(MethodReference target) =>
        target is GenericInstanceMethod generic ? generic.ElementMethod : target;
}

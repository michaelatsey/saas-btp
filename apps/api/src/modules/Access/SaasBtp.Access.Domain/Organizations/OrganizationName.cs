using System.Diagnostics.CodeAnalysis;

namespace SaasBtp.Access.Domain.Organizations;

/// <summary>
/// The name of an organization, as its founder declared it, measured as rule BP001-R7 states. It is built only through
/// <see cref="TryCreate"/>, so that every instance holds a measured name.
/// </summary>
public sealed record OrganizationName
{
    /// <summary>The most Unicode scalar values the name may hold (BP001-R7).</summary>
    public const int MaxLength = DeclaredName.MaxLength;

    private OrganizationName(string value) => Value = value;

    /// <summary>The name, without the white space around it, otherwise as declared and not normalized.</summary>
    public string Value { get; }

    /// <summary>
    /// Measures <paramref name="declared"/> as BP001-R7 states: the white space around it removed,
    /// missing when nothing remains, too long beyond <see cref="MaxLength"/> Unicode scalar values.
    /// </summary>
    /// <param name="declared">The name as declared; null counts as missing.</param>
    /// <param name="name">The name, when it is valid.</param>
    /// <param name="defect">What is wrong with it, or <see cref="NameDefect.None"/>.</param>
    /// <returns>Whether the name is valid.</returns>
    public static bool TryCreate(string? declared, [NotNullWhen(true)] out OrganizationName? name, out NameDefect defect)
    {
        defect = DeclaredName.Measure(declared, out var trimmed);
        name = defect == NameDefect.None ? new OrganizationName(trimmed) : null;
        return name is not null;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

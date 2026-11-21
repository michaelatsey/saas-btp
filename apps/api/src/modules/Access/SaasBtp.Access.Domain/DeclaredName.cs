namespace SaasBtp.Access.Domain;

/// <summary>
/// The measure of a declared name (rule BP001-R7, as contract.md §2 states it): the white space
/// <see cref="string.Trim()"/> removes is removed from both ends; what remains is counted in
/// Unicode scalar values (<see cref="string.EnumerateRunes"/>), never in UTF-16 code units or
/// text elements; that trimmed value, otherwise unchanged and not normalized, is the one checked,
/// counted and kept.
/// </summary>
internal static class DeclaredName
{
    /// <summary>The most Unicode scalar values a name may hold (BP001-R7).</summary>
    internal const int MaxLength = 200;

    /// <summary>Measures <paramref name="declared"/>; a null name counts as missing.</summary>
    internal static NameDefect Measure(string? declared, out string trimmed)
    {
        trimmed = (declared ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return NameDefect.Missing;

        var scalarValues = 0;
        foreach (var _ in trimmed.EnumerateRunes())
        {
            if (++scalarValues > MaxLength)
                return NameDefect.TooLong;
        }

        return NameDefect.None;
    }
}

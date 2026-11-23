using System.Xml.Linq;

namespace SaasBtp.Architecture.Tests;

/// <summary>
/// The items of a project file as written in the working tree: what the compiler drops when no
/// code uses it, a reference still brings.
/// </summary>
internal static class ProjectFile
{
    public static IEnumerable<string> PackageReferences(string projectFile) => Includes(projectFile, "PackageReference");

    public static IEnumerable<string> FrameworkReferences(string projectFile) => Includes(projectFile, "FrameworkReference");

    /// <summary>Each <c>ProjectReference</c>, resolved to a full path from the project's folder.</summary>
    public static IEnumerable<string> ProjectReferences(string projectFile) =>
        Includes(projectFile, "ProjectReference").Select(include => Resolve(projectFile, include));

    /// <summary>A path an item of <paramref name="projectFile"/> includes, resolved to a full path from the project's folder.</summary>
    public static string Resolve(string projectFile, string include) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, include.Replace('\\', Path.DirectorySeparatorChar)));

    /// <summary>
    /// Each item named <paramref name="itemName"/> in an MSBuild file's <c>ItemGroup</c>s, with its
    /// <c>Include</c>, its <c>Update</c> and its <c>Remove</c>, each split on <c>;</c>, and its
    /// metadata, read from attributes and from child elements alike, since MSBuild accepts both. Item
    /// and metadata names are compared case-insensitively, as MSBuild compares them; conditions are
    /// not evaluated.
    /// </summary>
    public static IReadOnlyList<Item> Items(string projectFile, string itemName) => Read(projectFile, itemName, "ItemGroup");

    /// <summary>
    /// The defaults an MSBuild file's <c>ItemDefinitionGroup</c>s give the items named
    /// <paramref name="itemName"/>, read as <see cref="Items"/> reads an item.
    /// </summary>
    public static IReadOnlyList<Item> ItemDefinitions(string projectFile, string itemName) => Read(projectFile, itemName, "ItemDefinitionGroup");

    private static List<Item> Read(string projectFile, string itemName, string group) =>
        XDocument.Load(projectFile)
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, itemName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(element.Parent?.Name.LocalName, group, StringComparison.OrdinalIgnoreCase))
            .Select(element =>
            {
                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var attribute in element.Attributes())
                {
                    metadata[attribute.Name.LocalName] = attribute.Value;
                }

                foreach (var child in element.Elements())
                {
                    metadata[child.Name.LocalName] = child.Value.Trim();
                }

                return new Item(Split(metadata, "Include"), Split(metadata, "Update"), Split(metadata, "Remove"), metadata);
            })
            .ToList();

    private static List<string> Split(Dictionary<string, string> metadata, string name) =>
        metadata.TryGetValue(name, out var value)
            ? [.. value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];

    private static List<string> Includes(string projectFile, string item) =>
        XDocument.Load(projectFile)
            .Descendants()
            .Where(element => element.Name.LocalName == item)
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToList();
}

/// <summary>
/// An item of an MSBuild file: what it includes, updates and removes, and its metadata keyed
/// case-insensitively (<c>Include</c>, <c>Update</c> and <c>Remove</c> among them, as written).
/// </summary>
internal sealed record Item(IReadOnlyList<string> Include, IReadOnlyList<string> Update, IReadOnlyList<string> Remove, IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>The value of a metadata, or <see langword="null"/> when the item does not set it.</summary>
    public string? this[string name] => Metadata.TryGetValue(name, out var value) ? value : null;

    /// <summary>Whether the item's <c>OutputItemType</c> is <c>Analyzer</c>, compared case-insensitively.</summary>
    public bool IsAnalyzerOutput => string.Equals(this["OutputItemType"], "Analyzer", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the item's <c>ReferenceOutputAssembly</c> reads as <c>false</c>, compared case-insensitively.</summary>
    public bool ReferencesNoOutputAssembly => string.Equals(this["ReferenceOutputAssembly"], "false", StringComparison.OrdinalIgnoreCase);
}

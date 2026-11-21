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
        Includes(projectFile, "ProjectReference")
            .Select(include => Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(projectFile)!,
                include.Replace('\\', Path.DirectorySeparatorChar))));

    private static List<string> Includes(string projectFile, string item) =>
        XDocument.Load(projectFile)
            .Descendants()
            .Where(element => element.Name.LocalName == item)
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToList();
}

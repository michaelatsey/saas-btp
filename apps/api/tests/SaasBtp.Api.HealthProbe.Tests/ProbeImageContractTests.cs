using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SaasBtp.Api.HealthProbe.Tests;

/// <summary>
/// What ties the probe to the API image (ADR-ARCH-039): the probe runs on the base framework
/// alone, and the Dockerfile's HEALTHCHECK runs this probe with a timeout longer than the
/// probe's own. Reads the Dockerfile and the project file as they stand in the working tree.
/// </summary>
public sealed partial class ProbeImageContractTests
{
    [Fact]
    public void TheHealthcheckRunsTheProbeAssemblyInExecForm()
    {
        var healthcheck = HealthcheckInstruction();
        var assembly = typeof(ReadinessProbe).Assembly.GetName().Name;

        Assert.EndsWith($"CMD [\"dotnet\", \"/probe/{assembly}.dll\"]", healthcheck, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProbeTimesOutBeforeTheHealthcheckDoes()
    {
        var match = TimeoutOption().Match(HealthcheckInstruction());
        Assert.True(match.Success, "The HEALTHCHECK sets no --timeout in seconds.");

        var healthcheckTimeout = TimeSpan.FromSeconds(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(ReadinessProbe.DefaultTimeout > TimeSpan.Zero, "The probe has no timeout.");
        Assert.True(
            ReadinessProbe.DefaultTimeout < healthcheckTimeout,
            $"The probe waits {ReadinessProbe.DefaultTimeout}, the HEALTHCHECK {healthcheckTimeout}.");
    }

    [Fact]
    public void TheProbeAssemblyReferencesNeitherAspNetCoreNorExtensions()
    {
        var references = typeof(ReadinessProbe).Assembly.GetReferencedAssemblies().Select(name => name.Name!);

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal));
    }

    // The compiler drops references no code uses, so the assembly alone would not show an unused
    // FrameworkReference, which still binds the probe to that shared framework at run time.
    [Fact]
    public void TheProbeProjectReferencesOnlyTheBaseFramework()
    {
        var project = XDocument.Load(Metadata("ProbeProjectPath"));

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")?.Value);
        Assert.DoesNotContain(project.Descendants(), element => element.Name.LocalName
            is "FrameworkReference" or "PackageReference" or "ProjectReference");
    }

    // The probe as built, which the test output holds: its runtime configuration names the base
    // framework alone, and its dependencies hold no package, whatever the root build files add.
    [Fact]
    public void TheProbeAsBuiltRunsOnTheBaseFrameworkAloneWithNoPackage()
    {
        var name = typeof(ReadinessProbe).Assembly.GetName().Name;
        using var runtimeConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, $"{name}.runtimeconfig.json")));
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, $"{name}.deps.json")));

        var options = runtimeConfig.RootElement.GetProperty("runtimeOptions");
        Assert.False(options.TryGetProperty("frameworks", out _), "The probe runs on more than one shared framework.");
        Assert.Equal("Microsoft.NETCore.App", options.GetProperty("framework").GetProperty("name").GetString());
        Assert.Equal([$"{name}/1.0.0"], deps.RootElement.GetProperty("libraries").EnumerateObject().Select(library => library.Name));
    }

    private static string HealthcheckInstruction()
    {
        // Dockerfile instructions are case-insensitive and may be indented.
        var instructions = File.ReadAllLines(Metadata("ApiDockerfilePath"))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("HEALTHCHECK ", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Only the last HEALTHCHECK takes effect: one is declared, so none is shadowed.
        return Assert.Single(instructions);
    }

    private static string Metadata(string key) => typeof(ProbeImageContractTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key)
        .Value!;

    [GeneratedRegex(@"--timeout=(\d+)s\b")]
    private static partial Regex TimeoutOption();
}

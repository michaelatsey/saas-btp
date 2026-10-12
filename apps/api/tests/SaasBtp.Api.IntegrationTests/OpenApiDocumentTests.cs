using System.Reflection;
using System.Text.Json;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// Reads <c>apps/api/openapi/v1.json</c> as it stands in the working tree: the file the build
/// writes and the repository commits.
/// </summary>
public sealed class OpenApiDocumentTests
{
    [Fact]
    public void TheDocumentDescribesThePingEndpoint()
    {
        using var document = ReadDocument();

        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/v1/ping", out var ping), "No /api/v1/ping path in v1.json.");
        Assert.True(ping.TryGetProperty("get", out _), "No GET operation on /api/v1/ping in v1.json.");
    }

    [Fact]
    public void TheDocumentDoesNotDescribeTheHealthEndpoints()
    {
        using var document = ReadDocument();

        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name);

        Assert.DoesNotContain(paths, path => path.StartsWith("/health", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonDocument ReadDocument()
    {
        var path = typeof(OpenApiDocumentTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "OpenApiDocumentPath")
            .Value!;

        return JsonDocument.Parse(File.ReadAllText(path));
    }
}

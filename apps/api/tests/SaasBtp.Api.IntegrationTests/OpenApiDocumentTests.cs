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
    public void TheDocumentDescribesTheFoundingOperation()
    {
        using var document = ReadDocument();

        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/v1/organizations", out var organizations), "No /api/v1/organizations path in v1.json.");
        Assert.True(organizations.TryGetProperty("post", out var post), "No POST operation on /api/v1/organizations in v1.json.");
        Assert.Equal("FoundCompany", post.GetProperty("operationId").GetString());

        // The body is the founding request only, never a service the endpoint resolves.
        var body = post.GetProperty("requestBody").GetProperty("content");
        Assert.Equal(["application/json"], body.EnumerateObject().Select(content => content.Name));
        Assert.Equal(
            "#/components/schemas/FoundCompanyRequest",
            body.GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());

        var statuses = post.GetProperty("responses").EnumerateObject().Select(response => response.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["201", "400", "401", "403", "422", "500"], statuses);
    }

    /// <summary>
    /// The founding's body as contract.md §2 states it: its three properties required and never null,
    /// whatever the endpoint tolerates when it reads a body to answer it 400.
    /// </summary>
    [Fact]
    public void TheFoundingBodyPropertiesAreRequiredAndNotNull()
    {
        using var document = ReadDocument();

        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("FoundCompanyRequest");

        string[] properties = ["organizationId", "organizationName", "workspaceName"];
        Assert.Equal(properties, schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        foreach (var property in properties)
        {
            var type = schema.GetProperty("properties").GetProperty(property).GetProperty("type");
            Assert.True(type.ValueKind == JsonValueKind.String && type.GetString() == "string", $"{property} is typed {type.GetRawText()}, not a non-null string.");
        }

        Assert.Equal("uuid", schema.GetProperty("properties").GetProperty("organizationId").GetProperty("format").GetString());
    }

    /// <summary>Every 201 of the document describes its <c>Location</c> header (contract.md §2 for the founding).</summary>
    [Fact]
    public void Every201DescribesItsLocationHeader()
    {
        using var document = ReadDocument();

        var created = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(operation => operation.Value.GetProperty("responses").TryGetProperty("201", out _))
            .Select(operation => operation.Value.GetProperty("responses").GetProperty("201"))
            .ToList();

        Assert.NotEmpty(created);
        foreach (var response in created)
        {
            Assert.True(response.TryGetProperty("headers", out var headers), "A 201 response describes no header.");
            var location = headers.GetProperty("Location");
            Assert.True(location.GetProperty("required").GetBoolean());
            Assert.Equal("string", location.GetProperty("schema").GetProperty("type").GetString());
            Assert.Equal("uri-reference", location.GetProperty("schema").GetProperty("format").GetString());
        }
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

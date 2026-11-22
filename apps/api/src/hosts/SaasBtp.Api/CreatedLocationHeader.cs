using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SaasBtp.Api;

/// <summary>
/// Describes the <c>Location</c> header of every 201 response in the OpenAPI documents
/// (ADR-ARCH-018): a creation names what it created (contract.md §2 for the founding). It lives in
/// the host, so that no module references the OpenAPI package (ADR-ARCH-034 decision 1).
/// </summary>
internal static class CreatedLocationHeader
{
    public const string Name = "Location";

    public static Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.Responses?.TryGetValue("201", out var created) == true && created is OpenApiResponse response)
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
            response.Headers[Name] = new OpenApiHeader
            {
                Description = "The address of the resource created.",
                Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" },
            };
        }

        return Task.CompletedTask;
    }
}

using Microsoft.AspNetCore.Routing;
using SaasBtp.Access.Features.Organizations.Found;
using SaasBtp.Access.Features.Organizations.Read;
using SaasBtp.Access.Features.Workspaces.List;

namespace SaasBtp.Access.Features;

/// <summary>
/// The endpoints of the Access module's slices, each mapped explicitly, with no scanning
/// (ADR-ARCH-034 decision 2). Their handlers are registered by <see cref="FeatureRegistration"/>.
/// </summary>
internal static class FeatureEndpoint
{
    /// <summary>Maps the slices' endpoints on the route builder of the API's major version.</summary>
    public static IEndpointRouteBuilder MapFeatures(this IEndpointRouteBuilder endpoints) =>
        endpoints
            .MapFoundCompany()
            .MapReadOrganization()
            .MapListWorkspaces();
}

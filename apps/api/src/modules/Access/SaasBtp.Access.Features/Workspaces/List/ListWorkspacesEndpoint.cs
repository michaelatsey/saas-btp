using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SaasBtp.Access.Features.Workspaces.List;

/// <summary>
/// <c>GET /api/v1/workspaces</c>, list the workspaces I can enter (contract.md §2): it calls its
/// handler and answers 200 with them. The host has authenticated the caller before this runs, under
/// its fallback policy: the read accepts a token for its whole lifetime (ADR-ARCH-038 decisions 1
/// and 6).
/// </summary>
/// <remarks>
/// Any other failure is answered 500 with no body (ADR-ARCH-040) and logged once with its class, never
/// a value or an exception message (ADR-ARCH-032 §5).
/// </remarks>
internal static partial class ListWorkspacesEndpoint
{
    public static IEndpointRouteBuilder MapListWorkspaces(this IEndpointRouteBuilder endpoints)
    {
        // Typed as a route handler, so that its IResult is written to the response.
        var list = (Func<HttpContext, Task<IResult>>)ListAsync;

        endpoints.MapGet("/workspaces", list)
            .WithName("ListWorkspaces")
            .WithTags("Workspaces")
            .Produces<IReadOnlyList<WorkspaceResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(HttpContext context)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ListWorkspacesEndpoint).FullName!);
        var cancellationToken = context.RequestAborted;

        try
        {
            // Resolved here, so that a host without the module's persistence answers this 500 too.
            var handler = context.RequestServices.GetRequiredService<ListWorkspacesHandler>();
            var workspaces = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);

            return TypedResults.Ok<IReadOnlyList<WorkspaceResponse>>(
                [.. workspaces.Select(workspace => new WorkspaceResponse(workspace.Id, workspace.Name, workspace.OrganizationId))]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away: there is no one to answer.
            throw;
        }
#pragma warning disable CA1031 // Any other failure is answered 500 with no detail (ADR-ARCH-040).
        catch (Exception failure)
#pragma warning restore CA1031
        {
            Failed(logger, StatusCodes.Status500InternalServerError, EndpointFailure.ClassOf(failure));
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Workspace listing failed, answered {Status}: {Class}")]
    private static partial void Failed(ILogger logger, int status, string @class);
}

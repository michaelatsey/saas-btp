using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SaasBtp.Access.Features.Organizations.Read;

/// <summary>
/// <c>GET /api/v1/organizations/{id}</c>, read an organization (contract.md §2): 200 with the
/// organization when the policies let the caller see it, 404 with no body otherwise, whether or not it
/// exists (ADR-ARCH-032 §5, ADR-ARCH-040). The host has authenticated the caller before this runs,
/// under its fallback policy: the read accepts a token for its whole lifetime (ADR-ARCH-038
/// decisions 1 and 6).
/// </summary>
/// <remarks>
/// A segment that is not a UUID does not match the route and is answered 404 by routing, as an
/// identifier that exists nowhere is. A 404 is logged once with a fixed class, the same whether the
/// organization is hidden or absent; any other failure is answered 500 with no body and logged once
/// with its class, never a value or an exception message (ADR-ARCH-032 §5).
/// </remarks>
internal static partial class ReadOrganizationEndpoint
{
    public static IEndpointRouteBuilder MapReadOrganization(this IEndpointRouteBuilder endpoints)
    {
        // Typed as a route handler, so that its IResult is written to the response.
        var read = (Func<HttpContext, Guid, Task<IResult>>)ReadAsync;

        endpoints.MapGet("/organizations/{id:guid}", read)
            .WithName("ReadOrganization")
            .WithTags("Organizations")
            .Produces<OrganizationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> ReadAsync(HttpContext context, Guid id)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ReadOrganizationEndpoint).FullName!);
        var cancellationToken = context.RequestAborted;

        try
        {
            // Resolved here, so that a host without the module's persistence answers this 500 too.
            var handler = context.RequestServices.GetRequiredService<ReadOrganizationHandler>();
            var organization = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);
            if (organization is null)
            {
                NotFound(logger, StatusCodes.Status404NotFound, "not visible");
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(OrganizationResponse.Of(organization));
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Organization read answered {Status}: {Class}")]
    private static partial void NotFound(ILogger logger, int status, string @class);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Organization read failed, answered {Status}: {Class}")]
    private static partial void Failed(ILogger logger, int status, string @class);
}

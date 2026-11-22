using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasBtp.Access.Domain.Organizations;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace SaasBtp.Access.Features.Organizations.Found;

/// <summary>
/// <c>POST /api/v1/organizations</c>, found a company (contract.md §2): it reads the body, calls its
/// handler, and answers. The host has authenticated the caller and checked that the token is recent
/// before this runs (ADR-ARCH-038 decisions 1, 4 and 6).
/// </summary>
/// <remarks>
/// <para>
/// The answers that carry a detail, 422 and 403, are problem details returned here; 400 and 500
/// carry none (ADR-ARCH-040). The body is read here rather than bound by the framework, so that a
/// body that cannot be read or bound is answered 400 with no body and logged with its class.
/// </para>
/// <para>
/// Every refusal is logged once with its class, fixed text or exception type names, never a value
/// of the request or an exception message (ADR-ARCH-032 §5).
/// </para>
/// </remarks>
internal static partial class FoundCompanyEndpoint
{
    /// <summary>The host's policy of an operation that requires a recent token (ADR-ARCH-038 decision 6).</summary>
    public const string RecentTokenPolicy = "RecentToken";

    /// <summary>The 422 message of each field, the same whatever its fault (ADR-ARCH-040).</summary>
    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        [FoundCompanyField.OrganizationId] = "The reference must be a UUIDv7.",
        [FoundCompanyField.OrganizationName] = "The company name must hold 1 to 200 characters.",
        [FoundCompanyField.WorkspaceName] = "The first workspace name must hold 1 to 200 characters.",
    };

    private const string ContactMustBeConfirmed = "The contact must be confirmed.";

    public static IEndpointRouteBuilder MapFoundCompany(this IEndpointRouteBuilder endpoints)
    {
        // Typed as a route handler, so that its IResult is written to the response.
        var found = (Func<HttpContext, Task<IResult>>)FoundAsync;

        endpoints.MapPost("/organizations", found)
            .Accepts<FoundCompanyRequest>("application/json")
            .WithName("FoundCompany")
            .WithTags("Organizations")
            .RequireAuthorization(RecentTokenPolicy)
            .Produces<OrganizationResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status500InternalServerError);

        // The same operation for a request whose content type is not JSON. The JSON content type the
        // documented route accepts makes routing answer any other 415 before the endpoint runs; this
        // route, tried after it (order 1) and never described, lets the endpoint answer it 400 with
        // no body, as contract.md §2 states for a body that cannot be read.
        endpoints.MapPost("/organizations", found)
            .WithOrder(1)
            .RequireAuthorization(RecentTokenPolicy)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> FoundAsync(HttpContext context)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(FoundCompanyEndpoint).FullName!);
        var cancellationToken = context.RequestAborted;

        try
        {
            var (request, unreadable) = await ReadAsync(context, cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                Refused(logger, StatusCodes.Status400BadRequest, unreadable);
                return TypedResults.BadRequest();
            }

            // Resolved here, so that a host without the module's persistence answers this 500 too.
            var handler = context.RequestServices.GetRequiredService<FoundCompanyHandler>();
            var outcome = await handler.HandleAsync(
                request.OrganizationId!.Value, request.OrganizationName, request.WorkspaceName, cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case FoundCompanyOutcome.Founded founded:
                    var organization = founded.Company.Organization;
                    return TypedResults.Created(
                        $"{context.Request.PathBase}{RouteOf(context)}/{organization.Id.Value:D}",
                        new OrganizationResponse(organization.Id.Value, organization.Name.Value, StatusToken(organization.Status)));

                case FoundCompanyOutcome.Invalid invalid:
                    var fields = string.Join(", ", invalid.Fields);
                    Refused(logger, StatusCodes.Status422UnprocessableEntity, fields);
                    return TypedResults.Problem(new HttpValidationProblemDetails(
                        invalid.Fields.ToDictionary(field => field, field => new[] { Messages[field] }, StringComparer.Ordinal))
                    {
                        Status = StatusCodes.Status422UnprocessableEntity,
                    });

                case FoundCompanyOutcome.NotConfirmed:
                    Refused(logger, StatusCodes.Status403Forbidden, "contact not confirmed");
                    return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: ContactMustBeConfirmed);

                default:
                    throw new InvalidOperationException($"The founding came to an outcome it does not answer: {outcome.GetType().Name}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away: there is no one to answer.
            throw;
        }
#pragma warning disable CA1031 // Any other failure is answered 500 with no detail (contract.md §2).
        catch (Exception failure)
#pragma warning restore CA1031
        {
            Failed(logger, StatusCodes.Status500InternalServerError, ClassOf(failure));
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// The body, or why it cannot be read or bound (contract.md §2, 400): not JSON, not a JSON object,
    /// a property of the wrong type, or an <c>organizationId</c> that is not a UUID, absent included.
    /// </summary>
    private static async Task<(ReceivedFoundCompanyRequest? Request, string Unreadable)> ReadAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.HasJsonContentType())
        {
            return (null, "body not JSON");
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return (null, "body unreadable");
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            // Kestrel's BadHttpRequestException (a body over the size limit, a malformed chunk) is an
            // IOException, as is a transport failure while the request is still alive.
            return (null, "body unreadable");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, "body unreadable");
            }

            var options = context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            ReceivedFoundCompanyRequest? request;
            try
            {
                request = document.RootElement.Deserialize<ReceivedFoundCompanyRequest>(options);
            }
            catch (JsonException unbound)
            {
                return (null, string.Equals(unbound.Path, "$.organizationId", StringComparison.OrdinalIgnoreCase)
                    ? "organizationId not a UUID"
                    : "body not bound");
            }

            return request?.OrganizationId is null ? (null, "organizationId not a UUID") : (request, string.Empty);
        }
    }

    /// <summary>
    /// The operation's route as mapped, the host's group prefix included, so that the location names
    /// the organization whatever trailing slash or casing the request matched with.
    /// </summary>
    private static string RouteOf(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } route }
            ? route.TrimEnd('/')
            : throw new InvalidOperationException("The founding runs outside its route endpoint.");

    /// <summary>The class of a failure: the type names of the exception and its inner ones, and a database's SQLSTATE.</summary>
    private static string ClassOf(Exception failure)
    {
        var parts = new List<string>();
        for (var current = failure; current is not null; current = current.InnerException)
        {
            parts.Add(current is DbException { SqlState: { } state } ? $"{current.GetType().Name} (SQLSTATE {state})" : current.GetType().Name);
        }

        return string.Join(" <- ", parts);
    }

    /// <summary>The wire token of a status (<c>naming.md</c>, Enum value style).</summary>
    private static string StatusToken(OrganizationStatus status) => status switch
    {
        OrganizationStatus.Declared => "declared",
        OrganizationStatus.Verified => "verified",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "An organization status has no token."),
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Founding refused, answered {Status}: {Class}")]
    private static partial void Refused(ILogger logger, int status, string @class);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Founding failed, answered {Status}: {Class}")]
    private static partial void Failed(ILogger logger, int status, string @class);
}

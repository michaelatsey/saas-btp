using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// The endpoints the tests map (ADR-ARCH-038 decision 9, "one the test maps"), added to the
/// host's own route builder so that they run behind the host's routing, authentication and
/// authorization exactly as the host's endpoints do.
/// </summary>
/// <remarks>
/// <c>WebApplication</c> wires its pipeline into the application builder a startup filter is
/// given, and leaves its route builder under the property key of ASP.NET Core's routing
/// (<c>__EndpointRouteBuilder</c>, dotnet/aspnetcore v10.0.12, <c>WebApplicationBuilder.cs</c>).
/// The endpoints are added there before the first request, while the matcher is still unbuilt.
/// </remarks>
internal sealed class TestEndpoints : IStartupFilter
{
    public const string Protected = "/test/protected";
    public const string Recent = "/test/recent";
    public const string Subject = "/test/subject";
    public const string AnonymousSubject = "/test/anonymous-subject";

    /// <summary>What the subject endpoints answer when the port returns no subject.</summary>
    public const string NoSubject = "none";

    private const string EndpointRouteBuilderKey = "__EndpointRouteBuilder";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);

        if (!app.Properties.TryGetValue(EndpointRouteBuilderKey, out var value) || value is not IEndpointRouteBuilder routes)
        {
            throw new InvalidOperationException(
                $"The host's route builder is not under '{EndpointRouteBuilderKey}': the test endpoints cannot be mapped.");
        }

        // No authorization declared: the fallback policy applies.
        routes.MapGet(Protected, () => "ok");

        // An operation that requires a recent token (ADR-ARCH-038 decision 6).
        routes.MapGet(Recent, () => "ok").RequireAuthorization("RecentToken");

        routes.MapGet(Subject, CurrentSubject);
        routes.MapGet(AnonymousSubject, CurrentSubject).AllowAnonymous();
    };

    // The port is resolved explicitly, so that a host without it answers this endpoint only, not
    // every endpoint (a parameter of a type the container lacks would be read as a body).
    private static string CurrentSubject(HttpContext context) =>
        context.RequestServices.GetService(typeof(IUnitOfWorkSubject)) is IUnitOfWorkSubject subject
            ? subject.Current?.ToString("D") ?? NoSubject
            : "no port registered";
}

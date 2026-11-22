using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SaasBtp.Access.Adapters;
using SaasBtp.Api;
using SaasBtp.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Structured logs (ADR-ARCH-018): one JSON object per line, UTC timestamps, and the scopes that
// carry the TraceId and SpanId of the request.
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "O";
});

// Every 201 describes its Location header (CreatedLocationHeader).
builder.Services.AddOpenApi("v1", options => options.AddOperationTransformer(CreatedLocationHeader.TransformAsync));

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(
        DatabaseHealthCheck.Name,
        tags: [DatabaseHealthCheck.ReadinessTag],
        timeout: DatabaseHealthCheck.Timeout);

// The provider's access token authenticates every request; the anonymous endpoints say so
// (ADR-ARCH-038 decisions 1 to 4 and 6). It also gives each transaction its subject.
builder.Services.AddAccessTokenAuthentication(builder.Configuration);

// Every module under apps/api/src/modules/, through its entry point, called by type
// (ADR-ARCH-034 §2).
builder.Services.AddAccessModule(builder.Configuration);

var app = builder.Build();

// One route group per major (ADR-ARCH-018); its name selects the OpenAPI document it belongs to.
var v1 = app.MapGroup("/api/v1").WithGroupName("v1");

v1.MapGet("/ping", () => TypedResults.Ok(new PingResponse("ok")))
    .WithName("Ping")
    .AllowAnonymous();

v1.MapAccessEndpoints();

// Health endpoints are unversioned, anonymous and absent from the OpenAPI documents.
// Liveness runs no check, but like every request it passes the authentication middleware: with an
// AccessToken setting missing or invalid it answers 500 too (AccessTokenOptions).
var liveness = new HealthCheckOptions { Predicate = _ => false };
var readiness = new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(DatabaseHealthCheck.ReadinessTag),
};

app.MapHealthChecks("/health/live", liveness)
    .AllowAnonymous()
    .ExcludeFromDescription();

app.MapHealthChecks("/health/ready", readiness)
    .AllowAnonymous()
    .ExcludeFromDescription();

app.Run();

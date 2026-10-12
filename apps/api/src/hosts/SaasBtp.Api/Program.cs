using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SaasBtp.Api;

var builder = WebApplication.CreateBuilder(args);

// Structured logs (ADR-ARCH-018): one JSON object per line, UTC timestamps, and the scopes that
// carry the TraceId and SpanId of the request.
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "O";
});

builder.Services.AddOpenApi("v1");

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(
        DatabaseHealthCheck.Name,
        tags: [DatabaseHealthCheck.ReadinessTag],
        timeout: DatabaseHealthCheck.Timeout);

var app = builder.Build();

// One route group per major (ADR-ARCH-018); its name selects the OpenAPI document it belongs to.
var v1 = app.MapGroup("/api/v1").WithGroupName("v1");

v1.MapGet("/ping", () => TypedResults.Ok(new PingResponse("ok")))
    .WithName("Ping");

// Health endpoints are unversioned, anonymous and absent from the OpenAPI documents.
// Liveness runs no check: it answers for the process alone.
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

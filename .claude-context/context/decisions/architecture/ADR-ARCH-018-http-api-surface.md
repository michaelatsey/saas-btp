# ADR-ARCH-018 — HTTP API surface: Minimal APIs, major version in the URL, committed OpenAPI

Status: accepted
Date: 2026-10-12

## Context
The API host is rebuilt after the reset (#114). Its first client is an offline-first PWA,
the platform validated by spike #3 (`docs/spikes/powersync-web/findings.md`): an installed
client can keep running an old bundle long after a release, until its
service worker updates, so two API versions will coexist in practice. The frontend/backend
contract must be a committed artifact that the TypeScript client is generated from, with
the .NET side isolated from the JS workspace.

## Decision
1. Endpoints use Minimal APIs. No MVC controllers.
2. The major version is a URL segment, `/api/v{major}/...`, implemented as one route group
   per major, tagged with `WithGroupName("v{major}")`. No versioning library.
3. Within a major, only additive changes are allowed: a new optional field, a new
   endpoint. Removing or renaming a field, changing a type, or adding a required input
   opens a new major. A major is retired only when no device still calls it; the
   criterion is the state of the device fleet, not a date.
4. OpenAPI documents are produced by the built-in `Microsoft.AspNetCore.OpenApi`, one
   document per major (`v1` first), generated at build time with
   `Microsoft.Extensions.ApiDescription.Server`, and committed at
   `apps/api/openapi/v1.json`. CI fails when a build produces a file that differs from the
   committed one. This document is the contract boundary: TypeScript clients are generated
   from it into `packages/api-client`, never the reverse; the generator is chosen when the
   client is first generated; and the .NET side stays isolated from the JS workspace.
5. Build-time generation launches the application entry point on a mock server, so
   application startup never requires a database connection or a secret; any
   database-dependent registration is guarded accordingly.
6. Health endpoints are `/health/live` (the process) and `/health/ready` (its
   dependencies, the database included). They are unversioned, anonymous, and excluded
   from the OpenAPI documents.
7. Logs use the built-in console logger with the JSON formatter and `IncludeScopes`
   enabled, so every line carries the `TraceId` and `SpanId` that `ActivityTrackingOptions`
   enables by default. Timestamps are UTC. OpenTelemetry is added at the first real need.

## Consequences
- No third-party dependency for versioning or OpenAPI; the contract is reviewable in
  every pull request through the committed document.
- No deprecation headers and no supported-versions header. The first coexisting `v2` is
  the trigger to re-evaluate a versioning library.
- The additive rule also protects writes queued offline and replayed after an update.

## Alternatives considered
- `Asp.Versioning`: rejected for now; its benefits (deprecation and version-reporting
  headers) serve third-party clients, and the only client is ours.
- Header or media-type versioning: rejected; less visible in logs and caches, and more
  awkward for the PWA client.
- No version segment: rejected; adding one later breaks every URL, while adding it now
  costs nothing.
- Runtime-only OpenAPI: rejected; the contract would not be committed or checked in CI.
- Serilog: rejected for now; the built-in JSON formatter covers structured logs with
  trace correlation.

## References
- Microsoft Learn, "Generate OpenAPI documents" (ASP.NET Core 10): multiple documents,
  build-time generation, mock-server entry point.
- Microsoft Learn, "Logging in .NET and ASP.NET Core": `ActivityTrackingOptions` defaults.

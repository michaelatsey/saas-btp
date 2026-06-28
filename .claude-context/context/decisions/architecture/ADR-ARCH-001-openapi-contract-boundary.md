# ADR-ARCH-001 — TypeScript clients generated from OpenAPI, not from C#

Status: accepted
Date: 2026-06-28

## Context

The monorepo hosts a .NET 10 backend (`apps/api`) and TypeScript frontends
(`apps/web`, later `apps/mobile`). The frontends need typed access to the backend
contracts. Two strategies exist:

1. Generate TypeScript directly from C# types (e.g. TypeGen, Reinforced.Typings).
2. Generate TypeScript from the OpenAPI document the API produces.

Constraints valued by the project: decoupling, low blast radius on failure,
mobile (Phase 2) parity, and a contract visible in PR diffs.

## Decision

The shared contract boundary is the **OpenAPI document**, not the C# types.

- `apps/api` enables build-time OpenAPI generation via the built-in
  `Microsoft.AspNetCore.OpenApi` + `Microsoft.Extensions.ApiDescription.Server`
  (`OpenApiGenerateDocuments=true`). The `openapi.json` is committed so contract
  changes appear in PR diffs.
- TypeScript clients/types are generated FROM that `openapi.json` into
  `packages/api-client`, consumed by `apps/web` and `apps/mobile`.
- The .NET API stays fully isolated from the pnpm/JS workspace. .NET does not need
  Node; JS does not need .NET.
- The specific generator (Kiota / openapi-typescript / NSwag / OpenAPI Generator)
  is chosen just-in-time, when the client is first generated — NOT now.

## Consequences

- Easier: no build-time coupling between toolchains; a generation failure is
  isolated to the frontend; one contract serves web and mobile; rollback = pin or
  regenerate the client.
- Harder: requires the OpenAPI document to be kept clean (operationIds, schemas) —
  enforced via linting (e.g. Spectral) at CI later.
- Forbidden: leaking internal C# types into the frontend by direct C#->TS generation.

## Rationale (current as of 2026-06)

- .NET 10 generates OpenAPI natively (Swashbuckle removed from templates); the
  document can be produced at build time and committed alongside code.
- .NET 10 defaults to OpenAPI 3.1 / JSON Schema 2020-12, which converged with strict
  JSON Schema — third-party TS generators interact without translation bugs on
  complex models (the historical pain of C#->TS and old 3.0 tooling).

## Alternatives considered

- Direct C#->TS generation: rejected — couples toolchains, leaks implementation
  types, no natural mobile path, larger blast radius.
- Manual TS types: rejected — drift risk, no single source of truth.

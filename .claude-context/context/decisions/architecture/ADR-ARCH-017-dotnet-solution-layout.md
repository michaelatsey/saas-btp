# ADR-ARCH-017 — Solution layout for the .NET side of the monorepo

Status: accepted
Date: 2026-10-12

## Context
The API reset (#114) removed every .NET project and the architecture ADRs written for them.
The API is rebuilt from the consigned documents. The first .NET work is an API host skeleton
with no module; it needs a fixed place for the host, its tests, the schema migrations and
the .NET libraries this repository will own (MicroKit, copied in-tree). The internal
structure of modules is decided separately, before BP-001 is implemented.

## Decision
- `apps/api/src/SaasBtp.Api` is the only host.
- `apps/api/src/Modules/<Context>/` is reserved for bounded-context modules, one folder per
  context. This ADR imposes no structure inside a module.
- `apps/api/tests/` holds the test projects: `<TestedProject>.Tests` for unit tests,
  `<TestedProject>.IntegrationTests` for integration tests.
- `apps/api/database/SaasBtp.Migrations` holds the schema migrations (ADR-ARCH-019).
- `libraries/` at the repository root holds the .NET libraries owned by this repository.
  It is .NET only and distinct from `packages/`, which is the JS workspace.
- `SaasBtp.slnx` references every .NET project, `libraries/` included.
- The root `Directory.Build.props` and `Directory.Packages.props` govern every .NET project,
  `libraries/` included. No nested `Directory.Build.props` or `Directory.Packages.props` is
  added: a library brought into the repository has its MSBuild settings merged into the
  root files, never copied alongside it.
- Two module rules hold from the first module, each enforced by an architecture test:
  the host reaches a module only through that module's single entry point, and no project
  of one module references a project of another module. References between projects of
  the same module are not constrained by this ADR.

## Consequences
- The API skeleton can be built without deciding module internals.
- A nested `Directory.*.props` file is a defect: it would silently shadow the root
  governance for everything below it.
- Module internals, inter-module communication and the MicroKit copy are decided before
  BP-001; this ADR only reserves their locations.

## Alternatives considered
- Libraries under `apps/api/`: rejected; `apps/` holds deployable applications, and a
  library is not one.
- Libraries under `packages/`: rejected; `packages/` belongs to the pnpm workspace, and
  mixing toolchains there would blur the isolation of the .NET side.
- Recording the full module design now: rejected; it must first be confronted with
  BP-001's single-transaction requirement, and an accepted ADR cannot be amended.

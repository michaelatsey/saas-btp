# ADR-ARCH-020 — Solution layout for the .NET side of the monorepo: hosts, modules and folder casing

Status: accepted
Date: 2026-10-13

## Context
ADR-ARCH-017 fixed the layout of the .NET side of the monorepo and placed the only host
directly at `apps/api/src/SaasBtp.Api`, next to a `Modules/` folder. Before any .NET
project was created, the maintainer chose to group hosts under their own folder. That
choice also exposed the casing of grouping folders: `Modules/` was the only capitalized
one beside `apps/`, `src/`, `tests/`, `database/` and `openapi/`. An accepted ADR is
superseded, never edited, so this ADR restates the whole layout and supersedes
ADR-ARCH-017. The API skeleton (#124) is the first .NET work and is built on this layout;
the internal structure of modules is decided separately, before BP-001 is implemented.

## Decision
- `apps/api/src/hosts/` holds the hosts, one folder per host.
  `apps/api/src/hosts/SaasBtp.Api` is the first host.
- `apps/api/src/modules/<Context>/` is reserved for bounded-context modules, one folder per
  context. `modules/` is created with the first module, not before. This ADR imposes no
  structure inside a module.
- `apps/api/tests/` holds the test projects: `<TestedProject>.Tests` for unit tests,
  `<TestedProject>.IntegrationTests` for integration tests.
- `apps/api/database/SaasBtp.Migrations` holds the schema migrations (ADR-ARCH-019). The
  runner is a deployment tool, not a runtime host: it stays under `database/`, and
  ADR-ARCH-019 is unchanged.
- `libraries/` at the repository root holds the .NET libraries owned by this repository.
  It is .NET only and distinct from `packages/`, which is the JS workspace.
- `SaasBtp.slnx` references every .NET project, `libraries/` included.
- The root `Directory.Build.props` and `Directory.Packages.props` govern every .NET project,
  `libraries/` included. No nested `Directory.Build.props` or `Directory.Packages.props` is
  added: a library brought into the repository has its MSBuild settings merged into the
  root files, never copied alongside it.
- Folder casing follows one rule on the .NET side of the monorepo and on the root folders
  it uses (`apps/`, `libraries/`): a grouping folder is lower-case (`apps/`, `src/`,
  `hosts/`, `modules/`, `tests/`, `database/`, `openapi/`, `libraries/`); a folder named
  after a .NET project or a bounded context is PascalCase (`SaasBtp.Api`, `<Context>`).
  The rule applies down to the project folder; inside a project, folders follow the .NET
  convention and match their namespace. The JS side keeps its own conventions; this ADR
  does not govern it.
- Two module rules hold from the first module, each enforced by an architecture test:
  every host reaches a module only through that module's single entry point, and no project
  of one module references a project of another module. References between projects of
  the same module are not constrained by this ADR.

## Consequences
- The API skeleton can be built without deciding module internals.
- A further host has its place without a new layout decision, and the entry-point rule
  already binds it.
- The casing is settled before the folders exist: a case-only rename is handled poorly by
  Git on a case-insensitive file system.
- A nested `Directory.*.props` file is a defect: it would silently shadow the root
  governance for everything below it.
- Module internals, inter-module communication and the MicroKit copy are decided before
  BP-001; this ADR only reserves their locations.
- ADR-ARCH-017 is superseded. ADR-ARCH-018 and ADR-ARCH-019 are unchanged.

## Alternatives considered
- `Hosts/` and `Modules/` in PascalCase: rejected; the rule would become "lower-case except
  under `src/`", while `tests/`, at the same level, stays lower-case.
- Keeping the host directly under `src/`: rejected; a project folder would sit beside the
  `modules/` grouping folder at the same level, and a further host would add to the mix.
- The migration runner under `tools/`, as before the reset: rejected; the runner is
  deployed with the API on every release (ADR-ARCH-019), and `apps/` holds what is
  deployed.
- Libraries under `apps/api/`: rejected; `apps/` holds deployable applications, and a
  library is not one.
- Libraries under `packages/`: rejected; `packages/` belongs to the pnpm workspace, and
  mixing toolchains there would blur the isolation of the .NET side.
- Recording the full module design now: rejected; it must first be confronted with
  BP-001's single-transaction requirement, and an accepted ADR cannot be amended.

# ADR-ARCH-034 — Module structure: three projects per module, one entry point, tests inside the module, integration events between modules

Status: accepted
Date: 2026-10-23
Refs: issue #174; parent #125; ADR-ARCH-020; ADR-ARCH-022; ADR-ARCH-031; ADR-ARCH-032;
ADR-ARCH-033; `architecture.md` §2-§4; `sql.md`.

## Context
ADR-ARCH-020 reserves `apps/api/src/modules/<Context>/`, imposes nothing inside a module, places
every test project under `apps/api/tests/`, and requires two architecture tests: a host reaches
a module only through its single entry point, and no project of one module references a project
of another. Modules share one database today, and each must be extractable as a service with its
own storage (ADR-ARCH-033); an indivisible business step lives in one module (ADR-ARCH-031).
ADR-ARCH-033 left to this decision where a module's database context and its interceptors are
registered, and how a request involving two modules coordinates them.

The maintainer requires, for every module: isolation from the others, communication by
integration events as between services, a hexagonal structure with the domain at its centre, DDD,
features as vertical slices without a mediator, a decoupling the compiler enforces so that unit
tests stay clean, no project per adapter nor a separate project for ports, and the module's tests
inside the module.

The image check of ADR-ARCH-022 fails a pull request when a host uses a type from a project its
Dockerfile does not copy, but not when the host only references such a project: a module loaded by
reflection or assembly scanning could be missing from the image (comment of 2026-10-21 on #125).

What was read on 2026-10-23:
- The repository runs tests on `xunit.v3` under Microsoft.Testing.Platform (`global.json`,
  `Directory.Packages.props`) from `SaasBtp.slnx` (`.github/workflows/ci-api.yml`). The image
  build context lets back in each copied project by its own path (`.dockerignore`).
  `apps/api/tests/PostgresImage.cs` is shared by linking the file into each test project.
- nuget.org: `TngTech.ArchUnitNET` 0.13.4, updated 2026-08-20, with a `TngTech.ArchUnitNET.xUnitV3`
  integration; `NetArchTest.Rules` 1.3.2, last updated 2021-05-23.
- A reference modular monolith (kgrzybek/modular-monolith-with-ddd) checks isolation between
  modules with a hand-written list of modules: a module absent from the list is never checked,
  and one of its modules calls another synchronously unnoticed.

## Decision
1. **Three projects per module.** Under `apps/api/src/modules/<Context>/`:

   | Project | Holds | References |
   |---|---|---|
   | `SaasBtp.<Context>.Domain` | aggregates, value objects, business rules, domain events, repository ports | the domain libraries of `libraries/` only; no data access, web or hosting package |
   | `SaasBtp.<Context>.Features` | one vertical slice per use case, `<Feature>/<UseCase>/`, an endpoint and its handler; ports other than repositories, in `Ports/` | its Domain; the ASP.NET Core framework for endpoints; no adapter |
   | `SaasBtp.<Context>.Adapters` | the implementation of every port, one folder per adapter (`Persistence/`, …); the module's entry point | its Features and Domain; the data access and client packages its adapters need |

   An endpoint calls its handler directly; no mediator is used. A further adapter becomes a
   project of its own only when it brings a dependency the other adapters must not load, or when
   it has interchangeable implementations; the decision is recorded where it is made. How a
   module models its domain beyond this is the module's own.
2. **One entry point.** `<Context>Module`, a public static class of the Adapters project, offers
   `Add<Context>Module(IServiceCollection, IConfiguration)`, which registers everything the module
   needs, and `Map<Context>Endpoints(IEndpointRouteBuilder)`, which maps its routes. Registration
   is explicit: no assembly scanning and no discovery of handlers or endpoints by reflection. A
   host references each module's Adapters project only and calls both methods by type, so a
   module missing from a host, or from its image, fails the build. Every type of Features and
   Adapters other than the entry point is `internal`; Features grants `InternalsVisibleTo` to its
   module's Adapters and to its own tests only.
3. **Persistence inside a module.** `Add<Context>Module` registers the module's database context
   on its schema, with EF's automatic transactions disabled and the two interceptors of
   ADR-ARCH-033 §4. The interceptors are one shared implementation in `libraries/`, never copied
   into a module. A read in SQL runs on the connection and transaction of the module's unit of
   work, never on a connection opened apart: a connection opened apart carries no subject and
   reads nothing (ADR-ARCH-032 §3).
4. **Communication between modules.** Modules communicate by integration events, delivered as
   ADR-ARCH-032 §1 states, and a module that needs another's data keeps a local copy fed by its
   events. No project of a module references a project of another, whatever it holds
   (ADR-ARCH-020). No module calls another synchronously, and no authorization decision relies on
   a local copy, unless an ADR accepts it, stating why an event or a local copy does not fit and
   the consequences on transactions, availability and extraction. The one exception accepted is
   `<module>.may_act_in` (ADR-ARCH-032, ADR-ARCH-033). A request that involves two modules writes
   in one; the other reacts to its event.
5. **Event schemas.** The producer owns the schema of each event it publishes, in
   `apps/api/src/modules/<Context>/events/`. Each consumer declares its own types for the events
   it consumes, and a contract test of the consumer fails when its types no longer match the
   published schema. A published event changes only by adding an optional field; any other change
   is a new, versioned event, and the previous one is still published while a consumer uses it.
   The schema format and any generation tooling are decided with the first event a consumer uses.
6. **Tests inside the module.** This replaces the location of tests in ADR-ARCH-020 for modules.
   A module's tests live in `apps/api/src/modules/<Context>/tests/`:
   - `SaasBtp.<Context>.Domain.Tests`: unit tests, referencing the Domain only;
   - `SaasBtp.<Context>.Features.Tests`: unit tests of the handlers against fake ports, with no
     database;
   - `SaasBtp.<Context>.IntegrationTests`: the adapters against PostgreSQL migrated by the runner,
     including `MappingMatchesMigratedSchema` (ADR-ARCH-033) and the tests of ADR-ARCH-032 §6.

   `apps/api/tests/` keeps the tests that belong to no module: the hosts', the runner's, the
   architecture tests, and `SaasBtp.Testing`, the shared test support (the PostgreSQL image, the
   roles applied, a migrated database), created with the first module that needs it. The image
   build context lets back in each project a host references, never a module's `tests/` folder.
7. **Architecture tests.** `apps/api/tests/SaasBtp.Architecture.Tests` checks, with ArchUnitNET
   and its xUnit v3 integration, every rule the compiler does not already enforce:

   | Rule | Enforced by |
   |---|---|
   | A module's Domain depends on no data access, web or hosting type | no package reference; `DomainDependsOnNoInfrastructure` |
   | A module's Features depends on no Adapters type | no project reference; `FeaturesDoNotDependOnAdapters` |
   | No module depends on another module | `ModulesDoNotDependOnOneAnother`, which finds every folder under `apps/api/src/modules/` and fails when one of them is not loaded by the test |
   | A host uses a module only through `<Context>Module` | `HostsUseOnlyModuleEntryPoints` |
   | `<Context>Module` is the only public type of Adapters, and Features types are internal | `ModuleSurfaceIsItsEntryPoint` |
   | No mediator and no registration by assembly scanning | `ModulesUseNoMediatorNorAssemblyScanning` |
   | A module missing from a host or its image | the build and the image check of ADR-ARCH-022 |
   | A consumer's event types match the producer's schema | the consumer's contract test (decision 5) |

## Consequences
- Moving a module out moves one folder, its tests and its event schemas included, with its
  `may_act_in` function, scripts and journal rows (ADR-ARCH-033).
- A handler is unit-tested without a database, since Features cannot reach an adapter.
- A new adapter is a folder, not a project, until it needs isolation of its own.
- The image check of ADR-ARCH-022 needs no change: the host uses each module's entry point by type.
- `SaasBtp.slnx` lists each module's projects and tests; CI runs them from it unchanged.
- The tests' location in ADR-ARCH-020 is replaced for modules; its other rules stand.
- `architecture.md` §4 is aligned: no internal architecture imposed beyond decision 1, no
  mediator, and its own rule on audit fields and soft delete removed in favour of `sql.md` and
  `naming.md`, which differ on mutable entities and are reconciled separately.

## Alternatives considered
- One project per module, its layers as folders checked by architecture tests: the decoupling of
  Domain and Features from adapters would no longer be enforced by the compiler.
- Domain, Application, Infrastructure and IntegrationEvents projects per module: more projects,
  and the IntegrationEvents project referenced by consumers would tie modules by code.
- A project per adapter and a separate project for ports: a project for each new adapter, for a
  separation a folder and the compiler's reference rules already give.
- A mediator: an indirection between an endpoint and its handler with no behaviour that needs it
  today.
- Public synchronous contracts between modules (a module interface called by others): a runtime
  and code dependency between modules, which an extracted module would turn into a remote call.
- A dependency injection container per module: isolation the reference rules and architecture
  tests already give, at the cost of a non-standard composition.
- NetArchTest: not updated since 2021.
- Module tests under `apps/api/tests/`: a module would no longer be one folder.

## References
- nuget.org, `TngTech.ArchUnitNET`, `TngTech.ArchUnitNET.xUnitV3`, `NetArchTest.Rules`, read
  2026-10-23.
- M. Jovanović, "Vertical Slice Architecture Folder Structure"; D. Comartin, "Is Vertical Slice
  Architecture better than Clean Architecture or Ports and Adapters?", read 2026-10-23.
- kgrzybek/modular-monolith-with-ddd at `91c8ef2` (`src/Tests/ArchTests`, ADR 0014, 0016); the
  maintainer's mini-ged at `00a6689`.

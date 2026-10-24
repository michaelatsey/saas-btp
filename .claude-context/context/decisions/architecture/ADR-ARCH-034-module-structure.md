# ADR-ARCH-034 — Module structure: three projects per module, one entry point, tests inside the module, integration events between modules

Status: accepted
Date: 2026-10-23
Refs: issue #174; parent #125; ADR-ARCH-020; ADR-ARCH-021; ADR-ARCH-022; ADR-ARCH-031;
ADR-ARCH-032; ADR-ARCH-033; `architecture.md` §2-§4; `sql.md`.

## Context
ADR-ARCH-020 reserves `apps/api/src/modules/<Context>/`, imposes nothing inside a module, places
the test projects under `apps/api/tests/`, names them `<TestedProject>.Tests` and
`<TestedProject>.IntegrationTests`, and requires two architecture tests: a host reaches a module
only through its single entry point, and no project of one module references a project of
another. Modules share one database today, and each must be extractable as a service with its own
storage (ADR-ARCH-033); an indivisible business step lives in one module (ADR-ARCH-031). That a
module never queries Access's tables is a code rule enforced by architecture tests
(ADR-ARCH-032 §2). ADR-ARCH-033 left to this decision where a module's database context and its
interceptors are registered, and how a request involving two modules coordinates them.

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
  `Directory.Packages.props`) from `SaasBtp.slnx` (`.github/workflows/ci-api.yml`). The test
  settings of `Directory.Build.props` apply to projects whose name ends in `Tests`, wherever they
  live. The image build context lets back in each copied project by its own path
  (`.dockerignore`). `apps/api/tests/PostgresImage.cs` is shared by linking the file into each
  test project.
- nuget.org: `TngTech.ArchUnitNET` 0.13.4, updated 2026-08-20, with a `TngTech.ArchUnitNET.xUnitV3`
  integration; `NetArchTest.Rules` 1.3.2, last updated 2021-05-23.
- A reference modular monolith (kgrzybek/modular-monolith-with-ddd at `91c8ef2`) checks isolation
  between modules against a hand-written list of namespaces (`src/Tests/ArchTests/SeedWork/
  TestBase.cs`); its Registrations module is absent from it, and that module calls UserAccess
  synchronously (`src/Modules/Registrations/Infrastructure/Users/UserAccessGateway.cs`) with no
  test failing.

## Decision
1. **Three projects per module, one archetype.** Every module has the same three projects under
   `apps/api/src/modules/<Context>/`; there is one archetype, and no module uses a mediator.

   | Project | Holds | References |
   |---|---|---|
   | `SaasBtp.<Context>.Domain` | aggregates, value objects, business rules, domain events, repository ports | domain building blocks only; no data access, web or hosting package |
   | `SaasBtp.<Context>.Features` | one vertical slice per use case, `<Feature>/<UseCase>/`: an endpoint and its handler; ports other than repositories, in `Ports/` | its Domain; the ASP.NET Core framework, for endpoints only |
   | `SaasBtp.<Context>.Adapters` | the implementation of every port, one folder per adapter (`Persistence/`, …); the module's entry point | its Features and Domain; the data access and client packages its adapters need |

   The endpoint is the slice's driving adapter: it translates the request and calls its handler
   directly. In a slice, the endpoint is the class whose name ends in `Endpoint` and the handler
   the class whose name ends in `Handler`; a handler depends on no ASP.NET Core type. A further
   adapter becomes a project of its own only when it brings a dependency the other adapters must
   not load, or when it has interchangeable implementations, by an ADR that adds its rules to the
   table of decision 7. How a module models
   its domain beyond this is the module's own.
2. **One entry point.** `<Context>Module`, a public static class of the Adapters project, offers
   `Add<Context>Module(IServiceCollection, IConfiguration)`, which registers everything the module
   needs, and `Map<Context>Endpoints(IEndpointRouteBuilder)`, which maps its routes. Registration
   is explicit: no assembly scanning and no discovery of handlers or endpoints by reflection. A
   host references each module's Adapters project only and calls both methods by type, and every
   module under `apps/api/src/modules/` is called by the API host. Every type of Features and
   Adapters other than the entry point is `internal`; Features grants `InternalsVisibleTo` to its
   module's Adapters and to the module's test projects, Adapters to the module's integration
   tests, and no project grants it to anything else.
3. **Persistence inside a module.** `Add<Context>Module` registers the module's database context
   on its schema, with EF's automatic transactions disabled and the two interceptors of
   ADR-ARCH-033 §4. The interceptors are one shared implementation outside the modules, never a
   copy per module; which library holds it is decided with #177. A read in SQL runs on the
   connection and transaction of the module's unit of work, never on a connection opened apart: a
   connection opened apart carries no subject and reads nothing (ADR-ARCH-032 §3). A module's
   context maps only its own schema, and its SQL names no other module's schema: this is how the
   rule of ADR-ARCH-032 §2 that a module never queries Access's tables is checked.
4. **Communication between modules.** Modules communicate by integration events, delivered as
   ADR-ARCH-032 §1 states, and a module that needs another's data keeps a local copy fed by its
   events. No project of a module references a project of another, whatever it holds
   (ADR-ARCH-020). No module calls another synchronously, in process or remotely, and no
   authorization decision relies on a local copy, unless an ADR accepts it, stating why an event
   or a local copy does not fit and the consequences on transactions, availability and
   extraction. The one exception accepted is `<module>.may_act_in` (ADR-ARCH-032, ADR-ARCH-033). A
   request that involves two modules writes in one; the other reacts to its event. A domain event
   and an integration event are distinct: the first belongs to the module's model, the second is
   the contract published to other modules; how a module writes an integration event to its outbox
   is decided with the first event a consumer uses (ADR-ARCH-032 §1).
5. **Event schemas.** The producer owns the schema of each event it publishes, in
   `apps/api/src/modules/<Context>/events/`. Each consumer declares its own types for the events
   it consumes, and a contract test of the consumer fails when its types no longer match the
   published schema. A published event changes only by adding an optional field; any other change
   is a new, versioned event, and the previous one is still published while a consumer uses it.
   The schema format and any generation tooling are decided with the first event a consumer uses.
6. **Tests inside the module.** This replaces, for modules, the location of tests set by
   ADR-ARCH-020; its naming stands. A module's tests live in
   `apps/api/src/modules/<Context>/tests/`:
   - `SaasBtp.<Context>.Domain.Tests`: unit tests of the Domain, with no data access, web or test
     container package;
   - `SaasBtp.<Context>.Features.Tests`: unit tests of the handlers against fake ports, with no
     database and no data access package;
   - `SaasBtp.<Context>.Adapters.IntegrationTests`: the adapters against PostgreSQL migrated by
     the runner, including `MappingMatchesMigratedSchema` (ADR-ARCH-033), the tests of
     ADR-ARCH-032 §6, and the transaction rules of decision 3.

   `apps/api/tests/` keeps the tests that belong to no module: the hosts', the runner's, the
   architecture tests, and `SaasBtp.Testing`, the shared test support (the PostgreSQL image, the
   roles applied, a migrated database), created with the first module that needs it. Its name does
   not end in `Tests`, so its public types carry XML comments (ADR-ARCH-021). The image build
   context lets back in each project a host references, never a module's `tests/` folder.
7. **How each rule is enforced.** `apps/api/tests/SaasBtp.Architecture.Tests` uses ArchUnitNET with
   its xUnit v3 integration for type dependencies, and reads the source tree and project files
   where type dependencies cannot tell. It finds the modules by listing the folders under
   `apps/api/src/modules/`, and fails when one of them lacks any of its three projects or is not
   loaded by the test. Its name does not follow `<TestedProject>.Tests` (ADR-ARCH-020), since it
   tests the solution rather than one project; it still ends in `Tests`.

   | Rule | Enforced by |
   |---|---|
   | Domain depends on no data access, web or hosting type (`Microsoft.EntityFrameworkCore*`, `Npgsql*`, `Microsoft.AspNetCore*`, `Microsoft.Extensions.Hosting*`, `Microsoft.Extensions.DependencyInjection*`), and references no such package | `DomainDependsOnNoInfrastructure`: type dependencies, and the `PackageReference` items of its project file |
   | Features depends on no Adapters type; in Features, only `*Endpoint` types depend on `Microsoft.AspNetCore*`, and no `*Handler` type does | `FeaturesDependOnNoAdapters`, `OnlyEndpointsDependOnAspNetCore` |
   | No project of a module references, or depends on a type of, another module | `ModulesDoNotDependOnOneAnother`: type dependencies, and no `ProjectReference` of a module's project files resolving into another folder under `apps/api/src/modules/` |
   | A host uses a module only through `<Context>Module`, and the API host calls every module's entry point | `HostsUseOnlyModuleEntryPoints`, `ApiHostLoadsEveryModule` |
   | `<Context>Module` is the only public type of Adapters; Features types are internal; `InternalsVisibleTo` names only the projects of decision 2 | `ModuleSurfaceIsItsEntryPoint`, `InternalsVisibleToStaysInsideTheModule` |
   | No mediator and no registration by scanning, in modules and hosts (not the migration runner, whose embedded scripts need `Assembly`): no dependency on `MediatR*`, `Scrutor*` or `System.Reflection.Assembly`, and no call to `System.Type.get_Assembly` | `NoMediatorNorAssemblyScanning` |
   | No module defines its own transaction or command interceptor, and no type of a module opens a connection of its own: no call to a constructor of `NpgsqlConnection`, nor to `NpgsqlDataSource.Create*`, `OpenConnection*` or `CreateConnection`; the context is configured with `UseNpgsql` | `ModulesDefineNoInterceptorNorConnection` |
   | A command without a transaction is refused, the subject is set first, and a SQL read sees the unit of work's subject | each module's integration tests (decision 6) |
   | A module's context maps only its own schema | an assertion this ADR adds to `MappingMatchesMigratedSchema`: every entity's schema is the module's own |
   | A module's SQL names no other module's schema | `ModuleSqlNamesOnlyItsSchema`: no string passed as SQL (to `FromSql*`, `SqlQuery*`, `ExecuteSql*` or a command's text) matches `\b<schema>\.[a-z_][a-z0-9_]*\b` for the schema of another module; prose such as "no access." is not SQL and is not read |
   | Unit-test projects use no data access, web or test container package | `UnitTestsUseNoInfrastructure` |
   | A module missing from the image while the host uses it | the build and the image check of ADR-ARCH-022 |
   | A consumer's event types match the producer's schema | the consumer's contract test (decision 5), from the first consumed event |
   | No synchronous call between modules other than through a code dependency (HTTP); no authorization on a local copy; an event changed only additively and kept while consumed; a module's `tests/` folder kept out of the image build context | review, against this ADR: these cannot be detected by a test |

   The last row is the one exception to "every rule names its test or build check" (#174), for
   the maintainer to accept in review.

## Consequences
- Moving a module out moves one folder, its tests and event schemas included, with its
  `may_act_in` function, scripts and journal rows (ADR-ARCH-033). Its consumers' contract tests
  then read its schemas from wherever the extracted service publishes them; where is decided with
  the first extraction.
- A handler is unit-tested without a database, since Features cannot reach an adapter.
- A new adapter is a folder, not a project, until it needs isolation of its own.
- The image check of ADR-ARCH-022 needs no change: the host uses each module's entry point by type,
  and `ApiHostLoadsEveryModule` catches a module the host forgets.
- `SaasBtp.slnx` lists each module's projects and tests; CI runs them from it unchanged.
- The tests' location in ADR-ARCH-020 is replaced for modules; its naming and other rules stand.
  `apps/api/CLAUDE.md` and the layout comment of `Directory.Build.props` are aligned.
- `architecture.md` §4 is aligned: no internal architecture imposed beyond decision 1, no
  mediator, and its own rule on audit fields and soft delete removed in favour of `sql.md` and
  `naming.md`, which differ on mutable entities and are reconciled separately.

## Alternatives considered
- One project per module, its layers as folders checked by architecture tests: the decoupling of
  Domain and Features from adapters would no longer be enforced by the compiler.
- Archetypes per kind of module (a richer one for domain modules, a flat one for sinks): no module
  needs another shape yet; a second archetype is added by an ADR when one does.
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
- kgrzybek/modular-monolith-with-ddd at `91c8ef2`; the maintainer's mini-ged at `00a6689`.

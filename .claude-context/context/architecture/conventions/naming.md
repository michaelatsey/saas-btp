# Naming Conventions (project-wide standard)

Status: accepted (2026-06-28). Reusable across all projects.
Principle: each layer keeps its idiomatic convention; boundaries translate
automatically. A field is named ONCE (in C#) and flows everywhere.

---

## Per-layer conventions

| Layer | Convention | Example |
|-------|-----------|---------|
| C# code / domain model | PascalCase | `ObserverName`, `Severity` |
| JSON / API wire (contract boundary, ADR-ARCH-018) | camelCase | `observerName` |
| TypeScript (generated from OpenAPI) | camelCase | `observerName` |
| PostgreSQL columns / tables | snake_case | `observer_name`, `safety_constat` |
| Enum values on the wire | camelCase string tokens | `dangerousSituation` |
| UI labels | localized (FR first), i18n layer | "Nom de l'observateur" |

A module's persistence adapter (`SaasBtp.<Context>.Adapters/Persistence/`) is laid out by
aggregate by default: one folder per aggregate, named after it in the plural (`Profiles/`,
`Organizations/`), holding its `IEntityTypeConfiguration<T>` (`<Aggregate>Configuration`), its
repository (`<Aggregate>Repository`) and the converters of its identifiers and values
(`<Aggregate>Converters.cs`). The mapping every table shares (schema, lengths, key and audit
columns) is one internal class beside the context. The context applies each configuration by one
explicit `ApplyConfiguration`, never `ApplyConfigurationsFromAssembly` (ADR-ARCH-034 decisions 2
and 7), and keeps `ConfigureConventions`, since a conversion applies to the whole model. The
persistence of a process that writes several aggregates has a folder of its own, named after the
process (`Founding/`), and the reads that decide nothing have `Queries/` (Ports and their adapters,
below).

A module's Features project registers and maps its own slices in two internal static classes at its
root: `FeatureRegistration.AddFeatures`, the handlers, and `FeatureEndpoint.MapFeatures`, the
endpoints, so that only an `*Endpoint` type depends on ASP.NET Core (`OnlyEndpointsDependOnAspNetCore`,
ADR-ARCH-034 decision 7). The module's entry point calls both explicitly, with no scanning
(ADR-ARCH-034 decision 2). A slice is a folder `<Feature>/<UseCase>/` holding its `*Endpoint` and its `*Handler`
(ADR-ARCH-034 decision 1); adding one changes its folder and these two classes only.

---

## Ports and their adapters

A port is named after the role it plays for its use case, never after the unit of work it runs
in. Ports live where ADR-ARCH-034 decision 1 puts them: a repository's in Domain, every other one
in the Features project's `Ports/`; their implementations in the Adapters project, one folder per
adapter.

- **A domain service** carries a business name, after the rule it holds, never after a pattern:
  `CompanyFounding` (`SaasBtp.Access.Domain/Founding/`). It lives in Domain, which holds the
  business rules (ADR-ARCH-034 decision 1); the name is this repository's own choice.
- **The persistence of one aggregate** is `I<Aggregate>Repository`, in Domain, implemented by
  `<Aggregate>Repository` under `Persistence/<Aggregates>/` (`IProfileRepository`,
  `ProfileRepository`, `Persistence/Profiles/`): one repository per aggregate, whose interface
  comes from the domain model, with the EF Core `DbContext` as the unit of work
  ([Implement the infrastructure persistence layer with Entity Framework Core](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-implementation-entity-framework-core)).
- **The persistence of a process that writes several aggregates** is `I<Process>Store`, a port of
  Features, implemented by `<Process>Store` under `Persistence/<Process>/`: the founding's
  `IFoundingStore`, implemented by `FoundingStore` under `Persistence/Founding/`. "Store" is the
  term .NET uses for a persistence abstraction over a set of data, as in `IUserStore<TUser>`, "an
  abstraction for a store which manages user accounts"
  ([IUserStore<TUser> Interface](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.identity.iuserstore-1));
  that page is cited for the term only, not as a recommendation for a process port.
- **The reads that decide nothing** (ADR-ARCH-032 §5: what the subject sees is what the policies
  let it read) are `I<Context>Queries`, a port of Features, implemented by `<Context>Queries` under
  `Persistence/Queries/`: `IAccessQueries`, implemented by `AccessQueries`. The name, and keeping
  the reads apart from the transactional side, follow the simplified CQRS of
  [Implement reads/queries in a CQRS microservice](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads)
  (`OrderQueries : IOrderQueries`); the folder `Queries/` is a sub-folder of the `Persistence/`
  adapter folder of ADR-ARCH-034 decision 1, not something that page prescribes.
- **Any other port** is named after what it gives the use case: `ICaller`, the person who submits
  the request (ADR-ARCH-038 decision 5), implemented by `TokenCaller` under `Caller/`. It lives in
  the Features project's `Ports/` (ADR-ARCH-034 decision 1); the name is this repository's own
  choice.
- **"Unit of work"** names only the transaction of ADR-ARCH-033 §4, which MicroKit's `IUnitOfWork`
  carries out, never a module type. A store may run several units of work: the founding retries a
  profile conflict in a new one (contract.md §4) and reads the current ownership in another
  (contract.md §3).

Two architecture tests of `SaasBtp.Architecture.Tests` check this rule on every module project:
`ModuleNamingTests.NoModuleTypeIsNamedAfterAUnitOfWork` fails when a type's name ends in
`UnitOfWork`, and `ModuleNamingTests.PersistencePortsAreStoresOrQueriesNamedLikeTheirAdapter` fails
when an interface of a Features `Ports` namespace that an Adapters `Persistence` class implements is
not named `I<Name>Store` or `I<Name>Queries`, or when that class is not named after it without `I`.
A name that contains `UnitOfWork` elsewhere than at its end, the domain-service name, the repository
name, the other ports' names and the folders are checked in review, because no test checks them.

---

## How translation happens (named once)

- C# model is the single authored source (PascalCase).
- System.Text.Json serializes to camelCase on the wire (ASP.NET Core default).
- EF Core maps to snake_case columns via a naming convention (snake_case provider).
- OpenAPI document is produced from the C# model -> TypeScript generated in camelCase.
- UI labels live in i18n resources, never as field identifiers.

Never hand-rename a field per layer. If a name appears twice by hand, it is a smell.

---

## Field naming rules

- Identifiers: `id`; foreign references: `<entity>Id` (e.g. `siteId`, `assigneeId`).
- Booleans: `is` / `has` prefix (e.g. `isClosed`, `hasPhotos`).
- Timestamps: ISO 8601, stored UTC; names like `createdAt`, `updatedAt`,
  `observedAt`, `dueAt`, `closedAt`.
- Audit fields: `createdAt`, `createdBy`, `updatedAt`, `updatedBy` (columns
  `created_at`, `created_by`, `updated_at`, `updated_by`). The soft-delete mark is
  MicroKit's shadow property `AggregateDeletedAt` (column `aggregate_deleted_at`),
  never a member of the domain type. Which types carry them, and when, is stated
  once in `sql.md` (Audit / lifecycle). The aggregate version is MicroKit's shadow
  property `AggregateVersion` (column `aggregate_version`), per `sql.md`
  (Optimistic concurrency).
- Collections: plural (`photos`, `comments`).

---

## Enum value style

- camelCase string tokens on the wire (not integers, not SCREAMING_CASE).
- Stable: never reuse or repurpose a token; add new ones, deprecate old ones.
- Exception — persisted closed-set tokens: when a closed-set Value Object's token IS the stored
  value and the DB CHECK constraint (e.g. the Safety Constat `type` / `severity`:
  `dangerous_situation`), that snake_case token stays identical end-to-end, wire included. The
  property-naming (camelCase) conventions above govern property names and true enum values; they do
  NOT apply to these persisted tokens — a second, camelCase wire form would need a mapping table for
  no benefit.

---

## Specifications

Spec files (`specifications/**`) document fields in camelCase (the contract
convention) with a French UI-label column. They do NOT use C# PascalCase or DB
snake_case — those are derived, not authored in specs.

---

## Aggregate identifiers

- Format: UUIDv7 (RFC 9562).
- Storage: PostgreSQL `uuid`.
- Domain representation: a strongly-typed Value Object
  (`readonly record struct XId(Guid Value)`). A raw `Guid`/`string` is never used
  as an identifier in Domain.
- Generation: at aggregate creation time, by the creator of the aggregate.
- Client-generated identifiers: an aggregate whose creation may be sent more than once
  is identified by the client, before the first send. This covers offline-created
  aggregates (field capture) and online creations the client may retry (founding a
  company, BP-001). The id doubles as the idempotency key:
  - A replayed create returns the existing resource, never 409. The replay's payload is
    not compared with the original. Who may replay is defined by the business process
    of the creation; when it says nothing, any requester who can see the resource may.
  - Any other create whose id already identifies a resource is refused with 409. The
    response says only that the id is in use, nothing about the resource. The client
    generates a new id and may send again.
  - A creation that produces several aggregates at once names, in its business
    process, the aggregate whose id is the key (for example, BP-001 names the
    organization).
- Rationale: time-ordered → B-tree locality on append-heavy tables + free chronological
  sort; native in .NET 10 (`Guid.CreateVersion7`) and Postgres; generatable in JS for the
  offline client.
- To validate at persistence wiring: .NET v7 has no intra-ms monotonic counter and an
  endianness quirk — verify the Npgsql `Guid`→`uuid` mapping preserves order.

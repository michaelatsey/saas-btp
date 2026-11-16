# MicroKit.Persistence

MicroKit's persistence: the repository and unit-of-work contracts, the core read-side types, the
EF Core implementation, its PostgreSQL provider and the persistence analyzer, copied into this
repository as source (ADR-ARCH-035, ADR-ARCH-037). The copy is this repository's own: it is not
synchronized with MicroKit in either direction (ADR-ARCH-035 §1).

## Source

- Repository: `michaelatsey/microkit`, folder `modules/MicroKit.Persistence/`.
- Commit: `a12097b48c7bd00314ea11bc6c7f667d28962bdd`, committed 2026-11-12T00:18:35Z.
- Read as the head of `main` at 2026-11-16T12:57:50Z, with
  `gh api repos/michaelatsey/microkit/commits/main` (ADR-ARCH-037 §1).
- MicroKit's own documentation of the library, not copied:
  `https://github.com/michaelatsey/microkit/blob/a12097b48c7bd00314ea11bc6c7f667d28962bdd/modules/MicroKit.Persistence/README.md`.

## What is here

| Here | Copied from |
|---|---|
| `src/MicroKit.Persistence.Abstractions/` | `src/MicroKit.Persistence.Abstractions/` |
| `src/MicroKit.Persistence/` | `src/MicroKit.Persistence/` |
| `src/MicroKit.Persistence.EntityFrameworkCore/` | `src/MicroKit.Persistence.EntityFrameworkCore/` |
| `src/MicroKit.Persistence.EntityFrameworkCore.PostgreSql/` | `src/MicroKit.Persistence.EntityFrameworkCore.PostgreSql/` |
| `src/MicroKit.Persistence.Analyzers/` | `src/MicroKit.Persistence.Analyzers/` |
| `tests/MicroKit.Persistence.Tests/` | `tests/MicroKit.Persistence.UnitTests/` (`Query/`, `Results/`) |
| `tests/MicroKit.Persistence.EntityFrameworkCore.IntegrationTests/` | `tests/MicroKit.Persistence.IntegrationTests/` |
| `tests/MicroKit.Persistence.Analyzers.Tests/` | `tests/MicroKit.Persistence.Analyzers.Tests/` |
| `tests/MicroKit.Persistence.Architecture.Tests/` | `tests/MicroKit.Persistence.ArchitectureTests/` |

Not copied:
- `MicroKit.Persistence.Specifications`, `MicroKit.Persistence.Testing`,
  `MicroKit.Persistence.Testing.PostgreSql` and `MicroKit.Persistence.Testing.SqlServer`,
  copied only when a decision needs them (ADR-ARCH-035 §2).
- The SQL Server provider and `samples/` (ADR-ARCH-035 §2).
- `.claude/`, `.claude-context/`, `CHANGELOG.md`, `MicroKit.Persistence.slnx` and `version.json`.

The PostgreSQL provider depends on the EF Core project, which depends on `MicroKit.Persistence`.
`MicroKit.Persistence` depends on `MicroKit.Persistence.Abstractions` and `MicroKit.Result`, and
the abstractions on `MicroKit.Domain` (`../MicroKit.Domain/`, `../MicroKit.Result/`).

## Changes made here

Project files (ADR-ARCH-035 §5, §6):

- The package properties are removed from every project: `Description`, `PackageTags`,
  `IncludeBuildOutput`, `IncludeSymbols` and the analyzer's `Pack` item. So are the properties
  equal to the root `Directory.Build.props`. MicroKit's root `Directory.Build.props` and
  `Directory.Packages.props` are not copied.
- `IsTrimmable` is not kept, so the `IL2091` `NoWarn` of the EF Core project and of the PostgreSQL
  provider, which only the trim analyzer reported, is removed with it.
- `MicroKit.Persistence` grants its internals to `MicroKit.Persistence.EntityFrameworkCore` and
  `MicroKit.Persistence.Tests`. The grant to `MicroKit.Persistence.Testing`, not copied, is
  removed, and the test project's grant follows its new name.
- The PostgreSQL provider references `Npgsql` directly, as the root `Directory.Packages.props`
  requires wherever PostgreSQL is reached.
- The analyzer keeps `netstandard2.0`, `EnforceExtendedAnalyzerRules` and the `RS2008` `NoWarn`:
  MicroKit ships no release-tracking files for it, and the build reports 18 RS2008 errors
  without the suppression. Its `NoWarn` now appends to the root's instead of replacing it.
  `NU5128`, a packing warning, and `IsTrimmable=false` are removed.
- The analyzer references `Microsoft.CodeAnalysis.CSharp` with `PrivateAssets="all"`, and no other
  project references it (ADR-ARCH-035 §5).
- The analyzer's help links (`src/MicroKit.Persistence.Analyzers/HelpLinks.cs`) pointed at MicroKit's
  README on its `main` branch. Every diagnostic's link now opens its own section of this README,
  `https://github.com/michaelatsey/saas-btp/blob/main/libraries/MicroKit.Persistence/README.md#mkpnnn`
  (Diagnostics, below): the copy is dedicated and not synchronized (ADR-ARCH-035 §1).
  `HelpLinkTests.EveryHelpLink_TargetsTheReadmeAnchorOfItsId` asserts the links, and failed on the
  code as copied; `HelpLinkTests.EveryHelpLinkAnchor_IsTheAnchorOfExactlyOneReadmeHeading` asserts
  that each link has exactly one heading of this README behind it.
- A module's Adapters project references the analyzer as an analyzer
  (`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`), and no test project does
  (ADR-ARCH-035 §3, §9). Its own tests reference it by a plain project reference, as the assembly
  under test, so it does not run on their compilation: ADR-ARCH-035 §9 forbids a test project to
  reference the analyzer as an analyzer (`OutputItemType="Analyzer"`).

Tests (ADR-ARCH-021, ADR-ARCH-035 §7):

- The test projects take the names of ADR-ARCH-020 and ADR-ARCH-035 §7, and run on `xunit.v3`.
  `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`,
  `Shouldly`, `NetArchTest.Rules`, the unused `NSubstitute` and
  `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` are removed, with `GenerateDocumentationFile`
  and the CS1591 `NoWarn`.
- `NoWarn`: `CA1707` stays suppressed in each test project (MicroKit's test names use
  underscores). MicroKit's `IL2026`, `IL2070`, `IL2091`, `CS0618` and `NU1701` are not kept: the
  build reports none of them.
- Shouldly assertions are rewritten with xUnit's `Assert`:
  - a string `ShouldNotContain` becomes an ignore-case `Assert.DoesNotContain`, Shouldly's
    default;
  - Shouldly's `Should.ThrowAsync<Exception>` accepted a derived type, so
    `ReadSideTests.ASortKeyTheCursorCannotCarry_IsRefused_NamingIt` and two tests of
    `EfUnitOfWorkRollbackTests` use `Assert.ThrowsAnyAsync<Exception>`;
  - `Present.NotNull`, added to the integration tests, returns the value it checks, as
    Shouldly's `ShouldNotBeNull` did; xUnit's `Assert.NotNull` returns nothing for a reference
    type.
- The copied tests cite MicroKit's own ADRs and `.claude/` rule files, which this repository does
  not hold. Each citation in a comment is qualified in the form
  `MicroKit ADR-004 D1 (michaelatsey/microkit@a12097b)`, and so is each rule file, so that a bare
  number cannot be read as an ADR of this repository.
- xUnit's analyzer code fixes are applied with `dotnet format`:
  - xUnit1051: the test's cancellation token is passed;
  - xUnit2012: `Assert.DoesNotContain` over a predicate;
  - xUnit2013: `Assert.Single`.

  The NetArchTest rules of `NamespaceConventionTests` are rewritten on ArchUnitNET.
- `MicroKit.Persistence.Tests`:
  - It keeps `Query/` and `Results/`, and references `MicroKit.Persistence` directly: MicroKit
    reached it through Specifications and Testing.
  - `Testing/` and `Docker/` (they test MicroKit.Persistence.Testing) are dropped, and so are
    `QueryOptionsOrderingExtensionsTests` and `QueryOptionsSpecificationExtensionsTests` (they
    test MicroKit.Persistence.Specifications).
  - Two tests of `QueryOptionsExtensionsTests` built their options with Specifications'
    `WithOrderBy`, and now use core's `WithOrdering`, which stores the same ordering.
  - `PaginationOptionsTests` uses an aggregate of its own instead of Testing's `TestItem`.
- `MicroKit.Persistence.EntityFrameworkCore.IntegrationTests`:
  - The SQL Server suites are dropped (ADR-ARCH-035 §2):
    - the six `*SqlServerTests` classes over the shared `*Scenarios` and `SqlServerSuiteFixture`;
    - the seven `sqlserver` rows of `ProviderOptionsTests`;
    - a SQL Server branch of a check constraint in `PersistenceResultTests`.
  - `Testing/RelationalFixtureTests` (it tests the fixture packages) is dropped.
  - `Docker/` is dropped. Its `DockerRequiredFact` and `DockerRequiredTheory` skipped the tests
    without Docker, and their 181 uses become `Fact` and `Theory`: a missing Docker fails, never
    skips.
  - `PostgreSqlSuiteFixture` is rewritten on `Testcontainers.PostgreSql`, with the image every test
    of this repository starts: `apps/api/tests/PostgresImage.cs`, PostgreSQL 17.6, ICU, `en-US`,
    linked into the project. MicroKit used `postgres:17-alpine`.
  - `DomainConventionTests` compiled an invalid `EntityId` in memory with Roslyn. It now emits
    the type with `System.Reflection.Emit`: a sealed class deriving from
    `EntityId<TextOnlyId>` whose only constructor takes a string, not a record, with throwing
    overrides of the base record's abstract members. The test asserts the same failure as at
    `a12097b`, and does: run against the copied library, both fixtures make
    `AddEntityIdConventions` throw `InvalidOperationException` with the same message
    (`'Generated.TextOnlyId' derives from EntityId<TextOnlyId> but declares no constructor taking a
    single Guid, ...`). The only test project that references Roslyn
    is the analyzer's own (`MicroKit.Persistence.Analyzers.Tests`, below).
  - `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9 brings the SQLitePCLRaw packages at 2.1.11, and
    restore reports advisory GHSA-2m69-gcr7-jv3q (CVE-2025-6965) on `SQLitePCLRaw.lib.e_sqlite3`
    2.1.11: the advisory covers the package up to 2.1.11, and this repository treats it as an
    error. MicroKit suppressed the advisory. This project references every SQLitePCLRaw package
    the restore resolves (`bundle_e_sqlite3`, `core`, `lib.e_sqlite3`, `provider.e_sqlite3`) at
    2.1.14 instead: 2.1.12 moves the native library to SQLite 3.53.3, 2.1.13 drops `win-arm` from
    its targets, 2.1.14 gives the providers' callbacks an explicit `Cdecl` calling convention.
- `MicroKit.Persistence.Analyzers.Tests`:
  - It references `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.0.0 directly: transitive pinning
    is off, and the testing package would otherwise compile it against Roslyn 1.0.1.
  - `HelpLinkTests.EveryHelpLinkAnchor_IsTheAnchorOfExactlyOneReadmeHeading` read MicroKit's
    README, found through `MicroKit.Persistence.slnx`. It reads this README instead, which the
    project file copies to the test output (the build fails if it is missing); `FindModuleRoot`
    goes with the `.slnx` search. `HeadingAnchors` (the GitHub slug rule) and its two tests are
    kept as copied.
- `MicroKit.Persistence.Architecture.Tests`:
  - `PackageClosureTests` (the Testing packages), `ReadmeSamplesTests` (README and samples) and
    `IlCallScanTests.TheFixtures_AndTheirLifecycle_NeverConsultTheDockerProbe` are dropped.
  - The tests and assertions on Testing, Testing.*, Specifications and the SQL Server provider
    are removed from `LayerDependencyTests`, `ContractPlacementTests`, `SealedClassTests`,
    `ReadRepositoryPurityTests` and `NamespaceConventionTests`.

Defects fixed in the copied test code, which documentation generation now reports (the root
`Directory.Build.props` generates XML documentation in test projects, and MicroKit's turned it
off):

- `Analyzers.Tests/Stubs/QueryTest.cs`: a `paramref` to a parameter the class does not have
  (CS1734).
- `EntityFrameworkCore.IntegrationTests/EntityFrameworkCore/EfRepositoryAggregateLoadTests.cs`:
  an ambiguous `cref` to `EfRepository.FindAsync` (CS0419).

No defect of the library code was found by the copied tests or the review. The one change to the
library's code, the help links of `HelpLinks.cs`, is a change made here for this copy (above), not
a defect fix.

## Diagnostics

The analyzer's rules, one section per diagnostic ID. The help link of each diagnostic opens its
section here (`src/MicroKit.Persistence.Analyzers/HelpLinks.cs`). The text is MicroKit's, from its
README at `a12097b`, and describes the copied analyzer, whose sources are identical to that commit.

### MKP001

**Error — a read repository calls `CommitAsync` or `SaveChangesAsync`.** A type implementing
`IReadRepository<T>` calls `CommitAsync`, `SaveChangesAsync` or `SaveChanges`. The read side never commits: a
query handler that writes breaks the CQRS split, and its write escapes the unit of work. Move the write to a
command handler, through `IRepository<TAggregate, TId>` and `IUnitOfWork`.

### MKP002

**Error — a read repository declares or calls a write method.** A type implementing `IReadRepository<T>`
declares or calls `AddAsync`, `UpdateAsync` or `DeleteAsync`. Writes belong to `IRepository<TAggregate, TId>`;
keep the read repository to finds, lists, pages, `AnyAsync` and `CountAsync`.

### MKP003

**Warning — `SaveChanges` called directly on a `DbContext`.** A call to `SaveChanges` or `SaveChangesAsync` on a
`DbContext`, outside a type implementing `IUnitOfWork`, bypasses the unit of work and its error translation.
Commit through `IUnitOfWork.CommitAsync`. A `DbContext` that overrides `SaveChangesAsync` is reported too;
suppress the site with `#pragma warning disable MKP003`.

### MKP004

**Warning — a `DbContext` injected outside the infrastructure layer.** A constructor takes a `DbContext` in a
class whose namespace contains none of `Infrastructure`, `Persistence`, `EntityFrameworkCore`, `Repository`,
`Data`. Handlers take repositories, not the context. A heuristic: suppress it for infrastructure that lives
in another namespace.

### MKP005

**Error — a repository method returns `IQueryable<T>`.** A public or internal method of a repository returns
`IQueryable<T>` (or a task of one): the caller composes the query and the repository no longer owns what
runs. Return a materialized result, a page or a projection.

### MKP006

**Info — one query includes two or more collections without a split decision.** Two collection includes in
one query make one SQL query that repeats the rows of one collection for each row of the other (a cartesian
product). `AsSplitQuery()` loads each collection in its own query — the reads are then not one snapshot —
and `AsSingleQuery()` keeps one query on purpose. On `QueryOptions`, the options' `AsSplitQuery()` settles it.
One collection, a reference include, `Include(string)` and includes EF Core ignores (a projection, a count)
are not reported.

### MKP007

**Warning — a query runs on each iteration of a loop.** A LINQ executor over a query, an EF Core async
executor, `Find`, explicit loading, a nested `foreach` over a query or a repository read inside a `foreach`,
`await foreach` or `for` loop costs one round trip per element. Load what the loop needs in one query before
it (`Contains` over the keys, or an include). Composing a query in a loop, staging (`Add*`, `Delete*`,
`Remove*`), bulk `Execute*` writes, `ListKeysetAsync`/`ListPagedAsync` page walks, `while`/`do` loops, a `for`
without a condition and code inside a lambda or a local function are not reported.

### MKP008

**Warning — a query is materialized before it is filtered.** `ToList()`/`ToArray()` (or an awaited
`ToListAsync`/`ToArrayAsync`) on a query from a `DbSet`, directly followed by an in-memory filter
(`Where`, `First`, `Any`, `Count`, …) whose predicate would translate, loads every row and filters it in
memory. Move the filter before the materialization. It is reported only when the move is plausible: no
`Take`, `Select` or join before it, a predicate over public, settable, mapped scalar properties. Check the
move by hand — case, culture, precision and unsaved tracked values can differ between memory and SQL.
`AsEnumerable()` marks a deliberate in-memory filter.

### MKP009

**Warning — `IgnoreQueryFilters()` disables every query filter.** The parameterless overload ignores every
filter of the model, tenant filters included. Name the filters to ignore
(`IgnoreQueryFilters(["name"])`), or use `QueryOptions.WithDeleted()` for soft-deleted aggregates. It is a
warning, not an error: an anonymous filter can only be ignored by the parameterless overload.

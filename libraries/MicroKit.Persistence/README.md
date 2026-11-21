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
| `tests/MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests/` | nothing: written here (Library changes, below) |

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

No defect of the library code was found by the copied tests or the review. The help links of
`HelpLinks.cs` are a change made here for this copy (above), not a defect fix. The changes this
repository makes to the library itself are listed under Library changes, below.

## Library changes

Changes made here to the library's behaviour, each by its own pull request with the tests that
prove it (ADR-ARCH-035 §8).

### The subject and transaction interceptors (issue #211)

Every context registered by `UsePostgreSQL` refuses any command outside a transaction, and every
transaction it begins sets the subject as its first statement (ADR-ARCH-032 §3,
ADR-ARCH-033 §4). In `src/MicroKit.Persistence.EntityFrameworkCore.PostgreSql/`:

- `Transactions/IUnitOfWorkSubject.cs`, public: the port through which the interceptors obtain the
  subject of the current unit of work, a UUID or none (ADR-ARCH-038 decisions 1 and 3). The host
  implements it from the validated token.
- `Transactions/SubjectTransactionInterceptor.cs`: once a transaction has started, in the
  synchronous and asynchronous callbacks, it sends `select set_config('app.user_id', @subject,
  true)` through ADO.NET on the transaction's own connection, the subject as a text parameter,
  before EF Core returns the transaction and so before any other command or savepoint. Without a
  subject (the port returns none, or no port is registered), or when the statement fails, it rolls
  the transaction back and disposes it, and the start fails with `InvalidOperationException`. EF
  Core leaves open the connection it opened to begin the transaction, until the context closes it
  or is disposed.
- `Transactions/TransactionRequiredCommandInterceptor.cs`: every reader, non-query and scalar
  command, synchronous or asynchronous, that EF Core sends without a transaction is refused with
  `InvalidOperationException` before it reaches the database. Inside `SaveChanges`, EF Core wraps
  the refusal in a `DbUpdateException`.
- `Transactions/NoAutomaticTransactionInterceptor.cs`: before each save, sets
  `Database.AutoTransactionBehavior` to `Never`. The setting exists only on the context's
  `DatabaseFacade`, not in its options; without it, a `SaveChanges` of several statements outside
  a unit of work would begin a transaction of its own, which would receive a subject.
- `DependencyInjection/PostgreSqlEfCoreBuilderExtensions.cs`: `UsePostgreSQL` adds the three
  interceptors to every context it registers. The port is resolved from the scope that resolves
  the context or, with `PoolSize` set, once from the root provider; a pooled registration then
  refuses, with `InvalidOperationException` when the first context is resolved, a port registered
  with a lifetime other than singleton, which would give every lease the same captured subject.
  The method's signature is unchanged.

Not in this change: the refusal of a transaction the unit of work did not open, and the unit of
work's one transaction (issue #212, below); the host's implementation of the port (issue #217).

Behaviour that changes as a consequence:

- A transaction passed with `Database.UseTransaction` received no subject: the interceptor acts
  when a transaction starts, and EF Core reports a transaction it is given as used, not started.
  Issue #212 refuses such a transaction as soon as it is used (below).
- `EfUnitOfWork.CommitAsync` and `TryCommitAsync` called outside `ExecuteAsync` flush outside a
  transaction, so the save is refused: they throw a `PersistenceException` ("Database error during
  commit") whose inner `DbUpdateException` holds the refusal. `TryCommitAsync` refuses to run
  inside a transaction, so it has no successful use on this provider. A unit of work commits
  through `ExecuteAsync` or `TryExecuteAsync`.

Tests:

- `tests/MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests/`, a project of
  this repository named after the provider it tests (ADR-ARCH-020, ADR-ARCH-035 §7), on the
  PostgreSQL of `apps/api/tests/PostgresImage.cs`. Its contexts are registered through
  `UsePostgreSQL`, with the Npgsql driver logging each command it sends, with its parameters, so
  that a test reads what reached the database. `SubjectInterceptionTests`:
  - `ACommandWithoutATransactionIsRefusedAndReachesNoDatabase`: a query, a `SaveChanges` of two
    rows, a raw insert and a scalar command (`IRelationalDatabaseCreator.HasTables`), each
    synchronous and asynchronous, with a subject available: refused, no transaction begun, no
    command sent, no row written.
  - `TheFirstCommandOfATransactionSetsTheSubjectAsAParameter`: through `BeginTransaction`,
    `BeginTransactionAsync` and the unit of work's `ExecuteAsync`, the first command sent is the
    `set_config` (as Npgsql sends it, `$1` in place of `@subject`), with the subject as its only
    parameter and not in its text; the transaction then reads the subject.
  - `ARollbackToASavepointKeepsTheSubject`, synchronous and asynchronous.
  - `ATransactionStartedWithoutASubjectFailsWithAnError`: a port returning none, and no port,
    each synchronous and asynchronous: the start fails, no command is sent, and once there is a
    subject the same context begins a transaction.
  - `OnOnePooledConnectionTheSubjectDoesNotOutliveItsTransaction`: after a committed and after a
    rolled-back transaction, on one physical connection (`Maximum Pool Size=1`) that Npgsql does
    not reset (`No Reset On Close=true`), the next transaction, begun on the connection itself,
    reads `nullif(current_setting('app.user_id', true), '')` as NULL.
  - `OnAPooledContextEachTransactionReadsTheSubjectAgain`, `EachScopeSetsItsOwnSubject` and
    `WithAPoolSizeASubjectPortThatIsNotASingletonIsRefused`.
  - `AUnitOfWorkCommitOutsideATransactionIsRefusedAndWritesNothing`: the second consequence
    above. The first was shown by `ATransactionTheContextDidNotBeginReceivesNoSubject`, which
    issue #212 replaces with a test of the refusal.
- Copied tests adapted, since they register their context through `UsePostgreSQL`: the
  PostgreSQL registrations of `AggregateBoundaryTests` and `PersistenceResultTests` register a
  fixed subject (`PostgreSql/FixedSubject.cs`, added here), and the two pooled tests of
  `AggregateBoundaryTests` (`OnAPooledContext_...`) run each lease's reads inside a transaction.
  What they assert is unchanged.

### The unit of work's one transaction (issue #212)

The unit of work runs every command in the one explicit transaction it opened, and refuses a
transaction it did not open (ADR-ARCH-033 §4, ADR-ARCH-035 §8), so that a founding saved in
several `SaveChanges` commits whole or not at all (ADR-ARCH-033 §5).

- `src/MicroKit.Persistence.EntityFrameworkCore/Transactions/EfUnitOfWork.cs`: the unit of work
  records the transaction it begins in `ExecuteAsync` and `TryExecuteAsync` while it is open. A
  nested call of the same unit of work joins that transaction, as before. Any other open
  transaction, begun on the context or passed in with `Database.UseTransaction`, is refused with
  `InvalidOperationException` by `ExecuteAsync`, `TryExecuteAsync` and `CommitAsync` before
  anything runs, and is left to its owner, neither committed nor rolled back. MicroKit joined any
  open transaction. `TryCommitAsync` keeps its refusal of any open transaction.
- `src/MicroKit.Persistence.Abstractions/Transactions/ITransactionalContext.cs`,
  `src/MicroKit.Persistence.Abstractions/Transactions/IUnitOfWork.cs` and
  `src/MicroKit.Persistence/Results/IResultUnitOfWork.cs`: their documentation now describes the
  join of the unit of work's own transaction and the refusal of any other. Their signatures are
  unchanged.
- `src/MicroKit.Persistence.EntityFrameworkCore.PostgreSql/Transactions/UsedTransactionInterceptor.cs`:
  `Database.UseTransaction` and `UseTransactionAsync` are refused with
  `InvalidOperationException` as soon as the transaction is used. The context did not begin it, so
  its subject was never set. The refusal comes before the context takes the transaction:
  `Database.CurrentTransaction` stays null, and a later command is refused as outside a
  transaction. The connection and the transaction stay as the caller had them, open. EF Core
  counts an open of the connection before it reports the transaction as used; the interceptor
  closes once before refusing, so that the caller's own `CloseConnection` still closes it.
- `src/MicroKit.Persistence.EntityFrameworkCore.PostgreSql/DependencyInjection/PostgreSqlEfCoreBuilderExtensions.cs`:
  `UsePostgreSQL` adds that interceptor to the three of issue #211.

A transaction begun directly on the context, outside any unit of work, still sets its subject
first (issue #211); the unit of work refuses it only when it is called inside it.

Tests, against the PostgreSQL of `apps/api/tests/PostgresImage.cs`:

- `tests/MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests/UnitOfWorkTransactionTests.cs`,
  through `UsePostgreSQL` and `AddUnitOfWork`, with a subject:
  - `TwoSaveChangesAndAQueryRunInOneTransactionAndCommitTogether`, through `ExecuteAsync` and
    `TryExecuteAsync`: one transaction begun, the same `txid_current()` after each save, the query
    sees the first row, one connection, both rows committed.
  - `TwoSaveChangesRollBackTogether`: the operation throws after both saves, or `TryExecuteAsync`
    returns a failed result after them: no row.
  - `AFailureAfterTheFirstSaveChangesLeavesNoRowOfEither`: the second save fails at the database
    (a duplicate key): `PersistenceException`, no row.
  - `AFailureAtCommitLeavesNoRowOfEitherAndTheNextUnitOfWorkBeginsAgain`: a unique constraint
    deferred to commit, as the founding's keys are, fails at COMMIT: `PersistenceException`, no
    row, and the same unit of work then begins and commits a transaction of its own.
  - `ANestedCallJoinsTheUnitOfWorksOwnTransaction`: `ExecuteAsync` and `TryExecuteAsync` inside
    `ExecuteAsync`: one transaction, three rows.
  - `AUnitOfWorkRefusesATransactionBegunOnTheContext` and
    `AUnitOfWorkRefusesATransactionPassedInThroughUseTransaction`, each through `ExecuteAsync`,
    `ExecuteAsync` with a result, both `TryExecuteAsync` and `CommitAsync`: refused, the operation
    not run, the transaction still the context's current one and its owner's to end, no row
    written once it commits. The second uses a context built with `UseNpgsql` alone, since
    `UsePostgreSQL` refuses `UseTransaction` itself.
- `SubjectInterceptionTests.ATransactionPassedInThroughUseTransactionIsRefused`, synchronous and
  asynchronous, replaces `ATransactionTheContextDidNotBeginReceivesNoSubject` (issue #211):
  refused, no current transaction, no command sent, the connection still open, a query
  afterwards refused as outside a transaction, and the caller's `CloseConnection` closing the
  connection.
- Copied tests changed: in
  `tests/MicroKit.Persistence.EntityFrameworkCore.IntegrationTests/PostgreSql/EfUnitOfWorkJoinTests.cs`,
  `ExecuteAsync_InsideACallerTransaction_JoinsIt_AndLeavesItToTheCaller` and
  `ExecuteAsync_InsideACallerTransaction_WhenTheOperationThrows_LeavesItOpenAndNotRolledBack`
  asserted that the unit of work joins a transaction its caller opened, which this change removes.
  A refused operation never runs, so a throwing operation adds nothing: the two become one test,
  `ExecuteAsync_InsideACallerTransaction_IsRefused_AndLeavesItToTheCaller`, with the default and a
  retrying strategy. The nested-call tests of that class are unchanged.
- `tests/MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests/PostgreSqlServer.cs`:
  `TestDatabase.ExecuteAsync` runs a statement of a test's set-up on a connection no context owns.

### The SQL read of the unit of work (issue #213)

A read in SQL obtains the connection and transaction of the unit of work through the library, so
that it sees the unit of work's subject and never opens a connection of its own (ADR-ARCH-034 §3,
ADR-ARCH-035 §8). It takes its values only as parameters (ADR-ARCH-032 §3).

- `src/MicroKit.Persistence.EntityFrameworkCore/Transactions/IUnitOfWorkSqlQuery.cs`, public:
  `IUnitOfWorkSqlQuery<TContext>.SqlQueryAsync<TElement>(FormattableString sql, CancellationToken ct)`
  returns the rows of a read, materialized and not tracked. It names its context, so that each
  module resolves the read of its own unit of work. No overload takes a `string`: written as an interpolated string, each
  value is sent as a parameter. A format built at run time (`FormattableStringFactory.Create` over
  concatenated text) is not parameterized and must not be used. The read cannot refuse it: at run
  time it cannot be told apart from a string the compiler builds from `$"..."`, as with EF Core's
  own `Database.SqlQuery(FormattableString)`, and no rule of the copied analyzer (MKP001 to MKP009)
  detects it, so review enforces it (ADR-ARCH-032 §3). The name keeps every call within
  the `SqlQuery*` calls `ModuleSqlNamesOnlyItsSchema` reads (ADR-ARCH-034 §7).
- `src/MicroKit.Persistence.EntityFrameworkCore/Transactions/EfUnitOfWork.cs` implements it. Once
  `Database.CurrentTransaction` is checked to be the transaction the unit of work opened, it runs
  EF Core's `Database.SqlQuery` on the unit of work's context: on its connection, already open, and
  in that transaction, without `Database.UseTransaction`. With no transaction open, a transaction
  begun on the context, or after the unit of work ended, the read is refused with
  `InvalidOperationException`: it opens no connection and sends no command. A nested
  `ExecuteAsync` or `TryExecuteAsync` joins the unit of work's transaction, and a read there runs.
- `src/MicroKit.Persistence.EntityFrameworkCore/DependencyInjection/PersistenceServiceCollectionExtensions.cs`:
  `AddUnitOfWork<TContext>` binds `IUnitOfWorkSqlQuery<TContext>` to the scope's unit of work of
  that context. A call for another context registers another service and replaces none: the
  non-generic interfaces of the unit of work are still rebound by such a call, as in MicroKit.
- The documentation of `ITransactionalUnitOfWork` lists the five interfaces the unit of work is
  bound to.

Tests, against the PostgreSQL of `apps/api/tests/PostgresImage.cs`:

- `tests/MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests/UnitOfWorkSqlQueryTests.cs`,
  through `UsePostgreSQL` and `AddUnitOfWork`, with a subject:
  - `ASqlReadInAUnitOfWorkRunsOnItsConnectionAndTransaction`, through `ExecuteAsync`,
    `TryExecuteAsync`, a nested `ExecuteAsync`, a nested `TryExecuteAsync`, and `ExecuteAsync`
    with retries on failure whose
    first attempt fails with a transient `40001` and is retried:
    `current_setting('app.user_id', true)` read through the port equals the subject; the read's
    `txid_current()` equals the context's; it sees the row the unit of work saved before it; one
    transaction begun and one open of the connection per attempt, none for a read; one committed
    row; without retries, every command on one physical connection.
  - `ASqlReadOutsideAUnitOfWorkFailsAndOpensNoConnection`, with no transaction, a transaction
    begun on the context, after the unit of work ended, and after the ended unit of work's
    transaction was handed back through `Database.UseTransaction` (refused by the provider, so the
    context holds no transaction): `InvalidOperationException`, no open of
    the connection, no command sent, a transaction begun on the context left as it was.
  - `ASqlReadSendsItsValuesAsParameters`: an injection-shaped value comes back unchanged, and the
    command sent holds `$1` and its parameter, never the value.
  - `TheSqlReadTakesNoString`: every method of the port takes a `FormattableString` and no `string`.
  - `EachContextResolvesTheSqlReadOfItsOwnUnitOfWork`: with two contexts registered, each resolves
    the read of its own unit of work, and the first one's read runs in its unit of work.
- `Probe.ConnectionsOpened`, counted by `Probe.ConnectionCounter`, which `CountedSubjectDbContext`
  adds: the opens of the context's connection EF Core performs.

## Diagnostics

The analyzer's rules, one section per diagnostic ID. The help link of each diagnostic opens its
section here (`src/MicroKit.Persistence.Analyzers/HelpLinks.cs`). The text is MicroKit's, from its
README at `a12097b`, and describes the copied analyzer, whose rule sources are identical to that commit; only HelpLinks.cs differs (see above).

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

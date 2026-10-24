# ADR-ARCH-035 — MicroKit in `libraries/`: a dedicated copy, each library copied once sound and needed, referenced by project

Status: accepted
Date: 2026-10-24
Refs: issue #177; parent #125; ADR-ARCH-020; ADR-ARCH-021; ADR-ARCH-022; ADR-ARCH-031;
ADR-ARCH-032; ADR-ARCH-033; ADR-ARCH-034.

## Context
On 2026-10-11 the maintainer decided that this repository no longer consumes MicroKit from
nuget.org: MicroKit is copied into the repository, dedicated to it, with no synchronization with
the MicroKit repository. `libraries/` holds the .NET libraries this repository owns, under the
root `Directory.Build.props` and `Directory.Packages.props`, and does not exist yet; the test
projects live under `apps/api/tests/` (ADR-ARCH-020), except a module's, which live in the module
(ADR-ARCH-034 §6). Tests assert with xUnit's own `Assert`, no assertion library is added, no
VSTest package is referenced, and CS1591 is exempted by a test project's name alone
(ADR-ARCH-021). An image copies only its project and what it references (ADR-ARCH-022).

The modules need MicroKit's domain building blocks, results and persistence (ADR-ARCH-034);
ADR-ARCH-033 §3 left to this decision whether a module's context comes through
MicroKit.Persistence, and ADR-ARCH-034 §3 which library holds the interceptors of ADR-ARCH-033 §4.
Integration events will use MicroKit.Messaging, introduced with the first event a consumer uses
(ADR-ARCH-032 §1). The maintainer states that MicroKit.Domain is sound, that MicroKit.Result and
MicroKit.Persistence are being corrected in the MicroKit repository, and that MicroKit.Messaging
is being improved there.

What the MicroKit repository holds, read at `michaelatsey/microkit@a930a97` on 2026-10-24:
- one folder per library, `modules/MicroKit.<Lib>/`, with `src/` and, for most libraries,
  `tests/`; some add `docs/`, `samples/`, `benchmarks/` or `testing/`;
- references between projects, within a library and between libraries, in two forms for most of
  them: a project reference by default, a package reference when `CIReleaseBuild` is `true`;
- MicroKit.Persistence.Abstractions depends on MicroKit.Result and MicroKit.Domain;
  MicroKit.Messaging depends on MicroKit.Execution.Abstractions;
- `MicroKit.Persistence.Analyzers`, a Roslyn analyzer (netstandard2.0) enforcing the separation
  of reads and writes;
- tests on `xunit` 2.9.3 with `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and
  `coverlet.collector`, assertions with `Shouldly`, substitutes with `NSubstitute`, architecture
  tests on `NetArchTest.Rules`; test projects that turn documentation off and suppress CS1591;
  some architecture tests that read MicroKit's documentation files;
- build settings this repository already has, plus `IsTrimmable`, package publication properties,
  and `CentralPackageTransitivePinningEnabled`.

The SDK pinned here (10.0.402) is newer than MicroKit's (10.0.107), and every package version
already pinned here is equal to or newer than MicroKit's.

## Decision
1. **A dedicated copy.** A MicroKit library used by this repository lives in `libraries/` as source.
   No `MicroKit.*` package is referenced from nuget.org. The copy is not synchronized with the
   MicroKit repository in either direction: a change made here is not reported there, and a
   change made there reaches this repository only by a new, deliberate copy.
2. **When a library is copied.** A library is copied when both hold: a piece of work of this
   repository needs it, and the maintainer has declared it sound. The pull request that copies it
   names the MicroKit commit it is copied from, and brings the MicroKit libraries it depends on.
   Expected today:
   - for the implementation of BP-001: MicroKit.Domain; MicroKit.Result and
     MicroKit.Result.AspNetCore; MicroKit.Persistence.Abstractions, MicroKit.Persistence,
     MicroKit.Persistence.EntityFrameworkCore, its PostgreSQL provider, and
     MicroKit.Persistence.Analyzers;
   - with the first event a consumer uses: MicroKit.Messaging, with MicroKit.Execution.Abstractions.

   Not copied: MicroKit.MediatR (no mediator, ADR-ARCH-034), MicroKit.Tenancy (isolation is row
   level security keyed on the workspace, ADR-ARCH-031, ADR-ARCH-032), the SQL Server provider,
   samples, benchmarks and performance tests. MicroKit.Auth, MicroKit.Persistence.Specifications,
   MicroKit.Persistence.Testing and every other library are copied only when a decision or a work
   needs them.
3. **A module's persistence comes through it.** A module on EF Core builds its context and unit of
   work through the copied MicroKit.Persistence.EntityFrameworkCore and its PostgreSQL provider,
   which answers ADR-ARCH-033 §3. The persistence analyzer applies to every module's projects.
4. **Layout.** `libraries/MicroKit.<Lib>/src/<Project>/` holds a library's projects and
   `libraries/MicroKit.<Lib>/tests/` their tests; this replaces, for libraries, the location of
   tests set by ADR-ARCH-020. Each `libraries/MicroKit.<Lib>/README.md` states the commit it was
   copied from and lists the changes made here since.
5. **References and versions.** Projects reference copied libraries by project only: the copy
   removes the package form of every reference and the package publication properties. The
   analyzer is referenced as an analyzer (`OutputItemType="Analyzer"`,
   `ReferenceOutputAssembly="false"`), keeps its own target framework, and adds
   `Microsoft.CodeAnalysis.CSharp` to the central file. A copied library carries no version of its
   own; its version is this repository's commit. Every library in the project-reference closure of
   a host is copied by its Dockerfile and let back in by the `.dockerignore` allow-list, by project
   folder, never by its `tests/` folder (ADR-ARCH-022).
6. **Build settings.** The root `Directory.Build.props` and `Directory.Packages.props` govern the
   copied projects (ADR-ARCH-020): no nested `Directory.*.props` comes with them. A setting of
   MicroKit absent from the root, such as `IsTrimmable`, is kept only if the pull request that
   copies the library states why; transitive pinning stays off. The package versions a copied
   library needs are added to the root's central file; where a version is already pinned here, the
   one pinned here is kept. The SDK is the one `global.json` pins; warnings the newer SDK and
   analysis level report in the copied code are fixed by the copy.
7. **Tests.** A copied library's tests are copied with it and conform to ADR-ARCH-021 in full:
   - they run on `xunit.v3` under Microsoft.Testing.Platform; `Microsoft.NET.Test.Sdk`,
     `xunit.runner.visualstudio` and `coverlet.collector` are removed;
   - `Shouldly` assertions are rewritten with `Assert`; `NSubstitute` may be added to the central
     file, since ADR-ARCH-021 does not cover substitutes;
   - architecture tests move to ArchUnitNET and into the library's `<TestedProject>.Tests`;
   - project files lose `GenerateDocumentationFile` and `NoWarn` for CS1591; projects take the
     names of ADR-ARCH-020 (`<TestedProject>.Tests`, `<TestedProject>.IntegrationTests`);
   - a test that needs a library not copied, or MicroKit's documentation, is dropped or rewritten,
     as the pull request states; a test is never skipped for a missing Docker, since the
     repository's test script requires it.
8. **The interceptors of ADR-ARCH-033 and the changes they need.** The transaction and command
   interceptors of ADR-ARCH-033 §4 live in the copy of the PostgreSQL provider. In the same copy,
   the unit of work runs every command in one explicit transaction it opens, or joins one already
   open, and the library adds the way for a read in SQL to obtain the connection and transaction
   of the unit of work (ADR-ARCH-034 §3). These changes are made in this repository, each by its
   own pull request with the tests that prove it.
9. **How it is checked.** `SaasBtp.Architecture.Tests` (ADR-ARCH-034 §7) also reads `libraries/`:

   | Rule | Enforced by |
   |---|---|
   | No `PackageReference` to a `MicroKit.*` package anywhere | `NoMicroKitPackageReference`, reading the project files |
   | No `Directory.*.props` below the root | `NoNestedBuildProps` |
   | Copied test projects reference no VSTest package nor assertion library | `TestProjectsFollowAdr021`, reading the project files |
   | A library missing from an image while a host uses it | the build and the image check of ADR-ARCH-022 |
   | The source commit and the changes since in each library's `README.md`; a library's `tests/` kept out of the image build context | review, against this ADR |

## Consequences
- A defect reported by the MicroKit repository's audit of MicroKit.Persistence (its issue #141)
  is fixed there before the library is copied, or here after it; a defect fixed here stays here.
- Copying a library is a reviewed pull request of its own, with its tests passing on this
  repository's stack; the first one creates `libraries/` and adds its projects to `SaasBtp.slnx`.
- BP-001's implementation waits for MicroKit.Result and MicroKit.Persistence to be declared sound,
  or copies them as they are and fixes them here.
- MicroKit.Tenancy's global filter, and the audit's finding that one query disables every global
  filter, do not concern this repository's isolation, which row level security enforces.
- `architecture.md` §4, `apps/api/CLAUDE.md` and the layout comment of `Directory.Build.props`
  are aligned with decisions 2 and 4.

## Alternatives considered
- Consuming MicroKit from nuget.org: decided against on 2026-10-11.
- Copying every library at once: libraries no work needs, and untested here, would enter the
  repository.
- Copying a library as it is, whatever its state: defects known in the MicroKit repository would
  enter the repository and be fixed twice.
- Not copying the tests: a library changed here, as decision 8 requires, would have no test.
- Keeping MicroKit's test stack for the copied tests: two test stacks, one of which ADR-ARCH-021
  rules out.
- The interceptors in a library of this repository (`libraries/SaasBtp.Persistence`): one more
  library, beside the unit of work they must cooperate with.
- Keeping the package form of references behind a build flag: two ways to build the same project,
  one of which this repository never uses.

## References
- `michaelatsey/microkit` at `a930a97`, read 2026-10-24: `Directory.Build.props`,
  `Directory.Packages.props`, `global.json`, the project files of MicroKit.Domain, MicroKit.Result,
  MicroKit.Persistence and MicroKit.Messaging and of their tests, `EfUnitOfWork.cs`,
  `PostgreSqlEfCoreBuilderExtensions.cs`.
- MicroKit repository issue #141 (audit of MicroKit.Persistence), as reported by the maintainer on
  2026-10-24.
- ADR-ARCH-020, ADR-ARCH-021, ADR-ARCH-022, ADR-ARCH-033, ADR-ARCH-034.

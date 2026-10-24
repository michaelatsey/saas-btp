# ADR-ARCH-035 — MicroKit in `libraries/`: a dedicated copy, each library copied once sound and needed, referenced by project

Status: accepted
Date: 2026-10-24
Refs: issue #177; parent #125; ADR-ARCH-020; ADR-ARCH-021; ADR-ARCH-022; ADR-ARCH-031;
ADR-ARCH-032; ADR-ARCH-033; ADR-ARCH-034.

## Context
On 2026-10-11 the maintainer decided that this repository no longer consumes MicroKit from
nuget.org: MicroKit is copied into the repository, dedicated to it, with no synchronization with
the MicroKit repository. `libraries/` holds the .NET libraries this repository owns, under the
root `Directory.Build.props` and `Directory.Packages.props`, and does not exist yet
(ADR-ARCH-020). An image copies only its project and what it references, and a library a host
references enters its Dockerfile and the `.dockerignore` allow-list (ADR-ARCH-022).

The modules need MicroKit's domain building blocks, results and persistence (ADR-ARCH-034); the
interceptors of ADR-ARCH-033 §4 are one shared implementation whose library this decision names
(ADR-ARCH-034 §3). Integration events will use MicroKit.Messaging, introduced with the first
event a consumer uses (ADR-ARCH-032 §1). The maintainer states that MicroKit.Domain is sound,
that MicroKit.Result and MicroKit.Persistence are being corrected in the MicroKit repository, and
that MicroKit.Messaging is being improved there.

What the MicroKit repository holds, read at `michaelatsey/microkit@a930a97` on 2026-10-24:
- one folder per library, `modules/MicroKit.<Lib>/`, with `src/`, `tests/` and sometimes
  `testing/` and `benchmarks/`;
- references between libraries in two forms, a project reference by default and a package
  reference when `CIReleaseBuild` is `true`;
- tests on `xunit` 2.9.3 and `Microsoft.NET.Test.Sdk`, architecture tests on
  `NetArchTest.Rules`, with `Shouldly` and `NSubstitute`; this repository tests on `xunit.v3`
  under Microsoft.Testing.Platform, with architecture tests on ArchUnitNET (ADR-ARCH-021,
  ADR-ARCH-034);
- build settings this repository already has (`TreatWarningsAsErrors` in Release,
  `AnalysisLevel`, `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`), plus `IsTrimmable` and
  package publication properties (authors, URLs, symbols, license).

An audit of MicroKit.Persistence made in the MicroKit repository (its issue #141) reports, among
others, a query that disables every global filter, a read that detaches an entity being changed,
and an explicit transaction that cannot be joined.

## Decision
1. **A dedicated copy.** A MicroKit library used by this repository lives in `libraries/` as source.
   No package of MicroKit is referenced from nuget.org. The copy is not synchronized with the
   MicroKit repository in either direction: a change made here is not reported there, and a
   change made there reaches this repository only by a new, deliberate copy.
2. **When a library is copied.** A library is copied when both hold: a piece of work of this
   repository needs it, and the maintainer has declared it sound. The pull request that copies it
   names the MicroKit commit it is copied from. Expected today:
   - for the implementation of BP-001: MicroKit.Domain; MicroKit.Result and
     MicroKit.Result.AspNetCore; MicroKit.Persistence.Abstractions, MicroKit.Persistence,
     MicroKit.Persistence.EntityFrameworkCore and its PostgreSQL provider;
   - with the first event a consumer uses: MicroKit.Messaging.

   Not copied: MicroKit.MediatR (no mediator, ADR-ARCH-034), MicroKit.Tenancy (isolation is row
   level security keyed on the workspace, ADR-ARCH-031, ADR-ARCH-032), the SQL Server provider,
   and any other library until a decision needs it. A copy brings only the projects that are used,
   with their tests.
3. **Layout.** `libraries/MicroKit.<Lib>/src/<Project>/` holds a library's projects and
   `libraries/MicroKit.<Lib>/tests/` their tests, as in the MicroKit repository. Each
   `libraries/MicroKit.<Lib>/README.md` states the commit it was copied from and lists the changes
   made here since.
4. **References and versions.** Projects reference copied libraries by project only: the copy
   removes the package form of every reference between libraries and the package publication
   properties. A copied library carries no version of its own; its version is this repository's
   commit. A library that a host references is added to the host's Dockerfile and to the
   `.dockerignore` allow-list by project folder, never by its `tests/` folder (ADR-ARCH-022).
5. **Build settings.** The root `Directory.Build.props` and `Directory.Packages.props` govern the
   copied projects (ADR-ARCH-020): no nested `Directory.*.props` comes with them. A setting of
   MicroKit absent from the root, such as `IsTrimmable`, is kept only if the pull request that
   copies the library states why. The package versions a copied library needs are added to the
   root's central file; where a version differs from one already pinned here, the one pinned here
   is kept. The SDK is the one `global.json` pins.
6. **Tests.** A copied library's tests are copied with it, under `libraries/MicroKit.<Lib>/tests/`,
   and moved to this repository's test stack: `xunit.v3` under Microsoft.Testing.Platform,
   architecture tests on ArchUnitNET. Test projects take the names of ADR-ARCH-020
   (`<TestedProject>.Tests`, `<TestedProject>.IntegrationTests`), so that the settings of
   ADR-ARCH-021 apply to them. A test library the copied tests use (`Shouldly`, `NSubstitute`) is
   added to the central file by that pull request, or the tests are rewritten without it.
7. **The interceptors of ADR-ARCH-033 and the changes they need.** The transaction and command
   interceptors of ADR-ARCH-033 §4 live in the copy of MicroKit.Persistence.EntityFrameworkCore's
   PostgreSQL provider. In the same copy, the unit of work runs every command in one explicit
   transaction it opens, or joins one already open, and a read in SQL obtains the connection and
   transaction of the unit of work instead of opening a connection of its own (ADR-ARCH-034 §3).
   These changes are made in this repository, each by its own pull request with the tests that
   prove it.
8. **How it is checked.**
   - No `PackageReference` to a `MicroKit.*` package, and no `Directory.*.props` below the root:
     a check of the architecture tests reading the project files (ADR-ARCH-034).
   - A library missing from an image while a host uses it: the build and the image check of
     ADR-ARCH-022.
   - The source commit and the changes since, in each library's `README.md`: review.

## Consequences
- A defect reported by the MicroKit repository's audit is fixed there before the library is
  copied, or here after it; a defect fixed here stays here.
- Copying a library is a reviewed pull request of its own, with its tests passing on this
  repository's stack; the first one sets up `libraries/`.
- BP-001's implementation waits for MicroKit.Result and MicroKit.Persistence to be declared sound,
  or copies them as they are and fixes them here.
- MicroKit.Tenancy's global filter, and the audit's finding that one query disables every global
  filter, do not concern this repository's isolation, which row level security enforces.
- `architecture.md` §4's list of MicroKit libraries is aligned with decision 2.

## Alternatives considered
- Consuming MicroKit from nuget.org: decided against on 2026-10-11.
- Copying every library at once: libraries no work needs, and untested here, would enter the
  repository.
- Copying a library as it is, whatever its state: defects known in the MicroKit repository would
  enter the repository and be fixed twice.
- Not copying the tests: a library changed here, as decision 7 requires, would have no test.
- The interceptors in a library of this repository (`libraries/SaasBtp.Persistence`): one more
  library, beside the unit of work they must cooperate with.
- Keeping the package form of references behind a build flag: two ways to build the same project,
  one of which this repository never uses.

## References
- `michaelatsey/microkit` at `a930a97`, read 2026-10-24: `Directory.Build.props`,
  `Directory.Packages.props`, `global.json`, the project files of MicroKit.Domain, MicroKit.Result
  and MicroKit.Persistence.
- ADR-ARCH-020, ADR-ARCH-021, ADR-ARCH-022, ADR-ARCH-033, ADR-ARCH-034.

# MicroKit.Domain

The domain building blocks of MicroKit (aggregate roots, entities, value objects, domain events,
strongly-typed identifiers, business rules), copied into this repository as source
(ADR-ARCH-035, ADR-ARCH-037). The copy is this repository's own: it is not synchronized with
MicroKit in either direction (ADR-ARCH-035 §1).

## Source

- Repository: `michaelatsey/microkit`, folder `modules/MicroKit.Domain/`.
- Commit: `a12097b48c7bd00314ea11bc6c7f667d28962bdd`, committed 2026-11-12T00:18:35Z.
- Read as the head of `main` at 2026-11-16T12:57:50Z, with
  `gh api repos/michaelatsey/microkit/commits/main` (ADR-ARCH-037 §1).
- MicroKit's own documentation of the library, not copied:
  `https://github.com/michaelatsey/microkit/blob/a12097b48c7bd00314ea11bc6c7f667d28962bdd/modules/MicroKit.Domain/README.md`.

## What is here

| Here | Copied from |
|---|---|
| `src/MicroKit.Domain/` | `src/MicroKit.Domain/` |
| `tests/MicroKit.Domain.Tests/` | `tests/MicroKit.Domain.UnitTests/` |
| `tests/MicroKit.Domain.Architecture.Tests/` | `tests/MicroKit.Domain.ArchitectureTests/` |

Not copied: `benchmarks/`, `docs/`, `.claude/`, `CHANGELOG.md`, `CONTRIBUTING.md`,
`MicroKit.Domain.slnx` and `version.json`.

## Changes made here

Project files (ADR-ARCH-035 §5, §6):

- The package properties (`Description`, `PackageTags`) and the properties equal to the root
  `Directory.Build.props` are removed. MicroKit's root `Directory.Build.props` and
  `Directory.Packages.props` are not copied; its `IsTrimmable` is not kept.

Tests (ADR-ARCH-021, ADR-ARCH-035 §7):

- Both test projects take the names of ADR-ARCH-020 and ADR-ARCH-035 §7; their namespaces
  follow.
- They run on `xunit.v3`; `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`,
  `coverlet.collector`, `Shouldly`, `NetArchTest.Rules` and the unused `NSubstitute` are
  removed, with `GenerateDocumentationFile` and the CS1591 `NoWarn`. `CA1707` stays suppressed in
  each test project: MicroKit's test names use underscores, and its `.editorconfig`, which
  suppressed it, is not copied.
- Shouldly assertions are rewritten with xUnit's `Assert`:
  - a string `ShouldNotContain`, which ignores case in Shouldly unless told otherwise, becomes
    `Assert.DoesNotContain(..., StringComparison.OrdinalIgnoreCase)`;
  - a dictionary compared with `ignoreOrder: true` becomes `Assert.Equal`, which compares
    dictionaries by key and value (`CurrencyJsonConverterTests`);
  - `BusinessRuleTests.Exception_RuleWithoutCode_ThrowsArgumentException` uses
    `Assert.ThrowsAny`: its `null` row throws `ArgumentNullException`, which Shouldly's
    `Should.Throw<ArgumentException>` accepted.
- The NetArchTest rules of `DomainArchitectureTests` are rewritten on ArchUnitNET, with the
  same outcomes on the same fixtures:
  - "no dependency on a forbidden framework": a custom condition on each type's dependencies;
  - "value objects are sealed": a struct counts as sealed, as it did in NetArchTest;
  - "domain events are records": ArchUnitNET's `IsRecord` for a class, the compiler-generated
    `PrintMembers` for a struct.
- `DomainArchitectureTests.Domain_DeclaresNoPersistenceContract` asserts with
  `Assert.DoesNotContain` instead of `Assert.Empty` over a filter (xUnit2029).
- The copied tests cite MicroKit's own ADRs and `.claude/` rule files, which this repository does
  not hold. Each citation in a comment is qualified in the form
  `MicroKit ADR-004 D1 (michaelatsey/microkit@a12097b)`, and so is each rule file, so that a bare
  number cannot be read as an ADR of this repository.
  Three string literals of `PublishedDocumentationTests` (`"ADR-004 D9"`, `"ADR-004 D3"`,
  `"ADR-004 D2"`) stay as they are: they are the text the test expects in the XML documentation of
  the copied library source, which is unchanged, and each sits under a qualified comment citing
  the same ADR.
- `DocumentationExamplesTests` is dropped, with the MSBuild items and targets that copied its
  inputs: it reads `docs/`, `README.md`, `.claude/` and the library's ADRs, none of them copied.
  `PublishedDocumentationTests` reads the XML documentation the copied source generates, and is
  kept.

No defect of the library code was found by the copied tests or the review.

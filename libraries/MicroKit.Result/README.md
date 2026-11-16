# MicroKit.Result

MicroKit's result type and its ASP.NET Core mapping, copied into this repository as source
(ADR-ARCH-035, ADR-ARCH-037). The copy is this repository's own: it is not synchronized with
MicroKit in either direction (ADR-ARCH-035 §1).

## Source

- Repository: `michaelatsey/microkit`, folder `modules/MicroKit.Result/`.
- Commit: `a12097b48c7bd00314ea11bc6c7f667d28962bdd`, committed 2026-11-12T00:18:35Z.
- Read as the head of `main` at 2026-11-16T12:57:50Z, with
  `gh api repos/michaelatsey/microkit/commits/main` (ADR-ARCH-037 §1).
- MicroKit's own documentation of the library, not copied:
  `https://github.com/michaelatsey/microkit/blob/a12097b48c7bd00314ea11bc6c7f667d28962bdd/modules/MicroKit.Result/README.md`.

## What is here

| Here | Copied from |
|---|---|
| `src/MicroKit.Result/` | `src/MicroKit.Result/` |
| `src/MicroKit.Result.AspNetCore/` | `src/MicroKit.Result.AspNetCore/` |
| `tests/MicroKit.Result.Tests/` | `tests/MicroKit.Result.UnitTests/` |
| `tests/MicroKit.Result.AspNetCore.IntegrationTests/` | `tests/MicroKit.Result.IntegrationTests/` |
| `tests/MicroKit.Result.IntegrationTests/` | `tests/MicroKit.Result.AotTests/` |
| `tests/MicroKit.Result.Architecture.Tests/` | `tests/MicroKit.Result.ArchitectureTests/` |

Not copied: `MicroKit.Result.Analyzers` and its tests (neither listed by ADR-ARCH-035 §2 nor
referenced by a copied project), `tests/MicroKit.Result.PerformanceTests/` and `samples/`
(ADR-ARCH-035 §2), `docs/`, `.claude/`, `.claude-context/`, `CHANGELOG.md`,
`MicroKit.Result.slnx` and `version.json`.

## Changes made here

Project files (ADR-ARCH-035 §5, §6):

- The package properties (`Description`, `PackageTags`) are removed. MicroKit's root
  `Directory.Build.props` and `Directory.Packages.props` are not copied.
- `IsAotCompatible` is kept on both libraries: it runs the trim and AOT analyzers on their own
  code, which the reflection-free `MicroKit.Result.IntegrationTests` relies on. The SDK makes it
  imply `IsTrimmable`, so `IsTrimmable` stays in effect on these two projects only.
- `NoWarn` `CA1716;CA1000` is kept on `MicroKit.Result`: the public type `Error` has the name of
  a Visual Basic keyword, and generic types such as `Result<T>` carry static factory members; both
  are its public API.

Tests (ADR-ARCH-021, ADR-ARCH-035 §7):

- Names (ADR-ARCH-020):
  - MicroKit's HTTP tests run a real Kestrel host over `MicroKit.Result.AspNetCore`, and are
    named after it;
  - its AOT tests run `MicroKit.Result`'s serialization with reflection-based System.Text.Json
    turned off for the whole process, so they cannot join `MicroKit.Result.Tests`, and take
    `MicroKit.Result.IntegrationTests`.
- They run on `xunit.v3`; the VSTest packages, `coverlet.collector`, `Shouldly`,
  `NetArchTest.Rules` and the unused `NSubstitute` are removed, with `GenerateDocumentationFile`
  and the CS1591 `NoWarn`. `FsCheck.Xunit` is replaced by `FsCheck.Xunit.v3`; the five
  monad-law properties are unchanged.
- `NoWarn`: `CA1707` stays suppressed in each test project (MicroKit's test names use
  underscores). MicroKit's `IL2026;IL3050` are not kept: the build reports neither.
- Shouldly assertions are rewritten with xUnit's `Assert`. Where the rewrite needed more than a
  one-for-one call:
  - a string `ShouldNotContain` becomes an ignore-case `Assert.DoesNotContain`, Shouldly's
    default;
  - Shouldly's `Should.Throw<T>` accepted a derived exception type, so two tests use
    `Assert.ThrowsAny`:
    - `ValidationErrorTests.With_WhenPropertyNameIsNull_ThrowsArgumentException` gets an
      `ArgumentNullException`;
    - `ResultExtensionsTests.GetValueOrThrow_WithFactoryWhenDefault_ThrowsResultExceptionWithoutInvokingFactory`
      gets a `ResultException`, derived from `InvalidOperationException`;
  - `ErrorMetadataBuilderTests` asserts `metadata["extra"] is true`, the value
    `ShouldBe(true)` compared;
  - `ValidationResultFailureOnlyTests` and `ServerErrorDetailTests` compare sequences, the second
    ignoring order as `ignoreOrder: true` did.
- xUnit's analyzer code fixes are applied with `dotnet format`:
  - xUnit1051: a call that accepts a `CancellationToken` receives
    `TestContext.Current.CancellationToken`. The overload called is unchanged.
  - xUnit2012: `Assert.False(x.Any(p))` becomes `Assert.DoesNotContain(x, p)`.
- The copied tests cite MicroKit's own ADRs and `.claude/` rule files, which this repository does
  not hold. Each citation in a comment is qualified in the form
  `MicroKit ADR-004 D1 (michaelatsey/microkit@a12097b)`, and so is each rule file, so that a bare
  number cannot be read as an ADR of this repository.
- The NetArchTest rules of `NamespaceConventionTests` are rewritten on ArchUnitNET:
  - a type resides in a namespace or below it, NetArchTest's prefix match;
  - a static class is abstract and sealed.

No defect of the library code was found by the copied tests or the review.

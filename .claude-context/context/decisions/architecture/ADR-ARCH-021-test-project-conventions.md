# ADR-ARCH-021 — Test project conventions: xUnit v3, native assertions, XML documentation

Status: accepted
Date: 2026-10-13

## Context
The API skeleton (#124) brings the first test projects since the reset (#114). ADR-ARCH-020
fixes where test projects live and how they are named; nothing says which framework they
use. Two constraints shape the answer. The root `Directory.Build.props` generates XML
documentation for every project and turns warnings into errors in Release, while xUnit
requires test classes and test methods to be public: without a rule, every test would need
an XML comment to build (CS1591). And xUnit v3 4.x runs on Microsoft Testing Platform v2,
which the .NET 10 SDK no longer runs through the VSTest mode of `dotnet test`.

## Decision
1. Test projects use xUnit v3 (`xunit.v3`).
2. Assertions use xUnit's own `Assert`. No assertion library is added.
3. Tests run on Microsoft Testing Platform. `global.json` selects it as the runner of
   `dotnet test` for the whole repository, and no VSTest package
   (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) is referenced.
4. A missing XML comment (CS1591) is not reported in a test project: the name of a test
   states what it proves. Documentation generation stays on there, so a malformed XML
   comment is still reported.
5. That exemption is one condition in the root `Directory.Build.props`, on a project name
   ending in `Tests`, which is the naming of ADR-ARCH-020. No project file carries it, and
   every other project keeps CS1591.

## Consequences
- `dotnet test` takes the solution as `--solution` and a project as `--project`; CI and
  `apps/api/CLAUDE.md` use that form.
- Every test project of the repository runs on Microsoft Testing Platform: once
  `global.json` selects it, a VSTest test project is an error.
- A new test project is exempt from CS1591 through its name alone. One named outside the
  convention fails its Release build, which is the intended signal.
- A project that is not a test project must not have a name ending in `Tests`: it would
  lose CS1591 silently.

## Alternatives considered
- xUnit v2: rejected; v3 is the current major version of the framework.
- An assertion library: rejected; the native assertions express the tests written so far,
  and a library is one more dependency to keep current.
- Staying on VSTest with the `xunit.v3.mtp-off` package: rejected; Microsoft documents
  running Microsoft Testing Platform projects through the VSTest mode as legacy, and this
  repository has no VSTest project to keep compatible.
- Turning documentation generation off in test projects: rejected; it silences every
  documentation diagnostic, not only the one that does not apply.
- An XML comment on every test: rejected; it restates the name of the test.
- The exemption in each test project file: rejected; the rule would be repeated in every
  project.

## References
- xUnit.net, "Microsoft Testing Platform" and the 4.0.0 release notes: v2 of the platform
  is the default, and `global.json` selects it for `dotnet test` on the .NET 10 SDK.
- Microsoft Learn, "Testing with 'dotnet test'": the two modes of `dotnet test`, and the
  VSTest mode described as legacy for Microsoft Testing Platform projects.

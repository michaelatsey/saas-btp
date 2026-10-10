# API - .NET modular monolith

Conventions specific to `apps/api`. Repository-wide rules are in the root `CLAUDE.md`.

## Build and test

Run from the repository root. The solution file is `SaasBtp.slnx`; the SDK version is
pinned by `global.json`.

```bash
dotnet restore SaasBtp.slnx
dotnet build SaasBtp.slnx -c Release --no-restore
dotnet test SaasBtp.slnx -c Release --no-build
```

- These are the commands of `.github/workflows/ci-api.yml`. Keep the two in step.
- Build in `Release`: `Directory.Build.props` turns warnings into errors in that
  configuration only.
- Integration tests start PostgreSQL through Testcontainers and need Docker running.

## Where tests live

- Unit tests of a module: `apps/api/tests/SaasBtp.<Module>.UnitTests`. The Access
  module keeps its own, one project per layer, under `apps/api/src/Modules/Access/tests/`.
- Integration tests: `apps/api/tests/SaasBtp.<Module>.IntegrationTests`.
- Architecture rules: `apps/api/tests/SaasBtp.Architecture.Tests`. A failure there is a
  guard, not an obstacle: see "Never route around a guard" in the root `CLAUDE.md`.

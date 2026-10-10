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
- `dotnet test` needs Docker running: the integration tests and the database-migrator
  tests start PostgreSQL through Testcontainers.

## Where tests live

- Unit tests of a module: `apps/api/tests/SaasBtp.<Module>.UnitTests`. The Access
  module keeps its own, one project per layer, under `apps/api/src/Modules/Access/tests/`.
- Integration tests: `apps/api/tests/SaasBtp.<Module>.IntegrationTests`.
- Architecture rules: `apps/api/tests/SaasBtp.Architecture.Tests`. A failure there is a
  guard, not an obstacle: see "Never route around a guard" in the root `CLAUDE.md`.

## Gotchas

Pitfalls of the API's dependencies, each already paid for once. Each entry names the
file that argues it.

- **Supabase secret key: `apikey` header alone.** The GoTrue Admin API authenticates
  the service-role key (an `sb_secret_` key) on the `apikey` header only; adding
  `Authorization: Bearer` gets the call rejected as an invalid JWT.
  Source: `.claude-context/context/decisions/architecture/ADR-ARCH-011-onboarding-by-invitation.md`
  ("Open"), and
  `apps/api/src/Modules/Access/src/SaasBtp.Access.Infrastructure/Identity/GoTrueIdentityProvisioner.cs`.
- **An account can exist with no profile.** If the business transaction fails after
  `createUser`, the `auth.users` row stays, and the next `createUser` answers 422
  `email_exists` with no id in the body. The Admin API `filter` lookup that resolves it
  matches on a prefix: adopt the account only on exactly one exact match of the
  normalized e-mail.
  Source: `.claude-context/context/decisions/architecture/ADR-ARCH-011-onboarding-by-invitation.md`
  ("The orphan branch", "The Admin API e-mail lookup is a SEARCH", "Open").
- **Row-level security with no policy fails silently.** A table with RLS enabled and no
  policy returns zero rows, with no error, to any role that does not bypass RLS; the
  API's privileged role does bypass it. A table read by a non-privileged role needs an
  explicit policy for that role.
  Source: `.claude-context/context/architecture/conventions/sql.md` ("RLS"), and
  `.claude-context/sessions/014-2026-07-12-access-profiles-observer-claims.md`
  ("The RLS finding that shaped the design").
- **Migrations are append-only once applied to a database that holds data.** DbUp
  journals by script name, so rewriting a script in place changes nothing on a database
  that already ran it: change the schema with a new script.
  Source: `.claude-context/context/decisions/architecture/ADR-ARCH-008-identity-model-tenants-memberships.md`
  ("Rebaseline"), and `.claude-context/sessions/016-2026-07-13-access-squash-rebaseline.md`
  ("Learnings").

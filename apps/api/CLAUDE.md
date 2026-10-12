# API - .NET modular monolith

Conventions specific to `apps/api`. Repository-wide rules are in the root `CLAUDE.md`; the
layout is ADR-ARCH-020, the HTTP surface ADR-ARCH-018, the migrations ADR-ARCH-019 and the
test conventions ADR-ARCH-021.

Run every command from the repository root. The solution file is `SaasBtp.slnx`; the SDK
version and the test runner are set by `global.json`.

## Build and test

```bash
dotnet restore SaasBtp.slnx
dotnet build SaasBtp.slnx -c Release --no-restore
dotnet test --solution SaasBtp.slnx -c Release --no-build
```

- These are the commands of `.github/workflows/ci-api.yml`. Keep the two in step.
- Build in `Release`: `Directory.Build.props` turns warnings into errors in that
  configuration only.
- `dotnet test` runs on Microsoft Testing Platform: the solution is passed with
  `--solution`, a single project with `--project`.
- `dotnet test` needs Docker running: the integration tests start PostgreSQL through
  Testcontainers.

## OpenAPI contract

The build writes `apps/api/openapi/v1.json`; ADR-ARCH-018 sets the rules of the contract.

- A build that has nothing to compile does not write the file again. After deleting it or
  editing it by mistake, rebuild with `--no-incremental`.

## Run

```bash
docker compose -f infra/docker/compose.dev.yml up -d
export ConnectionStrings__SaasBtp="Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=$POSTGRES_PASSWORD"
dotnet run --project apps/api/src/hosts/SaasBtp.Api -- --urls http://127.0.0.1:5080
```

- Compose reads `POSTGRES_PASSWORD` from `infra/docker/.env`: it resolves `.env` next to
  the first file given with `-f`. The file is ignored by Git; create it with one line,
  `POSTGRES_PASSWORD=<a local password>`, and export the same value in the shell that runs
  the API.
- The password and the `initdb` arguments apply only when the data volume is created. To
  change either, remove the volume: `docker compose -f infra/docker/compose.dev.yml down --volumes`.
- The API starts with no database configured: `/health/live` answers 200 and
  `/health/ready` answers 503 until `ConnectionStrings__SaasBtp` points to a reachable
  database.

## Migrate

```bash
export ConnectionStrings__Migrations="Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=$POSTGRES_PASSWORD"
dotnet run --project apps/api/database/SaasBtp.Migrations
```

- The runner exits with a non-zero code on any failure, a missing connection string
  included. It never creates the database and never prints its connection string.
- Scripts go in `apps/api/database/SaasBtp.Migrations/Scripts/`. Their naming, their order
  and the rules they follow are ADR-ARCH-019.

## Tests

- Where test projects live and how they are named is ADR-ARCH-020; their conventions are
  ADR-ARCH-021.
- `apps/api/tests/PostgresImage.cs` is the one definition of the PostgreSQL the tests
  start: image, locale provider and locale, matching the Supabase project. It is linked
  into each test project. `infra/docker/compose.dev.yml` repeats its values, and a test
  fails when the two differ.

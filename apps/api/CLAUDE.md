# API - .NET modular monolith

Conventions specific to `apps/api`. Repository-wide rules are in the root `CLAUDE.md`; the
layout is ADR-ARCH-020, a module's structure and the location of its tests ADR-ARCH-034, the
HTTP surface ADR-ARCH-018, the migrations ADR-ARCH-019 and the test conventions ADR-ARCH-021.

Run every command from the repository root. The solution file is `SaasBtp.slnx`; the SDK
version and the test runner are set by `global.json`.

## Build and test

```bash
dotnet restore SaasBtp.slnx
dotnet build SaasBtp.slnx -c Release --no-restore
scripts/ci/test-api.sh --solution SaasBtp.slnx -c Release --no-build
```

- These are the commands of `.github/workflows/ci-api.yml`. Keep the two in step.
- Build in `Release`: `Directory.Build.props` turns warnings into errors in that
  configuration only.
- Run the tests through `scripts/ci/test-api.sh`, never through `dotnet test` directly: the
  script checks that Docker is reachable, then passes its arguments to `dotnet test`.
- `dotnet test` runs on Microsoft Testing Platform: the solution is passed with
  `--solution`, a single project with `--project`.
- The tests need Docker running: the integration tests start PostgreSQL through
  Testcontainers. When Docker is unreachable the script stops with `Docker is unavailable`
  and runs no test. That is the state of the machine, not a defect of the change being
  verified: report it instead of changing code.
- A skipped test fails the run: `Directory.Build.props` passes `--fail-skips on` to every
  test project, so a `Skip =` is reported as a failed test.
- `Explicit = true` no longer keeps a test out of the verification: the same file passes
  `--explicit on`, so an explicit test runs like any other. A test that must not run on
  every pull request (slow, or touching an external service) needs another way to be kept
  out, decided explicitly.
- Never pass `--fail-skips` or `--explicit` on the command line: both are already set, and
  an option given twice stops the run with `Zero tests ran` and exit code 5.

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

## Container images

```bash
docker build --platform linux/amd64 -f apps/api/src/hosts/SaasBtp.Api/Dockerfile -t saas-btp-api:local .
docker build --platform linux/amd64 -f apps/api/database/SaasBtp.Migrations/Dockerfile -t saas-btp-migrator:local .
```

- How the images are built is ADR-ARCH-022; where they are published, tagged, promoted,
  verified and delivered to staging, ADR-ARCH-030; how the runner is sequenced before the
  API, ADR-ARCH-024; how secrets reach them, ADR-ARCH-025.
- The build context is the repository root. The root `.dockerignore` is an allow-list: a file
  or a project a Dockerfile needs must be let back in there, or the build does not see it.
- Base images are pinned by digest. The SDK image digest is written in both Dockerfiles:
  bump it in both at once. A digest bump that moves `sdk:10.0-noble` to another feature band
  moves the `global.json` pin in the same pull request, and the reverse (ADR-ARCH-022). Read
  the SDK an image ships with `docker run --rm <sdk image@digest> dotnet --version`.
- Both images run as the non-root user `app`, whose id the base images export as `APP_UID`.
  Never add a secret to a Dockerfile, a build argument or the build context.
- The API listens on port 8080. Its image carries `curl` for the Coolify health check.
- The runner image is chiseled: no shell. Without `GSS Encryption Mode=Disable` in its
  connection string, it prints that `libgssapi_krb5.so.2` cannot be loaded, then connects
  normally; Npgsql documents the message as harmless.
- Images are published by `.github/workflows/publish-api-images.yml`, which
  `.github/workflows/deliver-api-staging.yml` calls once `CI API` passes on a push to `main`,
  before deploying the commit to staging with `.github/workflows/deploy-api-staging.yml`. Both
  can also be dispatched by hand from `main` with the full SHA of a commit on `main`. The
  publish workflow never overwrites an existing tag: an image already published from the same
  commit is reported, not pushed again, so a run where one image failed is completed by
  dispatching it again. A deployment refuses a digest without a valid attestation; an
  automatic one also refuses a commit older than the one staging runs, while a manual one
  (rollback) does not.

## Tests

- How test projects are named is ADR-ARCH-020; where they live is ADR-ARCH-020,
  ADR-ARCH-034 for a module's tests and ADR-ARCH-035 for a library's; their conventions are
  ADR-ARCH-021.
- `apps/api/tests/PostgresImage.cs` is the one definition of the PostgreSQL the tests
  start: image, locale provider and locale, matching the Supabase project. It is linked
  into each test project. `infra/docker/compose.dev.yml` repeats its values, and a test
  fails when the two differ.

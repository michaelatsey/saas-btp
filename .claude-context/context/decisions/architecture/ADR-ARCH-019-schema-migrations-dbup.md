# ADR-ARCH-019 — Schema migrations with DbUp, run before the application

Status: accepted
Date: 2026-10-12

## Context
The database schema is rebuilt from the consigned documents after the reset (#114). The
database is a Supabase PostgreSQL project. `sql.md` sets the DDL conventions, including a
portable subset validated on bare PostgreSQL and an explicit exclusion list for
auth-coupled scripts. The intended module design gives each module its own database
schema; whether modules are fully independent is decided before BP-001.

## Decision
- DbUp owns every DDL change. The application never creates or alters the schema at
  runtime, and EF Core migrations are not used.
- The runner is the console project `apps/api/database/SaasBtp.Migrations`. Each
  deployment runs it as a separate step before the application starts; application
  startup never runs migrations.
- The journal lives in the schema `dbup`, created by the runner if absent, never in
  `public`: the Supabase Data API exposes `public` by default.
- Scripts live in one flat folder, ordered by a global four-digit number, with the module
  in the file name: `NNNN__<module>__<description>.sql`. DbUp runs scripts in alphabetical
  order, and embedded resource names carry their folder path, so a folder per module would
  order by folder before number.
- A merged script is never edited; a correction is a new script.
- Every script stays compatible with the application version still running when it is
  applied. A destructive change (dropping or renaming a table or column, changing a type,
  adding a NOT NULL column without a default) is split across releases: expand first,
  then contract in a later release, once no running application version depends on the
  old shape.
- The portable subset runs on bare PostgreSQL in Testcontainers; auth-coupled scripts are
  listed by name in the test's exclusion list and validated against a real Supabase
  project, as `sql.md` prescribes.
- The connection string comes from the environment or a secret store, never from a
  committed file.

## Consequences
- Deployment must sequence the runner before the application; where the staging database
  lives is decided with the deployment, without changing this tool.
- One journal for all modules. If modules are later made fully independent, per-module
  journals can replace it before many scripts exist.

## Alternatives considered
- Supabase CLI migrations: rejected; loses the .NET integration and the Testcontainers
  anti-drift test.
- EF Core migrations: rejected; DDL is authored as reviewed SQL per `sql.md`.
- Running migrations at application startup: rejected; couples every instance start to
  schema changes.
- Journal in `public`: rejected; exposed by the Supabase Data API by default.
- A folder per module: rejected; breaks the global order.
- One journal per module: deferred to the module design decision.

## References
- Supabase docs, "Using custom schemas": only `public` is exposed by default.
- DbUp docs, "Run Group Order": scripts run in alphabetical order by default.

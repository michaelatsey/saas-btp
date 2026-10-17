# ADR-ARCH-026 — The staging database is a separate Supabase project

Status: accepted
Date: 2026-10-17
Refs: issue #127; ADR-ARCH-019; ADR-ARCH-024; ADR-ARCH-025.

## Context
ADR-ARCH-024 deploys the API and its migration runner to a staging environment before
production, and ADR-ARCH-019 leaves open where the staging database lives. One Supabase project
exists, `saas-btp`, in `eu-central-1`, on the Free plan; it serves as production and holds no
table yet. DbUp owns the schema (ADR-ARCH-019), and the staging deployment needs PostgreSQL,
Supabase Auth, the Supabase roles and the pooler, as production has them.

What Supabase offers, as of 2026-10-17:
- The Free plan allows two active projects, pauses a project after one week of inactivity, and
  includes no automatic backups.
- Branching is available on the Pro plan and above, billed per branch and per hour. A branch
  built from the dashboard is built from the base project's migration history; its database
  schema is not cloned.

## Decision
1. Staging runs on its own Supabase project, `saas-btp-staging`, in the same organization and
   region (`eu-central-1`) as production, on the Free plan.
2. Staging shares nothing with production: its own `postgres` password, its own database roles
   (ADR-ARCH-027, ADR-ARCH-028), its own Doppler configs `stg` (ADR-ARCH-025) and its own GitHub
   Environment (ADR-ARCH-024).
3. No production data is ever copied to staging.
4. Free-plan pausing is accepted. The API's readiness check produces the database activity
   Supabase counts; a paused project makes the runner fail, so the deployment job stops before
   Coolify is touched.

## Consequences
- A staging deployment can never alter production's schema or reach its data: the two projects
  share no credential.
- Both of the Free organization's active projects are used; a third environment needs a paid
  plan or the removal of one of them.
- A staging deployment after a long idle period fails until the project is resumed; the failure
  is the runner's, before any change to the API.
- The same DbUp journal and scripts run on both projects, so staging rehearses exactly what
  production will apply.

## Alternatives considered
- A Supabase branch of the production project: rejected; it needs a paid plan, is built from
  Supabase's migration history while DbUp owns the schema, and is billed per branch and per
  hour.
- PostgreSQL deployed on Coolify: rejected; it has neither Supabase Auth, nor the Supabase roles,
  nor the pooler, so staging would not rehearse production.
- Other schemas in the production project: rejected; staging would share production's roles,
  `postgres` password and database, and a staging script could reach production objects.

## Notes
Production is on the Free plan as well: it can be paused and has no backups. That is outside
this decision and is settled before production holds data.

## References
- Supabase pricing, read 2026-10-17: "Limit of 2 active projects", "Free projects are paused
  after 1 week of inactivity", automatic backups not included on Free, Branching "available on
  the Pro Plan and above" at a price per branch, per hour.
- Supabase docs, "Branching", read 2026-10-17: "A dashboard branch builds it from your base
  project's migration history"; "The database schema isn't cloned."
- Supabase docs, "Project Pausing", as read by the maintainer on 2026-10-17 (issue #127).

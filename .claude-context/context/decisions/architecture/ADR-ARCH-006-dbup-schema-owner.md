# ADR-ARCH-006 — DbUp owns the database schema (DDL); EF Core is runtime ORM only

Status: accepted
Date: 2026-07-10
Refs: architecture.md §4 (EF Core persistence), ADR-ARCH-005 (.NET domain = write
authority), naming.md (id convention), session 012 (Supabase wired),
safety-domain-model.md (first product schema).

## Context

Safety/Constat is the first product persistence. Access (#4) and Story A (#17) are
persistence-free (config-bound stores, no DbContext, no migration), so the DDL-ownership
decision is genuinely greenfield and must be made before the first table exists.

architecture.md already commits EF Core (via MicroKit.Persistence) as the runtime ORM
(repositories, outbox/inbox). The open question is NOT "ORM vs migrations" but "who owns
the DDL" — schema, RLS policies, triggers, and the PowerSync logical-replication
publication.

The DDL is SQL-heavy and Postgres-specific:
- Supabase security is RLS-first (pure SQL policies).
- PowerSync replicates via a Postgres publication (`CREATE PUBLICATION`), which EF
  migrations do not model.
- A modular monolith will eventually hold several DbContexts over one Postgres database;
  EF migrations across multiple DbContexts sharing one schema are fragile (shared history,
  ordering).

## Decision

- DbUp is the single owner of the PostgreSQL DDL. All schema changes (tables, constraints,
  indexes, RLS policies, triggers, functions, publications) are hand-written SQL scripts
  run by DbUp, tracked in a single DbUp journal.
- EF Core (MicroKit.Persistence) is the runtime ORM only: it maps aggregates to an existing
  schema it does not create.
- EF Core migrations are NOT used. Supabase CLI migrations are NOT used for the app schema.
  Exactly one migration authority: DbUp.
- Migrations run from a dedicated migrator (`tools/database-migrator`, console) in the
  CI/deploy step, never on API startup. The migrator connects via the privileged direct
  Postgres connection (owner role) — required for RLS/triggers/publications, bypassing RLS
  by design.

### Script convention
- Scripts embedded in the migrator (`EmbeddedResource`), applied in a single global order.
- Filename: `NNNN_<context>_<change>.sql` (e.g. `0002_safety_create_constats.sql`). The
  global number fixes order; the `<context>` prefix keeps bounded-context ownership legible
  in a flat folder. No per-module journals.

## Guardrails

- No `Database.Migrate()`, no `EnsureCreated()` anywhere.
- No `dotnet ef migrations add` — EF migrations tooling stays unused.
- No Supabase CLI migration for the app schema.
- One DbUp journal table only.
- An integration test validates the EF DbContext mapping against a database migrated by
  DbUp (never against `EnsureCreated` / an EF snapshot). This is the anti-drift guard
  between the SQL-owned schema and the EF model.

## Consequences

- Positive: explicit SQL; full control of RLS, triggers, PowerSync publication; safe with
  multiple DbContexts; schema versioned in the .NET solution/CI, consistent with
  ADR-ARCH-005.
- Negative: more discipline; no auto-generated migrations; the EF model must be kept in
  sync with the SQL schema by hand (mitigated by the mapping integration test).

## Deferred (not decided here)

- Postgres schema namespace (`public` vs per-context schema), concrete RLS policies, and
  the PowerSync publication: decided when writing `0002_safety_create_constats.sql`, not in
  this ADR. This ADR fixes ownership, not schema design.
- CI deploy ordering (migrate → verify schema → deploy API): recorded as intended shape;
  the pipeline is JIT (no Coolify pipeline live, build-checklist 5.2 untriggered).

## Alternatives considered

- EF Core migrations own the DDL. Rejected: awkward for RLS/triggers/publications, fragile
  across multiple DbContexts on one schema.
- Supabase CLI migrations own the DDL. Legitimate for a TS-first Supabase project; rejected
  here — .NET-centric stack, domain = write authority, schema belongs in the .NET
  solution/CI. Used alongside DbUp it would create two journals (`SchemaVersions` vs
  `supabase_migrations`) = two sources of truth.

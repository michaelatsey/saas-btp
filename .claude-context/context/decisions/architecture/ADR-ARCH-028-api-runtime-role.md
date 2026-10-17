# ADR-ARCH-028 — The API connects as `saasbtp_api`, owns nothing and is subject to RLS

Status: accepted
Date: 2026-10-17
Refs: issue #127; ADR-ARCH-019; ADR-ARCH-025; ADR-ARCH-027.

## Context
The API needs a database role at runtime, distinct from the runner's (ADR-ARCH-027), whose
credential reaches the API container through Coolify's Doppler integration (ADR-ARCH-025). The
domain enforces its rules in the API; row level security is the database's own guard.

What Supabase and PostgreSQL offer, as of 2026-10-17:
- A role is subject to RLS unless it has `bypassrls` or owns the table; a table that forces RLS
  subjects its owner as well.
- Every role receives `connect` and `temporary` on the `postgres` database, and `usage` on the
  schema `public`, through `PUBLIC`; `noinherit` does not affect those grants. Supabase's
  troubleshooting guide "Custom role inherits privileges that were not explicitly granted"
  recommends revoking `temporary` and `usage on public` from `PUBLIC`, and warns that revoking
  `connect` from `PUBLIC` "disconnects the Data API, Auth, Storage, and other Supabase services,
  because their roles connect through that grant. Leave `connect` granted to `PUBLIC`."
- After that revocation, `postgres`, `anon`, `authenticated` and `service_role` keep their own
  `usage` grants on `public`.
- The shared pooler opens at most its configured pool size of server connections per user,
  database and mode; clients beyond it wait in session mode, for up to a minute.
- Only the schemas listed in the Data API settings are exposed through it; `public` is listed by
  default.

## Decision
1. The API connects as `saasbtp_api`: `login`; no `superuser`, `createdb`, `createrole`,
   `replication` nor `bypassrls`; no membership in `postgres`, `saasbtp_migrator` nor any
   Supabase role. It owns nothing.
2. It receives `usage` on the application schemas and the object privileges each module needs,
   granted by the DbUp scripts that create those objects; nothing on `dbup`.
3. `infra/supabase/roles.sql` revokes `temporary` on the database and `usage` on `public` from
   `PUBLIC`, and grants neither back to `saasbtp_migrator` nor to `saasbtp_api`, which use no
   temporary table and nothing in `public`. `connect` stays granted to `PUBLIC`.
4. The API connects through Supavisor in session mode, like the runner, as
   `saasbtp_api.<project ref>`, with `SSL Mode=Require`. Its Npgsql pool (`Maximum Pool Size`) is
   capped below the project's pool size.
5. Application schemas are not listed among the Data API's exposed schemas, and grant nothing to
   `anon`, `authenticated` nor `service_role`.
6. Deferred to #125 and BP-001: how the API passes the user's claims to each transaction for the
   policies, and the grants on the `auth` schema that policies calling `auth.uid()` need.

## Consequences
- Every application table the API reads or writes needs a policy that admits `saasbtp_api`; a
  table with RLS enabled and no such policy returns no row and accepts no write.
- A compromised API credential can neither alter the schema nor bypass the policies.
- Every role of the project keeps `connect` on the database through `PUBLIC`; the boundary of
  `saasbtp_api` is its object privileges and the policies, not the right to connect.
- A function `saasbtp_migrator` creates is executable by `PUBLIC`, and so by `saasbtp_api` on
  any schema it has `usage` on, unless that execute is revoked. `roles.sql` cannot change it:
  run as `postgres`, `alter default privileges for role saasbtp_migrator revoke execute on
  functions from public` is refused with "permission denied to change default privileges"
  (`supabase/postgres:17.6.1.178`, 2026-10-17). A role may change its own default privileges, so
  the first DbUp script that creates a function closes it, before any function exists.
- Supabase's own roles lose `temporary` through `PUBLIC`. A Supabase service that needs it gets
  it back by an explicit grant in `roles.sql`, recorded when it is found.
- `sql.md`, section RLS, stated that the .NET domain "connects with a privileged role that
  bypasses RLS", after the removed ADR-ARCH-005. This decision replaces that statement, and the
  convention file now requires a policy for `saasbtp_api` on every table the API reads or writes.

## Alternatives considered
- Revoking `connect` from `PUBLIC` and granting it back to the roles that need it, as first
  decided on #127: rejected; Supabase's guide warns that it disconnects the platform's services
  and names no list of roles to grant it back to, so the list would be guessed.
- One role for the runner and the API: rejected; the API would hold DDL rights and own the
  tables, so RLS would not apply to it without forcing it everywhere.
- A role with `bypassrls`, as the removed ADR-ARCH-005 assumed: rejected; the policies would be
  a guard for every path except the main one.
- `service_role`: rejected; a Supabase role, shared with the platform, with `bypassrls`.

## References
- Supabase troubleshooting guide, "Custom role inherits privileges that were not explicitly
  granted", read 2026-10-17 (`supabase/supabase` at `7425d49`).
- Supabase docs, "Connect to your database", "Pooling and limits" and the Supavisor FAQ (pool
  size per user, database and mode; session-mode queueing), "Row Level Security", "Securing your
  API", read 2026-10-17.
- `infra/supabase/roles.sql`, applied on `supabase/postgres:17.6.1.178` on 2026-10-17: after it,
  `saasbtp_api` has `connect`, neither `temporary` nor `usage` on `public`, and no membership.

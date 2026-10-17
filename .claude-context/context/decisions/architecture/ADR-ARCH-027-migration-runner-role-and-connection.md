# ADR-ARCH-027 — The migration runner connects as `saasbtp_migrator` through Supavisor in session mode

Status: accepted
Date: 2026-10-17
Refs: issue #127; ADR-ARCH-019; ADR-ARCH-024; ADR-ARCH-025; ADR-ARCH-026.

## Context
ADR-ARCH-019 runs the DbUp runner before the application but does not decide the role it
connects as, nor the connection type. ADR-ARCH-024 runs it in a GitHub Actions job on a
GitHub-hosted runner, and ADR-ARCH-025 gives it its connection string from Doppler. The removed
ADR-ARCH-006 (tag `archive/pre-reset`) had the runner use the privileged direct connection as the
owner role; that requirement was not carried into ADR-ARCH-019.

The runner sets no command timeout, so every statement it sends is cancelled by Npgsql after the
default `Command Timeout` of 30 seconds; a longer script, an index build or a backfill, fails the
deployment (input 1 of the review comment on #127).

What Supabase offers, as of 2026-10-17:
- The direct connection resolves to IPv6 unless the IPv4 add-on is bought, and GitHub Actions is
  listed among IPv4-only clients. The shared pooler, Supavisor, accepts IPv4 in session mode on
  port 5432 and in transaction mode on port 6543.
- Custom roles with `LOGIN` connect through the shared pooler with the user name
  `<role>.<project ref>`, without being registered with the pooler. The pooler host carries a
  cluster index (`aws-<N>-<region>`) that cannot be derived from the region; it is copied from
  the project's Connect dialog.
- Transaction mode does not support prepared statements nor session-level settings; session mode
  keeps one server connection per client for the whole session, so advisory locks and session
  settings hold.
- `statement_timeout` defaults, per Supabase's "Timeouts" page: `anon` 3 s, `authenticated` 8 s,
  `postgres` capped by a global default of 2 minutes. A role's own setting overrides that default,
  which applies only to roles without one.
- `postgres` is not a superuser. It holds `CREATEROLE`, may grant `create` on the database, and
  may set defaults (`alter role ... set`) on the roles it creates.

## Decision
1. The runner connects through Supavisor in session mode, port 5432, over IPv4, as
   `saasbtp_migrator.<project ref>`, with an Npgsql key-value connection string and
   `SSL Mode=Require`. Never transaction mode, never the `postgres` role. The pooler host is
   copied from the Connect dialog of each project.
2. `saasbtp_migrator` has `login`; no `superuser`, `createdb`, `createrole`, `replication` nor
   `bypassrls`; no membership in `postgres` nor in any Supabase role. It holds `create` on the
   database, so it creates and owns the journal schema `dbup` and every application schema and
   object.
3. Every application table that enables row level security also forces it
   (`alter table ... force row level security`), so that owning a table does not exempt a session
   from its policies.
4. `saasbtp_migrator` has a role-level `statement_timeout` of 110 seconds. This is a choice, not
   a platform limit: the role's setting replaces the 2-minute default rather than staying under
   it. A script that needs longer raises it, with the runner's timeout, before it is merged.
5. The runner sets its command timeout to 120 seconds for every command, journal commands
   included, whatever the connection string says. It stays above the role's
   `statement_timeout`, so that the server cancels a long statement first and reports the cause.
6. The roles, their attributes, grants and timeouts are created once per project by the
   maintainer, from `infra/supabase/roles.sql`, outside the DbUp journal. Passwords are set
   interactively and never written to a file.

## Consequences
- The runner reaches the database from any GitHub-hosted runner without the IPv4 add-on.
- A failed or paused connection fails the runner, which stops the deployment before Coolify is
  touched (ADR-ARCH-024).
- Session mode keeps advisory locks available, should the runner later need one to exclude
  concurrent executions outside the deployment job's concurrency group.
- A single statement longer than 110 seconds fails the deployment until both timeouts are raised
  together; the role setting and the runner's constant name each other in their comments.
- Ownership no longer bypasses RLS: a DbUp script that reads or writes rows of a table it owns,
  a backfill for instance, is subject to that table's policies and needs one that admits it.
- The runner owns the schema; the API owns nothing (ADR-ARCH-028).

## Alternatives considered
- The direct connection: rejected; IPv6-only on the current plan, and GitHub-hosted runners are
  IPv4-only. The IPv4 add-on is a cost for no other benefit here.
- Transaction mode: rejected; no prepared statements, no session settings, no advisory locks
  across statements.
- The `postgres` role: rejected; it is Supabase's administrator role, with privileges on
  Supabase's own schemas that the runner never needs.
- A role-level `statement_timeout` of 0 (none): rejected; a blocked script would hold the
  deployment until the job's own timeout, with no server-side reason in the log.
- The timeout in the connection string only: rejected; it would live in Doppler, out of review,
  apart from the role setting it must stay above.

## References
- Supabase docs, "Connect to your database" (shared pooler host, session mode, IPv4, custom role
  user name), "Timeouts" (per-role defaults, the 2-minute default, `alter role ... set
  statement_timeout`), "Postgres Roles", troubleshooting "Tenant or user not found" and "Fatal:
  password authentication failed" (custom roles through Supavisor), read 2026-10-17
  (`supabase/supabase` at `7425d49`).
- PostgreSQL 17 docs, `ALTER ROLE`: database-role settings override role settings, which override
  database settings; `CREATEROLE` holders may set defaults on roles they hold `ADMIN OPTION` on,
  which creating a role grants.
- Npgsql docs, "Connection String Parameters": `Command Timeout`, default 30 seconds.
- DbUp source (`DbUp/DbUp` at `41228ce`): `WithExecutionTimeout` sets the timeout of the script
  executor and of the connection manager, whose transaction strategies apply it to every
  command, the journal's included.
- `infra/supabase/roles.sql`, applied as a non-superuser `postgres` on `supabase/postgres:17.6.1.178`
  on 2026-10-17, with the runner then run as `saasbtp_migrator` (pull request of #127).

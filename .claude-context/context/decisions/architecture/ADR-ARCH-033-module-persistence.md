# ADR-ARCH-033 — Module persistence: one schema per module, references to Access only, one journal, EF Core at run time inside an explicit transaction

Status: accepted
Date: 2026-10-21
Refs: issue #176; parent #125; ADR-ARCH-019; ADR-ARCH-027; ADR-ARCH-028; ADR-ARCH-031;
ADR-ARCH-032; `sql.md`; `naming.md`; `docs/domain/access.md`.

## Context
DbUp owns every DDL change, from one flat folder applied in one global order, with one journal
in the schema `dbup`; per-module journals were left to the module design (ADR-ARCH-019). Each
bounded context has its own schema (`sql.md`). `sql.md` and `naming.md` already assume EF Core
at run time, with migrations disabled. The removed ADR-ARCH-006 (tag `archive/pre-reset`) had an
integration test validating the EF mapping against a database migrated by DbUp; it was not
carried over.

ADR-ARCH-032 left to this decision: how every database access runs inside a transaction whose
first statement sets the subject; what statements returning rows mean for the creation order of
its decision 4; the form of the "exactly one current owner" constraint; whether tables reference
Access by foreign key. An indivisible business step lives in one module (ADR-ARCH-031). No
process ends a workspace (INV-ACCESS-03).

What the documentation states, read on 2026-10-21:
- PostgreSQL 17, `CREATE POLICY`: when a data-modifying statement has a `RETURNING` clause, the
  new or updated rows must satisfy the relation's `SELECT` policies, otherwise an error is
  raised; they are never silently ignored. An `UPDATE` that reads the relation applies the
  `SELECT` policies too.
- EF Core: database-generated values are read back after an insert (Npgsql, "Generated
  properties"); `SaveChanges` orders inserts so that the model's constraints are not violated,
  and fails on a circular dependency in the data to save; since EF Core 7 a single statement is
  sent without an explicit transaction ("What's new in EF Core 7.0"). A transaction interceptor
  sees transactions start and savepoints created; a command interceptor runs before each
  command and may stop it ("Interceptors").
- `xmin`, the concurrency token of `sql.md`, is mapped as a property EF reads back.

A reproduction on a disposable PostgreSQL 16 (2026-10-21), as a role without `bypassrls`, forced
row level security, the policies and deferred constraints of ADR-ARCH-032: inserting an
organization with `RETURNING xmin` before its owner's ownership row fails ("new row violates
row-level security policy"); inserting the ownership row first, then the organization with
`RETURNING`, succeeds; an organization committed without its ownership row fails on the deferred
foreign key; an ownership row inserted by another person on that organization is accepted but
makes them see nothing and change nothing.

## Decision
1. **Schemas and references across them.** Each module owns one schema and its tables; no module
   writes another's schema. A module's table may reference, by foreign key, only the keys Access
   publishes: `access.workspaces (id)` and `access.profiles (id)`. Such a key is `on delete
   restrict`; no process deletes a workspace or a profile today, and one that would must first
   decide what becomes of the rows referencing it. A foreign key check bypasses row level
   security, so it proves that the referenced row exists, never that the subject may use it: the
   module's insert policy still calls `access.may_act_in(workspace_id)` (ADR-ARCH-032), and a
   foreign key error is answered as "not found" (ADR-ARCH-032 §5). Between other modules, a
   reference is an identifier without a foreign key, kept consistent by integration events.
2. **One journal.** All modules share the runner, its single flat folder and its one journal in
   `dbup` (ADR-ARCH-019). A module's script may depend on an earlier script of Access, which the
   global number orders.
3. **EF Core at run time.** Each module maps its own schema with one EF Core context, on the
   Npgsql provider. EF Core never creates or alters the schema: no migrations, no
   `EnsureCreated`. Whether the context comes through MicroKit.Persistence is decided with
   MicroKit (#177). Each module's integration tests include `MappingMatchesMigratedSchema`: it
   migrates a PostgreSQL container with the runner, then compares, for every entity of the
   module's context, the table, columns, store types, nullability and keys of the EF model with
   the catalog of the migrated database, and fails on any difference, including a non-null
   column without a default that the model does not map.
4. **One transaction per unit of work, its subject first.** A unit of work belongs to one module
   and opens one explicit transaction on that module's context; a request that touches two
   modules uses two transactions, consistent with ADR-ARCH-031. Two interceptors, registered by
   every module's context, enforce ADR-ARCH-032 §3 in one place:
   - a transaction interceptor, when the transaction has started and before any other command or
     savepoint, sends `select set_config('app.user_id', @subject, true)` with the subject taken
     from the validated token, as a parameter;
   - a command interceptor stops any command that has no transaction, with an error, so that EF's
     single-statement shortcut and any read outside a unit of work are refused instead of
     running without a subject.
   Raw SQL goes through EF's parameterized forms only (ADR-ARCH-032 §3).
5. **Statements that return rows.** EF may read generated values back with `RETURNING`. A
   creation therefore inserts every row only once the subject may see it: a row whose select
   policy depends on another row of the same creation is inserted after that row. For a founding,
   the ownership row precedes the organization, its foreign key to the organization being
   deferred to commit. The EF model declares only the relation from the organization to its
   current ownership; the ownership's `organization_id` is mapped as a plain column, so that EF
   orders the ownership first and sees no cycle. The founding's integration test, run as
   `saasbtp_api`, fails when this order is reversed.
6. **Exactly one current owner.** The form proposed by ADR-ARCH-032 §4 is kept:
   - `access.organizations.current_ownership_id` is non-null, with a composite foreign key
     `(id, current_ownership_id)` to `access.ownerships (organization_id, id)`, deferrable,
     initially deferred;
   - `access.ownerships.organization_id` references `access.organizations (id)`, deferrable,
     initially deferred, and `(organization_id, id)` is unique.
   An ownership row that no organization references grants nothing; such rows are not removed
   today. A later root record with an owner takes the same form.

## Consequences
- Every module's tests need the `access` schema migrated, which they already need for
  `access.may_act_in`: the runner applies all scripts, Access's first.
- Renaming or replacing `access.workspaces (id)` or `access.profiles (id)` is a change to a
  published contract of Access, made by expand and contract (ADR-ARCH-019).
- A command sent outside a unit of work fails loudly instead of returning no row: a missing
  transaction is found by the first test that takes that path.
- The mapping test is the anti-drift guard between the DbUp scripts and the EF model; the
  integration tests of ADR-ARCH-032 §6 remain the authorization guard. Neither replaces the
  other.
- Another person can insert an orphan ownership row on an organization whose identifier they
  know; it reveals nothing beyond what "in use" already does (`naming.md`) and grants nothing.
  Whether to forbid it is decided with ownership transfer (BP-008).
- The exact Npgsql API for `xmin` is confirmed when the first mutable aggregate is mapped
  (`sql.md`, Optimistic concurrency).

## Alternatives considered
- No foreign key across schemas: an identifier pointing to no workspace would be caught only by
  the policies; a data script run as the migrator, or a policy defect, could leave rows without a
  workspace.
- Foreign keys between any modules: every module's DDL would depend on the others' tables,
  against ADR-ARCH-020.
- One journal per module: several runs, no global order across modules, and a module's script
  could no longer rely on an Access script; ADR-ARCH-019's single folder already orders them.
- Dapper or hand-written SQL instead of EF Core: `sql.md` and `naming.md` are written for EF
  Core's converters and concurrency token; a second access layer would need its own conventions
  and tests.
- Setting the subject in each repository: every path would have to remember it; one omission
  reads nothing, or worse, runs under a previous subject on a reused connection if the setting
  were not transaction-local.
- Not reading values back on insert (no `xmin` on root records): it removes optimistic
  concurrency from the organization, which ownership transfer (BP-008) updates.
- A select policy admitting a row to its creator during the creation: there is no
  transaction-scoped notion of creator in the database without a further setting the API would
  have to keep correct.

## References
- PostgreSQL 17, `CREATE POLICY` (`RETURNING` and `SELECT` policies), read 2026-10-21.
- Microsoft Learn, EF Core "Interceptors", "What's new in EF Core 7.0" (transactions, insert
  ordering), `CoreStrings.CircularDependency`, read 2026-10-21.
- Npgsql EF Core provider, "Generated properties", "Concurrency tokens", read 2026-10-21.
- Removed ADR-ARCH-006, tag `archive/pre-reset`.

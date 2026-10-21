# ADR-ARCH-033 — Module persistence: one database shared today, each module extractable, no foreign key across modules, EF Core by default inside an explicit transaction

Status: accepted
Date: 2026-10-21
Refs: issue #176; parent #125; ADR-ARCH-019; ADR-ARCH-027; ADR-ARCH-028; ADR-ARCH-031;
ADR-ARCH-032; `architecture.md` §2-§4; `sql.md`; `naming.md`; `docs/domain/access.md`.

## Context
Modules do not reference one another and communicate by integration events (`architecture.md`
§2-§3); an indivisible business step lives in one module (ADR-ARCH-031). Today every module
lives in one PostgreSQL database. The maintainer requires that any module can later be moved
out as a service of its own, with the storage and internal organization its work calls for.

DbUp owns every DDL change, from one flat folder applied in one global order, with one journal
in the schema `dbup`, and EF Core migrations are not used (ADR-ARCH-019). `sql.md` and
`naming.md` are written for EF Core at run time. The removed ADR-ARCH-006 (tag
`archive/pre-reset`) had an integration test validating the EF mapping against a database
migrated by DbUp; it was not carried over.

ADR-ARCH-032 left to this decision: how every database access runs inside a transaction whose
first statement sets the subject; what statements returning rows mean for its creation order;
the form of the "exactly one current owner" constraint; whether tables reference Access by
foreign key. A workspace is known only through an access to it (INV-ACCESS-09), and owning an
organization never lets a person act in a workspace by itself (INV-ACCESS-05).

What the documentation states, read on 2026-10-21:
- PostgreSQL 17, `CREATE POLICY`: when a data-modifying statement has a `RETURNING` clause, the
  new or updated rows must satisfy the relation's `SELECT` policies, otherwise an error is
  raised; they are never silently ignored. An `UPDATE` that reads the relation applies the
  `SELECT` policies too.
- EF Core: database-generated values are read back after an insert (Npgsql, "Generated
  properties"); `SaveChanges` orders inserts so that the model's constraints are not violated
  and fails on a circular dependency; since EF Core 7 a single statement is sent without an
  explicit transaction ("What's new in EF Core 7.0"); a transaction interceptor sees a
  transaction start or an existing one being used, and a command interceptor runs before each
  command and may stop it ("Interceptors").

A reproduction on a disposable PostgreSQL 16, on 2026-10-21, as a role without `bypassrls`, with
forced row level security and the policies of ADR-ARCH-032, every insert carrying `RETURNING
xmin, created_at`:
- a full founding passes in the order profile, ownership, organization, membership, access,
  workspace;
- an organization inserted before its ownership row, or a workspace before its access, fails
  with "new row violates row-level security policy";
- an organization committed without its ownership row fails on the deferred foreign key;
- another person can neither give themselves an access to that workspace (under its
  organization or under one they own), nor a membership, nor add a workspace, and sees nothing;
- a policy of another schema calling its own function, which delegates to
  `access.may_act_in`, admits the participant and refuses the other person.

## Decision
1. **One database today, each module extractable.** No module's tables reference another
   module's, Access included: there is no foreign key across schemas, and a reference to another
   module's record is its identifier. A module's only dependency in the database is its own
   function `<module>.may_act_in(workspace_id uuid)`, `security invoker`, `stable`, which
   delegates to `access.may_act_in` and which every policy of the module on workspace data calls;
   this is how such a policy calls `access.may_act_in` (ADR-ARCH-032 §2). Moving the module out
   replaces that one function, for instance by a local copy of workspace accesses fed by Access's
   events. A request that changes two modules writes in at most one of them; the other reacts to
   its integration event (ADR-ARCH-031).
2. **One journal today.** All scripts stay in the runner's single folder and journal
   (ADR-ARCH-019). A module's scripts depend on no other module's scripts, except its
   `<module>.may_act_in` on Access's function, which the global number orders first. Moving a
   module out takes its scripts, named `NNNN__<module>__…`, with it.
3. **Each module chooses its storage and access layer.** A module on the shared database uses
   EF Core at run time by default, one context per module mapping its schema, on the Npgsql
   provider, never creating or altering the schema. Whether the context comes through
   MicroKit.Persistence is decided with MicroKit (#177); where the context and its interceptors
   are registered is decided with the module structure (#174). Every module using EF Core has
   the integration test `MappingMatchesMigratedSchema`: it migrates a PostgreSQL container with
   the runner, applying the portable subset and the same named exclusion list as `sql.md`
   (Auth-coupled scripts), then compares, for every entity of the module's context, its table,
   columns, store types, nullability, primary and unique keys with the catalog, and fails on any
   difference, including a non-null column without a default the model does not map. Foreign
   keys are compared in one direction: every relationship of the model exists in the database;
   a database foreign key the model leaves out is admitted only when it is named in the test's
   list, with its reason. System columns such as `xmin` are outside the comparison.
4. **One transaction per unit of work, its subject first.** A unit of work belongs to one module
   and opens one explicit transaction on its context. Every context of the API:
   - disables EF's automatic transactions, so that no transaction starts outside a unit of work;
   - registers a transaction interceptor that, once the transaction has started and before any
     other command or savepoint, sends `select set_config('app.user_id', @subject, true)`
     through ADO.NET on that transaction's own connection, in both its synchronous and
     asynchronous callbacks, with the subject taken from the validated token as a parameter;
     starting a transaction without a subject, or using a transaction the unit of work did not
     open, is an error;
   - registers a command interceptor that stops any command without a transaction, with an
     error.
   Raw SQL goes through EF's parameterized forms only (ADR-ARCH-032 §3). Work that runs without
   a request, such as an event consumer, has no subject until its first case decides one.
5. **Statements that return rows, and the creation order.** EF may read generated values back
   with `RETURNING`, so a creation inserts each row only once the subject may see it. This
   replaces the order of ADR-ARCH-032 §4: for a founding, the author's profile, their ownership,
   the organization, their membership, their access, then the workspace. To allow it:
   - the ownership's foreign key to its organization is deferred to commit;
   - a workspace access carries its workspace's organization, and its composite foreign key
     `(workspace_id, organization_id)` to `access.workspaces (id, organization_id)` is deferred
     to commit, so that the access names a workspace of that organization;
   - the insert policies of a membership, an access and a workspace admit a row only when the
     subject is the current owner of the organization it names, which the subject can read
     through their own ownership;
   - the EF model declares the relation from the organization to its current ownership, and maps
     the ownership's `organization_id` and the access's `workspace_id` as plain columns, so that
     EF orders the inserts as above and sees no cycle; `MappingMatchesMigratedSchema` lists
     these two foreign keys.
   The founding's integration test, run as `saasbtp_api`, fails when the order is reversed. A
   later process that creates a record and the relation that makes it visible (adding a
   workspace, BP-007) follows the same rule.
6. **Exactly one current owner.** The form proposed by ADR-ARCH-032 §4 is kept:
   `access.organizations.current_ownership_id` is non-null, with a composite foreign key
   `(id, current_ownership_id)` to `access.ownerships (organization_id, id)`, deferrable,
   initially deferred, and `(organization_id, id)` is unique on `access.ownerships`. An
   ownership row that no organization references grants nothing; such rows are not removed
   today. A later root record with an owner takes the same form.

## Consequences
- Moving a module out touches its `may_act_in` function, its scripts and its journal rows, and
  no other module; its storage may then change.
- A module that does not use EF Core states its own guard against drift between its storage and
  its code before its first record.
- A command sent outside a unit of work fails loudly instead of returning no row.
- The mapping test guards against drift between the scripts and the EF model; the integration
  tests of ADR-ARCH-032 §6 guard authorization. Neither replaces the other.
- An update returning rows applies the select policies too: ownership transfer (BP-008) must
  leave the person making it able to see the organization when the update returns, or not read
  values back from it.
- Another person can insert an ownership row on an organization whose identifier they know; it
  reveals nothing beyond what "in use" already does (`naming.md`) and grants nothing. Whether to
  forbid it is decided with ownership transfer (BP-008).
- The reproduction ran on PostgreSQL 16; the tests of decisions 3 and 5 run on the image the
  repository pins (`apps/api/tests/PostgresImage.cs`).
- The exact Npgsql API for `xmin` is confirmed when the first mutable aggregate is mapped
  (`sql.md`, Optimistic concurrency).

## Alternatives considered
- Foreign keys from every module to `access.workspaces` and `access.profiles`: the insert policy
  already requires `may_act_in`, which is false for a workspace that does not exist; the keys
  would add little and tie every module's schema to Access's, against extraction.
- Policies calling `access.may_act_in` directly: moving a module out would mean rewriting every
  policy instead of one function.
- One journal per module now: it does not help while the database is shared, and the module
  prefix already lets a module's scripts leave with it.
- EF Core imposed on every module: a module whose work suits another storage would have to
  justify leaving the default anyway; the default keeps one set of conventions for the common
  case.
- Setting the subject in each repository: every path would have to remember it.
- The workspace visible to its organization's owner: INV-ACCESS-09 and INV-ACCESS-05 make a
  workspace known only through an access.
- Not reading values back on insert: it removes optimistic concurrency from records that are
  later updated, such as the organization in ownership transfer (BP-008).

## References
- PostgreSQL 17, `CREATE POLICY` (`RETURNING` and `SELECT` policies), read 2026-10-21.
- Microsoft Learn, EF Core "Interceptors", "What's new in EF Core 7.0" (transactions, insert
  ordering), `CoreStrings.CircularDependency`, read 2026-10-21.
- Npgsql EF Core provider, "Generated properties", "Concurrency tokens", read 2026-10-21.
- Removed ADR-ARCH-006, tag `archive/pre-reset`.

# ADR-ARCH-032 — Communication and authorization across modules: events through an outbox, one published workspace check, a transaction-local subject

Status: accepted
Date: 2026-10-21
Refs: issue #175; parent #125; ADR-ARCH-020; ADR-ARCH-027; ADR-ARCH-028; ADR-ARCH-031;
`docs/domain/access.md`; BP-001 (`docs/business-processes/BP-001-found-company/`).

## Context
Modules do not reference one another (ADR-ARCH-020) and communicate through integration events
(`architecture.md` §2). The workspace is the tenant, and Access owns workspace access, the only
authorization relation of the product (ADR-ARCH-031, `docs/domain/access.md` INV-ACCESS-07). An
organization is invisible to anyone without a relation to it (INV-ACCESS-09), and ending an access
(BP-005) must take effect without waiting for a token to expire.

Every application table forces row level security, and the API connects as `saasbtp_api`, which
owns nothing and is subject to it (ADR-ARCH-027, ADR-ARCH-028). ADR-ARCH-028 §6 left open how the
API passes the user to each transaction for the policies, and the grants on `auth` that policies
calling `auth.uid()` would need. The API reaches the database through Supavisor in session mode,
so one server connection serves successive requests (ADR-ARCH-028 §4).

BP-001 creates an organization and its first relations before the founder holds any relation, so
its policies must admit that creation without opening a path to someone else's data.

What PostgreSQL 17 documents: `set_config(name, value, true)` applies "only during the current
transaction"; `current_setting(name, true)` returns NULL when the setting is absent; a setting
with a two-part name is accepted for any name. Unique and foreign key checks bypass row level
security.

## Decision
1. **Integration events.** A module publishes an integration event in the same transaction as the
   change it reports, through a transactional outbox, and the event is delivered at least once.
   Every consumer is idempotent, keyed by the event's identifier. A module introduces its outbox
   with the first event a consumer actually needs; no outbox is built ahead of a consumer. Where
   event contracts live is decided with the module structure (#174).
2. **One workspace check.** Access publishes, in its schema, one function: `access.may_act_in(
   workspace_id uuid) returns boolean`, `security invoker`, `stable`. It is true when the subject of
   the current transaction holds a current access to that workspace. It is the only expression of
   "may act in this workspace" in the database, and every policy on workspace data, in any module,
   calls it. It marks the boundary of participation in a workspace, not a permission to perform a
   given action: while no process produces roles (ADR-ARCH-031), which operation a participant may
   perform on which record is a rule of the owning module's domain; a later role or permission is
   published by Access as its own function. Because the function runs with the caller's rights,
   Access's tables carry policies that let a subject read their own rows (their profile, their
   current accesses) without calling `access.may_act_in`, and `saasbtp_api` holds `select` on
   them. That a module never queries Access's tables itself is a code rule, enforced by
   architecture tests, not a database guarantee: every module connects as `saasbtp_api`. The
   function's signature is a published contract of Access, changed only by expand and contract.
3. **The subject of a transaction.** The API validates the user's token, then, as the first
   statement of every database transaction and before any savepoint, sets `app.user_id` to the
   authenticated user's identifier with `set_config('app.user_id', <id>, true)`. A local setting
   is undone by rolling back to a savepoint taken before it, never by one taken after it.
   Access publishes `access.current_profile_id()`, which returns the profile of that user, or
   NULL. Both functions read the subject as `nullif(current_setting('app.user_id', true),
   '')::uuid`: on a reused pooled connection, a setting defined once in the session reads as an
   empty string after the transaction ends, not as NULL. Without a subject, the functions return
   NULL or false and the policies admit no row: they fail closed. Every database access of the
   API runs inside an explicit transaction, and the API enforces it in one place that sets the
   subject when a transaction starts and refuses any command outside one; how it does so with the
   runtime mapping is decided with persistence (#176). `saasbtp_api` receives no grant on the
   `auth` schema, and no policy calls `auth.uid()`.

   Trust boundary: the database does not authenticate `app.user_id`. Any statement run as
   `saasbtp_api` can set it, so the value is trusted only because the API alone holds that role's
   credential and sets the value only from a token it has validated, never from the request body.
   An injection of SQL into the API therefore amounts to impersonating any user: the API sends
   only parameterized statements, and SQL assembled by string concatenation is a defect, refused
   in review and by an analyzer where one is available.
4. **Creation and relations.** Knowing the subject is never enough to create a relation. An insert
   policy on a relation (membership, access, any relation of a later module) admits a row only if
   it names the subject and the subject already holds, in the same transaction or before, the
   relation that the producing process requires on the target. For a creation that precedes any
   relation of its author, the order is: the author's profile, admitted only when its user is the
   subject; the root record; the author's ownership of it, admitted when it names the subject;
   then every other record or relation of the creation, admitted only when the subject is the
   root record's current owner.

   Every root record has exactly one current owner at the end of every transaction, and the
   database enforces it with constraints rather than with a check that reads rows: constraints
   apply outside row level security, while a check that reads rows sees only the subject's own.
   Proposed form, settled with persistence (#176): the root record references its current
   ownership through a non-null foreign key, deferred to the end of the transaction and composite
   with the record's identifier, so that the referenced ownership belongs to that record. A root
   record therefore cannot be committed without an owner; an ownership row that no root record
   references makes no one an owner, so inserting one for someone else's organization grants
   nothing; changing the current owner (BP-008) is an update of the root record, which only its
   current owner may make, and the previous ownership row stays as history. No function runs as
   `security definer` and no role bypasses row level security.

   The policies enforce isolation between organizations and the claims above; process rules finer
   than that, such as the founder's access covering the first workspace only (BP001-R4) or
   ownership never granting access by itself (INV-ACCESS-05), are enforced by the owning module's
   domain and proven by its tests. A statement that returns rows applies the select policies to
   them, so a creation does not return a row before the subject may see it; what this means for
   the runtime mapping is decided with persistence (#176). A later process that creates relations
   for others (an invitation, an added workspace) adds its own insert policies under the same
   rule.
5. **What the API does.** The API checks authentication and the identity level a process
   requires. Data authorization is the policies': the API never re-evaluates "may act in this
   workspace" for data access. It answers "not found" for a row the policies hide, never
   "forbidden". A creation refused because an identifier is already in use is answered as
   naming.md states ("Aggregate identifiers"): the existing resource to whoever may replay it,
   "in use" to anyone else. Every unique or foreign key error caused by another person's data is
   answered only as "in use" or "not found", never with the constraint or the row it names, so
   that nothing is revealed (INV-ACCESS-09). Internally, every such refusal is logged with its
   class (hidden row, identifier in use, constraint, replay), so that the generic answer does not
   hide the cause from operations.
6. **Tests.** Each module's integration tests run as `saasbtp_api` on a database migrated by the
   runner, and prove for its tables:
   - no row without a subject, on a fresh connection and on a reused one after a committed
     transaction;
   - on one connection, a transaction as A then a transaction as B: B sees nothing of A's;
   - a transaction rolled back after its subject was set leaves no subject to the next one;
   - a subject that is not a valid identifier makes the statement fail, never admits a row;
   - no row of a workspace the subject has no current access to, even by identifier;
   - refusal to create a row whose `workspace_id` is a workspace the subject has no current
     access to;
   - refusal of a membership or an access naming the subject in an organization they do not own;
   - refusal of a row naming another person, and of a claim on an existing record;
   - for Access: a root record cannot be committed without a current owner, and an ownership
     inserted for someone else's organization makes no one its owner;
   - for `saasbtp_migrator`: no row through ownership alone.

   These integration tests prove authorization at run time; the architecture tests that keep a
   module out of Access's tables prove a code dependency rule. Neither replaces the other.

## Consequences
- Ending an access (BP-005) applies from the next statement, whatever the token's lifetime.
- The rule exists once in the database. Offline synchronization will derive what a device may
  receive from the same relation; the agreement between that expression and
  `access.may_act_in` is tested when synchronization is built.
- `saasbtp_api` needs `usage` on the `access` schema, `execute` on its two published functions
  and `select` on the Access tables they read, granted by the DbUp scripts that create them
  (ADR-ARCH-028 §2). A module's policies calling an Access function is the one sanctioned
  dependency on Access in the database; whether
  tables may reference Access by foreign key is decided with persistence (#176).
- A statement run outside a transaction, or before the subject is set, sees nothing. A data
  access path that skips the transaction is a defect the module's tests must catch.
- A policy calls `access.may_act_in` per candidate row. Its subject lookup is written so that
  the planner evaluates it once per statement (`(select access.current_profile_id())`), the access
  relation is indexed on profile and workspace, and the cost is measured on the first module with
  volume.
- ADR-ARCH-028 §6 is answered: the user reaches each transaction through `app.user_id`, and no
  grant on `auth` is needed.
- The scripts that create these functions and policies live in the runner's single flat folder,
  `apps/api/database/SaasBtp.Migrations/Scripts/`, named `NNNN__access__<description>.sql`
  (ADR-ARCH-019). The runner applies each script in its own transaction, with DbUp variables
  disabled, so a function body in dollar quotes is applied as written (`MigrationRunner.cs`).
- `access.may_act_in` is the first function the runner creates. Its script first revokes, as
  `saasbtp_migrator`, the default `execute` on functions from `PUBLIC`, then grants `execute` to
  `saasbtp_api` only (ADR-ARCH-028, Consequences).
- A data script on workspace data runs as `saasbtp_migrator` with no `app.user_id`, so these
  policies admit none of its rows; such a script needs a policy for `saasbtp_migrator` alone,
  decided with it (`sql.md`, RLS).
- The tests of decision 6 need the two roles. Today the runner's tests connect as the
  container's superuser (`EmptyDatabaseRun.cs`); these tests apply `infra/supabase/roles.sql`
  to the container first, which holds no Supabase-specific role, run the runner as
  `saasbtp_migrator`, and connect as `saasbtp_api`.

## Alternatives considered
- Each module keeps a local projection of workspace access, fed by events: the rule exists in
  every module, and an ended access stays effective until its event is consumed.
- Policies keyed only on a workspace identifier set by the API for each request, with the API
  checking access: the database would trust any workspace the API sets, and every access path
  would have to repeat the check.
- `request.jwt.claims` and `auth.uid()`, as the Data API does: `saasbtp_api` would need grants on
  the `auth` schema, and the policies would depend on the provider's claim layout.
- A `security definer` function for the bootstrap: it runs as its owner, `saasbtp_migrator`, which
  forced row level security also subjects to the policies (ADR-ARCH-027), so it gains nothing
  without a policy that admits the migrator everywhere.
- A setting scoped to the session instead of the transaction: on a pooled connection, one
  request's user would remain set for the next.

## References
- PostgreSQL 17, "System Administration Functions" (`set_config`, `current_setting`), "Customized
  Options", "Row Security Policies" (referential integrity checks bypass row security), read
  2026-10-21.
- ADR-ARCH-027, ADR-ARCH-028, ADR-ARCH-031; `docs/domain/access.md`.

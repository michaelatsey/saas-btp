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
   "may act in this workspace" in the database. Every policy on workspace data, in any module,
   calls it. Because the function runs with the caller's rights, Access's tables carry policies
   that let a subject read their own rows (their profile, their current accesses) without calling
   `access.may_act_in`, and `saasbtp_api` holds `select` on them. That a module never queries
   Access's tables itself is a code rule, enforced by architecture tests, not a database
   guarantee: every module connects as `saasbtp_api`. The function's signature is a published
   contract of Access, changed only by expand and contract.
3. **The subject of a transaction.** The API validates the user's token, then, as the first
   statement of every database transaction, sets `app.user_id` to the authenticated user's
   identifier with `set_config('app.user_id', <id>, true)`. Access publishes
   `access.current_profile_id()`, which returns the profile of that user, or NULL. Both functions
   read the subject as `nullif(current_setting('app.user_id', true), '')::uuid`: on a reused
   pooled connection, a setting defined once in the session reads as an empty string after the
   transaction ends, not as NULL. Without a subject, the functions return NULL or false and the
   policies admit no row: they fail closed. Every database access of the API runs inside an
   explicit transaction, and the API enforces it in one place that sets the subject when a
   transaction starts and refuses any command outside one; how it does so with the runtime
   mapping is decided with persistence (#176). `saasbtp_api` receives no grant on the `auth`
   schema, and no policy calls `auth.uid()`.
4. **Creation and relations.** Knowing the subject is never enough to create a relation. An insert
   policy on a relation (membership, access, any relation of a later module) admits a row only if
   it names the subject and the subject already holds, in the same transaction or before, the
   relation that the producing process requires on the target. For a creation that precedes any
   relation of its author, the order is: the author's profile; the root record, admitted when a
   subject is set; the author's ownership of it, admitted when it names the subject, and unique
   per root record; then every other relation, admitted when the subject owns the root record
   through that ownership. Unique constraints, which PostgreSQL applies outside row level security,
   stop a second owner. No function runs as `security definer` and no role bypasses row level
   security. A statement that returns rows applies the select policies to them, so a creation does
   not return a row before the subject may see it; what this means for the runtime mapping is
   decided with persistence (#176). A later process that creates relations for others (an
   invitation, an added workspace) adds its own insert policies under the same rule.
5. **What the API does.** The API checks authentication and the identity level a process
   requires. Data authorization is the policies': the API never re-evaluates "may act in this
   workspace" for data access. It answers "not found" for a row the policies hide, never
   "forbidden". A creation refused because an identifier is already in use is answered as
   naming.md states ("Aggregate identifiers"), and every unique or foreign key error caused by
   another person's data is answered only as "in use", never with the constraint or the row it
   names, so that nothing is revealed (INV-ACCESS-09).
6. **Tests.** Each module's integration tests run as `saasbtp_api` on a database migrated by the
   runner and prove, for its tables: no row without a subject, both on a fresh connection and on
   a reused one after a committed transaction; no row of a workspace the subject has no current
   access to, even by identifier; refusal to create a row whose `workspace_id` is a workspace the
   subject has no current access to; refusal of a membership or an access naming the subject in an
   organization they do not own; refusal of a row naming another person; refusal of a claim on an
   existing record; and, for `saasbtp_migrator`, no row through ownership alone.

## Consequences
- Ending an access (BP-005) applies from the next statement, whatever the token's lifetime.
- The rule exists once in the database. Offline synchronization will derive what a device may
  receive from the same relation; the agreement between that expression and
  `access.may_act_in` is tested when synchronization is built.
- `saasbtp_api` needs `usage` on the `access` schema, `execute` on its two published functions
  and `select` on the Access tables they read, granted by the DbUp scripts that create them
  (ADR-ARCH-028 §2). A module's policies
  calling an Access function is the one sanctioned dependency on Access in the database; whether
  tables may reference Access by foreign key is decided with persistence (#176).
- A statement run outside a transaction, or before the subject is set, sees nothing. A data
  access path that skips the transaction is a defect the module's tests must catch.
- A policy calls `access.may_act_in` per candidate row. Its subject lookup is written so that
  the planner evaluates it once per statement (`(select access.current_profile_id())`), the access
  relation is indexed on profile and workspace, and the cost is measured on the first module with
  volume.
- ADR-ARCH-028 §6 is answered: the user reaches each transaction through `app.user_id`, and no
  grant on `auth` is needed.

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

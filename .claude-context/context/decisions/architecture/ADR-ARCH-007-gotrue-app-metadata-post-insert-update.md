# ADR-ARCH-007 — GoTrue writes app_metadata by an UPDATE posterior to the INSERT into auth.users

Status: accepted
Date: 2026-07-12
Refs: ADR-ARCH-005 (auth-boundary identity-provisioning exception), ADR-ARCH-006 (DbUp owns
DDL), conventions/sql.md §Auth-coupled scripts, session 014 (the premise this ADR corrects),
scripts 0004 (the trigger that carried the bug) and 0006 (the fix).

## Context

Story 1 (Safety) requires that a logged-in user always already carry an observer identity, so
`access.profiles` is provisioned at signup by a trigger on `auth.users` (ADR-ARCH-005 exception,
script 0004). 0004 was written `AFTER INSERT ON auth.users`, reading
`NEW.raw_app_meta_data ->> 'tenant_id'` and raising `P0002` when absent.

It raised on EVERY signup, including the nominal Admin-API path — the path it was designed to
accept. The cause is a property of GoTrue that is contre-intuitive, undocumented on the Supabase
side, and cost us a session to find:

**GoTrue does not set `app_metadata` during the INSERT of `auth.users`. It applies the caller's
`app_metadata` by a separate UPDATE issued AFTER the INSERT, inside the SAME transaction.**

Proof, three independent strands:

1. Source — `supabase/auth`, `internal/api/admin.go`, `adminUserCreate`:

   ```go
   user.AppMetaData = { "provider": ..., "providers": [...] }   // provider blob ONLY

   db.Transaction(func(tx) {
       tx.Create(user)                                  // the INSERT: app_metadata = provider blob only
       ...
       user.UpdateAppMetaData(tx, params.AppMetaData)   // the UPDATE: the caller's tenant_id lands HERE
       ...
   })
   ```

   The caller-supplied `app_metadata` (our `tenant_id`) is written by an UPDATE that runs after the
   INSERT. `UpdateAppMetaData` MERGES (it does not clobber), so the provider blob survives. The
   `UpdateAppMetaData` call is guarded by `if params.AppMetaData != nil`, so the Dashboard "Add
   user" modal, OAuth, magic-link and public signup emit NO such UPDATE at all. `raw_user_meta_data`
   (hence `full_name`) IS populated at the INSERT and does not move.

2. Real Postgres logs from the failed 0004 runs: `P0002` raised on the nominal signup, with the
   INSERT `NEW.raw_app_meta_data` carrying only `provider`/`providers`, never the `tenant_id` the
   caller passed.

3. Upstream issue supabase/auth #1280 ("Create User with AppMetadata"), still OPEN: callers ask for
   `app_metadata` supplied to `createUser` to be usable from signup-time logic, because today it is
   applied only after creation — the same behavior observed here.

An `AFTER INSERT FOR EACH ROW` trigger fires BEFORE that post-INSERT UPDATE, so it structurally
cannot see the `tenant_id`. The transaction IS atomic — but the trigger reads a snapshot in which
the data it needs does not exist yet. Session 014's premise ("an AFTER INSERT row trigger runs
inside the inserting transaction: true transactional atomicity" — used to reject the async
alternatives) was therefore misleading: the atomicity is real, but it covers a value the trigger
has not yet been handed.

## Decision

Record the fact as durable architecture, and adopt the rule that follows for ANY trigger on
`auth.users`:

- **A signup-time trigger that must read caller-supplied `app_metadata` MUST also fire on
  `UPDATE OF raw_app_meta_data`, not on `INSERT` alone.** The `tenant_id` arrives on that UPDATE.
- **An invariant over the final auth row ("a profile must exist") MUST be enforced at COMMIT, not at
  INSERT** — a `CONSTRAINT TRIGGER ... DEFERRABLE INITIALLY DEFERRED`, which fires after the
  post-INSERT UPDATE. Such a guard must depend only on immutable columns (`NEW.id`), never on
  `raw_app_meta_data`, so it can never read a value that is merely not-yet-written.
- **Never treat "`app_metadata` present in `NEW` at INSERT" as guaranteed.** Provisioning waits (a
  no-op) until the value is present; it does not raise on its absence at INSERT.
- **Stated runtime dependency — the deferred guard's correctness requires the constraint to STAY in
  DEFERRED mode for GoTrue's signup transaction.** This is the second load-bearing assumption, of the
  same class as the post-INSERT UPDATE above, and equally outside the repo's power to enforce. A
  `SET CONSTRAINTS ALL IMMEDIATE` anywhere in GoTrue's path (a future GoTrue change, a connection-pool
  setting, an admin session) would make `on_auth_user_profile_required` fire at INSERT time — before
  the post-INSERT UPDATE has written `tenant_id` — and would resurrect the exact 0004 bug (every
  Admin-API signup rejected). No migration, grant, or trigger definition can prevent a caller from
  issuing `SET CONSTRAINTS ALL IMMEDIATE`. The manual validation against a real Supabase project is
  the empirical proof that deferral holds: negative test 6a (a signup with no `tenant_id`) failing
  with `P0002` AT COMMIT rather than at the INSERT IS the observable demonstration that the constraint
  is deferred as designed. If that case ever starts failing at INSERT, the deferral has been broken.

This is implemented by 0006, which splits 0004's single `AFTER INSERT` trigger into
`access.provision_profile` (INSERT + `UPDATE OF raw_app_meta_data`) and the deferred guard
`access.assert_profile_provisioned` (constraint trigger at COMMIT).

## Consequences

- Any future trigger touching `auth.users` starts from this fact instead of rediscovering it. The
  cost of the discovery (one session) is paid once.
- The nominal Admin-API signup succeeds; the V1 rejection of a tenant-less signup still happens, but
  at COMMIT (the deferred guard) rather than falsely at INSERT. The set of flows that fail by design
  is unchanged (Dashboard "Add user", OAuth first login, magic-link/OTP, anonymous) — see the
  validation checklist.
- Harder: reasoning about `auth.users` triggers now requires knowing GoTrue's INSERT-then-UPDATE
  shape. That knowledge lives here so the trigger scripts stay legible.
- This is coupling to a GoTrue implementation detail. It is acceptable because it is fenced to the
  auth-coupled scripts (sql.md §Auth-coupled scripts), validated only against a real Supabase project
  (never a fabricated `auth.users`), and re-checkable if GoTrue changes: if #1280 is ever resolved so
  `app_metadata` is set at INSERT, the INSERT arm alone would suffice — but the UPDATE arm remains
  correct and harmless either way.

## Alternatives considered

- **Keep the `AFTER INSERT` trigger, read `app_metadata` at INSERT.** Rejected: it is the bug. The
  value is not present at INSERT.
- **Move `tenant_id` into `raw_user_meta_data` (which IS set at INSERT).** Rejected: `user_metadata`
  is client-writable, so a signup could carry a foreign `tenant_id` — a trivial cross-tenant
  escalation. `tenant_id` must stay server-set in `app_metadata` (ADR-ARCH-005, session 014).
- **Async provisioning (webhook / Edge Function / Database Webhook after commit).** Reconsidered now
  that the "true atomicity" argument that rejected it in session 014 is corrected. Still rejected for
  V1: the 0005 JWT hook READS `access.profiles`, so a profile that does not exist yet at first token
  issuance ships a token without `observer_name`. The deferred-constraint design keeps provisioning
  inside the signup transaction (no window) without depending on data the trigger cannot yet see,
  which is strictly better than an out-of-transaction async path here.
- **A new ADR vs. a script comment.** Chose an ADR: the fact is contre-intuitive, undocumented by
  Supabase, and load-bearing for every future `auth.users` trigger. A comment buried in 0006 would
  not be found by the next person designing such a trigger.

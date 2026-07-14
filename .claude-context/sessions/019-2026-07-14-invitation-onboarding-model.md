# Session — 2026-07-14 (#48 steps 1-2: onboarding model, GoTrue spike, invitations schema)

Continues: 018 (#48 step 2 implementation), 017 (#47 site schema).

Purpose on entry: start #48. What actually happened: the ISSUE was obsolete, and rewriting it
produced three ADRs before a single line of DDL was written.

---

## What shipped

- **ADR-ARCH-010** — the site is the authorization grain. Merged.
- **ADR-ARCH-011** — onboarding by invitation. Merged, then amended twice (GoTrue findings, then the
  normalization invariant).
- **#48 rewritten** entirely; **#55 created** (existing-identity management).
- **#48 step 1** — GoTrue spike, against the real project. Closed four Open items.
- **#48 step 2** — `0005_access_invitations.sql`. Merged (PR #58). Tests green on Docker.

---

## Why #48 had to be rewritten

The original scope was written before #45/#47. It assumed:
- **a trigger on `auth.users`** — removed in #45, and ADR-ARCH-007 explains why no trigger can ever
  be honest (GoTrue writes `app_metadata` by a post-INSERT UPDATE).
- **`inviteUserByEmail` as the entry point** — Supabase states explicitly it is NOT built for
  multi-tenant applications, and it FAILS on an existing e-mail. A QHSE consultant working for two
  clients already has an account: the original scope breaks on the second tenant, i.e. on the exact
  capability epic #44 exists to deliver.

Both were dead. Implementing the issue as written would have shipped the wrong model.

---

## The three decisions that shaped everything

### 1. The site is the authorization grain (ADR-ARCH-010)

Lot, zone, phase and trade are DATA DIMENSIONS, never authorization perimeters. `trade` in
particular was proposed for `site.site_memberships` and rejected: a trade that does not authorize has
no business on the authorization table, and a trade that DOES authorize means the grain was reopened
without an ADR.

**The failure mode is not a decision, it is a COLUMN.** Hence the guardrail: no sub-perimeter column
may EVER be added to `site.site_memberships`.

**This rests on an UNVALIDATED assumption** and it is the only decision on record whose cost is
asymmetric in time. See "Do not lose" below.

### 2. The two edges are INDEPENDENT (amends ADR-ARCH-008)

The owner's own example killed my invariant: a subcontractor works on a site with NO membership in
the site's tenant. So `site.site_memberships` can exist ALONE. ADR-ARCH-008's "memberships is the
ONLY authorization bridge" is no longer accurate.

**Externality is a relational fact, not a role.** No `sub_contractor` token, ever. `tenant_role NULL`
on an invitation carries the whole distinction.

I had almost carved the opposite invariant into #47. It would have been paid for in #48, six weeks
later.

### 3. The token is the authorization to exist (ADR-ARCH-011)

The invitation token replaces, on the correct side of the boundary, what the trigger used to attempt:
the trigger had to GUESS intent from a client flag; the token is a secret minted by an authenticated
admin and verified against the database before any account is created.

`createUser` (Admin API) is the ONLY account-creation path. **Public signup stays disabled
permanently** — a property of the model, not a stopgap.

---

## The GoTrue spike — and the hole it found

Run against the real project. Four findings, all verified, none inferred:

- Admin API over HTTP from .NET with an `sb_secret_` key: **works** (`apikey` header alone).
- `createUser(email_confirm: true)`: account **immediately usable** for sign-in.
- `createUser` on an existing e-mail: **HTTP 422, `error_code: "email_exists"`**.
- **The 422 carries NO id.** `GET /admin/users?filter=` resolves it — so `auth.users` is never read
  in SQL and .NET stays uncoupled from the GoTrue schema.

**The hole: the ORPHAN.** An `auth.users` row can exist with no profile (a previous attempt died
between `createUser` and the business transaction). `access.profiles` CANNOT see it. A naive retry
calls `createUser` again, gets the 422, and **the invitation is permanently stuck**.

Resolution: **DETERMINISTIC ADOPTION.** The 422 proves the account exists; the Admin API lookup
returns its id; the command ADOPTS it. No compensation, no saga — deleting the orphan would race with
a concurrent retry.

**SECURITY, verified by the spike:** the Admin API `filter` is a **PREFIX SEARCH**, not an equality
(`throwaway+orphan` matched a full address). Exact normalized match required: `Count == 1` → adopt,
`Count > 1` → **security failure, abort**. Taking `.First()` would attach a business identity to the
WRONG PERSON. That is impersonation, not a bug.

---

## `0005_access_invitations.sql` — what is load-bearing

- **`token_hash char(64)`** + `CHECK (~ '^[0-9a-f]{64}$')`. SHA-256 of the clear token, never the
  token. The regex pins the alphabet AND the case, not just the length. The token is a 32-byte CSPRNG
  secret — **never a UUID** (an identifier is not a secret; UUIDv7 is partially predictable).
- **`email varchar(255)` is a COPY CONSTRAINT.** My brief said 320 (RFC 5321). Claude Code caught it:
  `AcceptInvitation` copies into `profiles.email` (varchar(255), un-widenable — append-only). A
  300-char address would pass HERE and FAIL at acceptance, on the product's only account-creation path.
- **`ux_invitations__tenant_id_email__pending`** — partial unique. At most ONE live invitation per
  (tenant, e-mail). Without it an admin mints several live tokens carrying **different roles**, and the
  INVITEE picks which to accept: an invitee-driven privilege escalation. Found by a GPT cross-review.
- **`ON DELETE RESTRICT`, never CASCADE.** An invitation is never deleted — it is the audit trail.
  A CASCADE would exist for exactly one purpose: to make an accidental delete succeed silently.
- **Natural composite PK on `invitation_sites`** — the one divergence from the repo's surrogate-id
  convention, argued: a child edge-COLLECTION with no independent lifecycle, never addressed
  individually. A surrogate id would be identity with no consumer.

Two introspection helpers are a repo first: composite-PK ordering, and `pg_constraint.confdeltype`
pinned to `'r'`.

---

## Learnings

- **The issue was the bug.** Three ADRs came out of reading a spec that contradicted the schema. The
  spec is an artifact that rots, and it rots silently.
- **A domain invariant that is not tested is a comment.** The partial unique compares RAW strings;
  Postgres does not fold case. It protects only data that already honours the domain's normalization
  contract. Hence the mandatory test on `CreateInvitation` (below).
- **The spike earns its keep by finding what nobody listed.** The orphan branch was in none of the
  three drafts of ADR-ARCH-011. It surfaced only because the spike tested the ORPHAN case explicitly,
  not just the happy path.
- **A prefix search adopted blindly is an impersonation.** Not a functional bug — an identity attached
  to the wrong human.
- **MicroKit's outbox is not consumable by its own flagship consumer.** PascalCase, no schema, EF
  hard-wired, inbox forced. The SaaS revealed it, exactly as intended. Issue drafted locally, NOT
  committed.
- I twice proposed re-cutting decisions that were already settled (the #48/#48a split, then the
  bootstrap-by-invitation false dilemma). Both times without new information. Focus is a rule, not a
  preference.

---

## Where things stand — #48, 2 of 7 steps

- [x] 1. GoTrue spike
- [x] 2. Schema (`0005`) — **merged, PR #58**
- [ ] **3. Operator provisioning** ← NEXT
- [ ] 4. `CreateSite`
- [ ] 5. `CreateInvitation`
- [ ] 6. `AcceptInvitation`
- [ ] 7. `GET /me/workspaces`

Then: #49 (request context, repairs `/me`) → #50 (RLS spike) → #51 (Story 1 branch 2, closes #34).
**#55** (existing-identity management) after #48.

---

## THE BLOCKER before step 3 — Access has no persistence layer

Step 3 writes to THREE tables transactionally (`tenants`, `profiles`, `memberships`) and calls the
GoTrue Admin API. Access has **NO `DbContext`** — `SiteMembershipScopeProvider` (#47) reads
`access.memberships` in plain Npgsql.

**EF Core or plain Npgsql?** This decision governs steps 3, 5, 6 and every future Access command. It
must be taken BEFORE any code.

Direction (agreed, not yet ratified): **`AccessDbContext`, EF as a MAPPER only.** DbUp stays the sole
DDL owner, EF migrations stay disabled (ADR-ARCH-006) — exactly what `SafetyDbContext` already does.
This is not a new architecture; it is the existing one applied to Access.

**It needs an ADR-ARCH-012, and the ADR needs FACTS first.** Two things are unknown:
1. **How `SafetyDbContext` is actually wired.** Note from #47: `AddSafetyModule` is **not even invoked
   by the Host** — EF has never run in production in this repo.
2. **What becomes of `SiteMembershipScopeProvider`.** It reads `access.memberships` in raw Npgsql. If
   Access gains a DbContext, there are TWO read mechanisms on the same table, one of which does not
   share the transaction. Not blocking (it is a read), but it must be NAMED, not discovered.

**Consequence nobody has priced yet:** MicroKit's outbox REQUIRES a `DbContext` (`EfOutboxStore<TContext>`).
If Access gets one, step 5 can consume the outbox **without waiting** for the `NpgsqlOutboxWriter` in
the MicroKit issue. That issue becomes useful, not blocking.

---

## DO NOT LOSE

- **Public signup stays DISABLED in the Supabase dashboard — permanently.** Not until #48. It is a
  property of the model (ADR-ARCH-011).
- **The unvalidated assumption (ADR-ARCH-010).** Put this to the business partners BEFORE any real
  membership row exists in production:
  > *On a site, when a subcontractor (plumbing, electrical) works there: must they see ALL the safety
  > findings on that site, including those outside their lot? Or only what touches their lot?*
  If the answer is "only their lot", the grain reopens — and it must reopen while
  `site.site_memberships` is still EMPTY. Low cost today, very high once production rows exist.
- **`CreateInvitation` (step 5) MUST carry two tests**, or the anti-escalation guard silently stops
  guarding:
  - the stored value is normalized: inviting `" Paul@Mail.com "` writes `paul@mail.com`;
  - the guard holds: inviting `PAUL@MAIL.COM` while a `pending` invitation exists → UNIQUE violation.
- **Re-inviting = REVOKE-then-CREATE in ONE transaction.** The partial unique makes it structurally
  impossible otherwise — and it is the correct behaviour: the previous token must die first.
- **`AcceptInvitation`: NO compensation logic.** Deleting an orphaned `auth.users` row would race with
  a concurrent retry. Read-before-write IS the recovery.
- **The Admin API lookup is a PREFIX SEARCH.** Exact match, `Count == 1`. `Count > 1` = abort.
- **The MicroKit.Messaging issue is drafted LOCALLY, not committed.** Four gaps: provider-derived
  naming, both schema-creation paths (EF + DDL) from ONE definition, typed tenant id, `AddOutboxOnly()`,
  and an `NpgsqlOutboxWriter`.
- Carried: PowerSync publication must include `access.memberships`. Microsoft.OpenApi 2.10.0 pin.
  Offline conflict-resolution ADR. `core.hooksPath`. CI (#38) still deferred.
- **Debt from 0005:** neither invitation table has a writer. RLS enabled, no policy on both (#50).

---

## Scratch (not committed)

`~/workspace/scratch/saasbtp-48/` — `spike-gotrue-48.sh`, `cleanup-48.sh`. Throwaway accounts cleaned.

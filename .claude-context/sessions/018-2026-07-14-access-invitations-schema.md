# Session — 2026-07-14 (#48 STEP 2: access invitations schema, 0005 + anti-drift tests)

Continues: 017-2026-07-14-site-schema-and-scope-provider.md.

Implementation record for #48 **STEP 2 ONLY** — the storage for onboarding by invitation
(ADR-ARCH-011). The plan was reviewed and corrected over three rounds before implementation; the
settled decisions live in ADR-ARCH-011 and the approved plan. The other steps of #48 (operator
provisioning, `CreateSite`, `CreateInvitation`, `AcceptInvitation`, `GET /me/workspaces`) are LATER and
OUT OF SCOPE — this step creates STORAGE and nothing else.

---

## What this step delivers

### The schema — `0005_access_invitations.sql` (PORTABLE, append-only)

Two tables, each total, neither polymorphic (ADR-ARCH-011). The `access` schema already exists (0002),
so no `CREATE SCHEMA`.

- `access.invitations` — the onboarding workflow, NOT an authorization edge.
  - `id` (UUIDv7, no DB default) PK; `email` varchar(255) NORMALIZED lower(trim) — see the width fix
    below; `token_hash` char(64) UNIQUE — SHA-256 lowercase hex, NEVER the clear token; `status`
    varchar(20) CHECK (pending|accepted|revoked — `expired` is DERIVED, never stored; `revoked` not
    `cancelled`); `expires_at` timestamptz (domain-stamped, no default — the 7-day validity is
    CONFIGURATION); `tenant_id` uuid NOT NULL NO FK (issuer's tenant, the authorization anchor);
    `tenant_role` varchar(20) NULL CHECK (NULL OR owner|admin|member) — NULL = EXTERNAL invitee, no org
    edge; `invited_by` uuid NOT NULL NO FK (audit); `created_at`; `accepted_at` NULL.
  - `ck_invitations__token_hash_hex` CHECK (`token_hash ~ '^[0-9a-f]{64}$'`) — pins length + lowercase
    hex alphabet, making '', 'abc', an uppercased hash, or a leaked clear token impossible.
  - `ux_invitations__tenant_id_email__pending` — a PARTIAL UNIQUE INDEX on (tenant_id, email)
    WHERE status = 'pending'. The ONLY index in the script. See "Review corrections" (2).
- `access.invitation_sites` — the 0..N site edges an invitation will grant, one row per site. MIRRORS
  `site.site_memberships` (type + tokens + validity window) so acceptance is a COPY, not a translation.
  - Columns: `invitation_id`, `site_id`, `site_role` varchar(20) CHECK (site_manager|member),
    `valid_from` timestamptz, `valid_until` timestamptz NULL. `ck_invitation_sites__valid_window`
    mirrors `ck_site_memberships__valid_window`.
  - **Natural composite PK `(invitation_id, site_id)`** — no surrogate id (see "Design decisions").
  - `fk_invitation_sites__invitations (invitation_id) -> access.invitations (id) ON DELETE RESTRICT` —
    the ONLY FK, intra-context. RESTRICT (not CASCADE) because an invitation is NEVER deleted (audit
    trail); a CASCADE would exist only to make an accidental parent delete succeed silently. `site_id`
    carries NO FK (cross-context).
- RLS ENABLED, NO policy on both tables. Only consumer is the privileged .NET owner (bypasses RLS).
  RLS-on/no-policy returns ZERO ROWS SILENTLY to any non-bypassing role — a policy is MANDATORY the day
  a non-privileged consumer reads these (#50). The #45 lesson.

Portable (names no GoTrue object) => NOT added to `AuthCoupledScriptMarkers`; the anti-drift harness
applies it on bare Postgres automatically. Added `[InlineData("0005_access_invitations")]` to
`MigrationScriptClassificationTests`.

---

## Design decisions (argued in the approved plan)

1. **Natural composite PK on `invitation_sites`, not a surrogate `id`.** The invariant "one site edge per
   (invitation, site)" IS the identity — the key is the guard, no separate unique. `invitation_sites` is
   a child value-COLLECTION of the Invitation aggregate (ADR-ARCH-011: "an edge COLLECTION, not part of
   the invitation's identity"): no independent lifecycle, no inbound FK, never addressed individually. A
   surrogate id would be identity with no consumer — speculative (the #45 discipline). This is the ONE
   place 0005 diverges from the repo's surrogate-`id` convention on the two membership tables (which are
   authorization edges with a per-request `is_active` lifecycle, so they earn an id). Reversible
   append-only if ever needed.
2. **`ON DELETE RESTRICT` is the first explicit FK delete action in the repo** (memberships/site edges
   use implicit NO ACTION). Deliberate: the named action documents the never-delete intent and makes an
   accidental parent delete fail loudly. Asserted via `pg_constraint.confdeltype = 'r'`.
3. **`site_role`/`tenant_role` column names** disambiguate the two edge kinds one invitation carries;
   "mirror site_memberships exactly" means type + token set + validity window (so acceptance is a
   value-for-value COPY), NOT the literal column name (`site.site_memberships`'s column is `role`).

---

## Review corrections (applied before implementation)

1. **Email width 320 -> 255 (a real latent bug the brief carried).** `AcceptInvitation` COPIES the
   invitation e-mail into `access.profiles.email` (varchar(255), 0002, cannot be widened — append-only).
   A 300-char address would pass the invitation insert and FAIL at acceptance — a silent divergence on
   the product's ONLY account-creation path. Real addresses >255 do not exist; matching the DESTINATION
   column beats RFC 5321's theoretical 320. Commented in the DDL as a COPY CONSTRAINT so nobody "fixes"
   it back to 320.
2. **`ux_invitations__tenant_id_email__pending` added (a real gap).** Without it an admin could mint
   several LIVE tokens for the same person in the same tenant — possibly with DIFFERENT `tenant_role`
   values — and the INVITEE chooses which to accept: an invitee-driven privilege escalation, the exact
   family #45 closed. The partial unique makes it structurally impossible; scoped to `status = 'pending'`
   so re-inviting after revoke/accept stays allowed. Re-inviting therefore means REVOKE-then-CREATE in
   one transaction (CreateInvitation, a later step).
3. A §6 wording fix: dropped a false claim that a partial index "partially serves" a general tenant scan
   (a partial index only serves queries whose predicate is implied by its WHERE). The correct reason to
   skip a standalone tenant_id index is simply: no reader yet.

---

## Verification

Docker is NOT available in this environment, so the Testcontainers-backed schema test could not be
executed here. It is WRITTEN and COMPILES; run it where Docker is present (no CI yet — #38).

- Build: `dotnet build SaasBtp.Database.Migrator.Tests.csproj` — 0 warnings, 0 errors.
- Ran (non-Docker), green: migrator classification **6/6** (new `[InlineData("0005_access_invitations")]`
  resolves to the embedded resource and is portable).
- NOT run here (Docker-gated), written + compiling: `InvitationsSchemaTests` — anti-drift over both
  tables. POSITIVE: exact columns/types/lengths/nullability, PK (single + composite), the single
  RESTRICT FK (`confdeltype`), token_hash UNIQUE, the partial unique index WITH its WHERE predicate, all
  CHECKs, RLS enabled, no-default on every domain-stamped timestamp. NEGATIVE: `access.invitations` has
  ZERO FKs (no FK to access.tenants, no auth.*), `access.invitation_sites` has EXACTLY ONE FK (RESTRICT,
  never CASCADE), and neither table carries a speculative read-path index (no `ix_..._email`,
  `ix_..._tenant_id`, `ix_..._site_id`).
  - Two introspection helpers are NEW vs the 0004 test: a composite-PK assertion, and reading
    `pg_constraint.confdeltype` (cast to text) to pin the ON DELETE action — the first place the repo
    asserts a delete action. The partial-index predicate is asserted by robust substrings ("UNIQUE",
    "tenant_id", "email", "WHERE", "status", "'pending'"), NOT a hand-guessed parenthesization, because
    pg_get_indexdef casts a varchar column to text in the rendered predicate.

---

## Debt handed forward (storage-first, intentional — stated so it is inherited, not rediscovered)

1. **Neither table has a WRITER.** Population starts with `CreateInvitation` / `AcceptInvitation` — LATER
   steps of #48. Same debt #47 carried for the site tables.
2. **RLS is enabled with NO policy** on both. A policy becomes MANDATORY before any non-privileged
   consumer (PowerSync) reads them — that is #50.
3. **The e-mail / outbox delivery table is a SEPARATE later migration** (MicroKit.Messaging needs work
   before it can be consumed here). It is NOT in 0005 and was deliberately not scaffolded.

Recorded in three places: the approved plan, this session file, and `build-checklist.md`.

---

## Where things stand

- #48 **STEP 2 (storage)** implemented on branch `feature/access/invitations-schema`, UNCOMMITTED. The
  human runs every git operation (Claude never touches git). Docker-gated test to be run in a Docker
  environment before merge.
- The migration set is append-only. 0005 is the next script after 0004; 0001–0004 untouched.
- Next within #48: operator provisioning, `CreateSite`, `CreateInvitation`, `AcceptInvitation`,
  `GET /me/workspaces` (the ADR-ARCH-011 commands that WRITE these tables) — then #49 -> #50 -> #51.
- Carried, unchanged: public signup stays DISABLED (a property of the model now, ADR-ARCH-011, not a
  stopgap); the RLS policy on these tables is #50.

---

## Files touched

Added: `0005_access_invitations.sql` (migrator scripts); `InvitationsSchemaTests.cs` (migrator tests).
Modified: `MigrationScriptClassificationTests.cs` (+`[InlineData("0005_access_invitations")]`);
`build-checklist.md`.

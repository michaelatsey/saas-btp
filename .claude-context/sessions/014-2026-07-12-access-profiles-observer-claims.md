# Session 014 — Access: profiles identity projection + observer claims in JWT

Date: 2026-07-12
Issue: #39 (closed by this PR). Unblocks #34 (Story 1) branch 2.
Branch: feature/access/profiles-observer-claims
Also created: #38 (CI pipeline — deliberately deferred until after Story 1 branch 2)
Refs: ADR-ARCH-005 (amended), ADR-ARCH-006, sql.md, safety-domain-model.md

## Starting point
Story 1 foundation (PR #36) was merged: DbUp-owned safety.constats, Constat aggregate, first
DbContext, anti-drift proven on postgres:17-alpine. Branch 2 (repository, CreateConstat, POST/GET)
was next.

## The blocker we found before writing branch 2
`Constat.Create` rejects a blank `ObserverSnapshot.FullName` (a fix we made ourselves, from the
public-API review). But the Supabase JWT carries only `sub`, `email`, `app_metadata.tenant_id` —
**no name exists anywhere in the system**. The branch-2 handler could not build a valid
ObserverSnapshot; `POST /constats` would fail for every user.

Not a Safety gap: an Access gap. The product had an *identity* (GoTrue) but no *person*.
Story 1 branch 2 was blocked. #39 was created to fix it, minimally.

## Decisions frozen

- **`access.profiles` is an identity PROJECTION, not an aggregate.** No Access.Domain model, no
  repository, no .NET code touches it. Nothing consumes one. The absence of a domain model is
  deliberate — a Profile aggregate arrives with a real use case ("edit my profile"), not before.
- **Safety never queries Access at runtime.** ObserverSnapshot is built from JWT claims alone: zero
  DB read, zero project reference. The snapshot is frozen anyway, so claim freshness has no value —
  a stale claim is arguably *correct*.
- **Claims are named for their CONSUMER, not after Access's columns.** `access.profiles.full_name`
  → claim `observer_name` → `ObserverSnapshot.FullName`. Access stays free to rename its column
  without breaking Safety. (Each bounded context speaks its own language; the contract translates.)
- **`tenant_id` is NEVER client-supplied.** A signup carrying a foreign tenant_id would be trivial
  cross-tenant escalation. Server-set only (`raw_app_meta_data`). `full_name` MAY come from the
  client — it is the user's own data, like any registration form.
- **Provisioning = Postgres trigger on `auth.users`, atomic.** An AFTER INSERT row trigger runs
  inside the inserting transaction: true transactional atomicity. A webhook/Edge Function would only
  give "no window in practice" — and the JWT hook READS profiles, so a not-yet-created profile means
  a token without observer_name. Rejected the async alternatives on that basis.
  > **[AMENDED 2026-07-12 — see the Amendment section below.]** The atomicity premise was misleading
  > in practice: GoTrue writes `app_metadata` by an UPDATE *posterior* to the INSERT, so an
  > `AFTER INSERT` trigger runs before the `tenant_id` exists. The atomicity is real but covers a
  > value the trigger has not yet been handed. The async rejection still stands, on a corrected basis
  > (the JWT hook read). Fixed by 0006; the fact is now ADR-ARCH-007.
- **The trigger RAISEs (P0002 tenant_id / P0003 full_name) on missing data.** Documented explicitly
  as a **V1 PRODUCT constraint, not a technical law**: every human user files constats, a constat
  needs an observer name. A blank-name profile or a missing profile would just relocate the failure
  to constat-time (opaque, late). Must be revisited if enterprise SSO/OIDC or service accounts
  arrive — they may not supply a name.
- **Invariant (V1): one user belongs to exactly one tenant.** `user_id` as PK encodes it.
- **Tenant assignment flow (invitation, company code, first-admin) is OUT of scope** — its own story.
  Assignment stays server-side/seeded.

## The RLS finding that shaped the design
Our Safety pattern was "RLS enabled, no policy" (safety.constats has no non-privileged consumer).
**That pattern is WRONG for profiles**: the JWT hook runs as `supabase_auth_admin` — a
non-privileged role. With RLS enabled and no policy, it reads **ZERO ROWS silently** — not an error.
The token ships without observer_name, the user logs in, and cannot file a constat, with no signal
anywhere. So profiles gets exactly ONE policy: `FOR SELECT TO supabase_auth_admin USING (true)`.

`USING (true)` is correct and must NOT be "fixed" with a tenant_id predicate: supabase_auth_admin is
trusted GoTrue infrastructure, not a tenant, and has no tenant context — a predicate would match
zero rows and silently truncate every token. This is commented in-script, with the consequence named.

## ADR-ARCH-005 amended (not a new ADR)
Identity provisioning is an auth-boundary concern — a **bounded** exception to "the .NET domain is
the write authority". The boundary, stated explicitly so nobody cites the trigger later to justify
bypassing the domain:
- ALLOWED: auth lifecycle → identity projection.
- FORBIDDEN: business command → direct SQL write.
Rejected creating an ADR-ARCH-007: one ADR per half-page exception fragments the architecture.

The trigger's own logic boundary (precise wording, corrected mid-session): NO business computation,
NO business decision, NO authorization, NO orchestration — ONLY the minimal validation required for
the identity projection. The mandatory name IS one functional rule, and that is allowed. Membership,
roles, permissions, computed state remain forbidden.

## What shipped
Five DbUp scripts (0002 portable; 0003 permissions; 0004 trigger; 0005 hook), a MigrationRunner
portable/auth-coupled split (single source of truth — an unlisted auth-coupled script fails loudly),
`SaasBtp.Database.Migrator.Tests` (co-located with the migrator, NOT an `Access.IntegrationTests` —
there is no Access runtime module and creating one would fabricate a phantom module), docs, and a
manual validation checklist.

**Zero .NET runtime change.** The hook stamps `tenant_id` NESTED inside `app_metadata` because the
existing MicroKit `SupabaseClaimsMapper` reads it there (and also reads `roles` there) — so the hook
MERGES, never clobbers. observer_name/observer_function are top-level (greenfield).

## Reviews (fresh context, four passes across the session)
Architect, dependency, public-API (on the Safety foundation), then a pre-deployment security review
on #39's privileged SQL. The security review traced every hook raise-path, verified the app_metadata
merge is additive and type-matches the real .NET mapper, checked every reference under
`search_path = ''`, and confirmed the grants are minimal. Verdict: apply with fixes — all four fixes
were documentation/operational, not correctness.

The review's most valuable finding was operational, not a bug (see below).

## Fixes applied from the security review (this session — docs/comments only, no code, no schema change)
Two hook properties re-verified and left as-is: the profile lookup is a non-STRICT `SELECT … INTO`
(zero rows → `NOT FOUND` → silent nominal path, never an incident log — STRICT would have made the
`IF NOT FOUND` guard dead code and polluted the logs), and the trigger returns `NEW`. Four fixes,
all documentation:
1. Checklist: added a prominent "EXPECTED BEHAVIOR" section — users are Admin-API-only; Dashboard
   "Add user" / OAuth / magic-link / anonymous all fail P0002 by design, not a fault.
2. 0004 + 0005 inline rollback comments: prepended "disable the hook in the dashboard FIRST" so the
   scripts and the checklist agree on recovery order (they previously showed only the DROP statements).
3. 0005 comment: softened the absolute "NEVER raises" to the honest form (`WHEN OTHERS` does not catch
   QUERY_CANCELED) — comment only, the code is NOT changed and query_canceled is deliberately not trapped.
4. 0003 comment: named the consequence of "fixing" the `USING (true)` policy with a tenant_id predicate.

## Docs conformance pass (ADR / convention verification, this session)
Verified against the repo (not taken on faith): the ADR-ARCH-005 amendment states both sides
(ALLOWED auth-lifecycle projection / FORBIDDEN business-command SQL); decisions-index row reflects it
and every ADR file on disk has a row; sql.md carries the Auth-coupled and amended-RLS sections;
safety-domain-model §8 (MicroKit.Domain/Result kernel dep) and §10 (validation → 422) are present.
All already correct — no edits needed. One real gap closed (finding A): sql.md bullet 1 read as a
universal "tenant-isolation policy keyed on tenant_id", but profiles' policy is deliberately an
infra-role `USING (true)`, NOT tenant-keyed. Added a clause to the RLS section's bullet 3 so the
convention doc — not only the 0003 script comment — protects the rule; a reader following the
convention can no longer "correct" `USING (true)` into silent breakage. Build 0 warnings, 101 tests
green throughout. No git actions (human owns all git).

## Open debt / operational hazards (do not lose these)

- **NOT YET APPLIED TO SUPABASE.** The code is merged but the schema and hook do not exist on the
  real project. Nothing works until the human runs the migrator.
- **The Dashboard "Add user" button will stop working.** The trigger fires on EVERY insert into
  auth.users. Until the V1 constraint is revisited, these fail with P0002 (no server-set tenant_id):
  Dashboard "Add user", OAuth first login, magic-link/OTP signup of a new email, anonymous sign-in,
  naive bulk import. Existing sign-ins, password recovery and email change are UPDATEs → safe.
  **This is by design, not a fault** — but it is the single most likely thing to make the operator
  think the migration broke the project.
- **Backfill required.** The existing test user predates the trigger, so no signup event will fire
  and it has no profiles row. Without a backfill INSERT, it logs in without observer_name.
- **UNVERIFIED: can the postgres role create a trigger on `auth.users`?** Newer Supabase projects
  have tightened auth-schema privileges. This only surfaces when the migrator runs against real
  Supabase. If it fails, DbUp rolls the script back cleanly — but 0004's design must be revisited.
- **Rollback order matters**: disable the hook in the Supabase dashboard FIRST, then drop the
  trigger, then the functions. Dropping a still-registered function makes GoTrue call a missing
  function → every login fails harder, mid-incident.
- **Recovery channel**: the migrator connects as postgres over the session pooler, independent of
  GoTrue. Even with every project login broken, the database is reachable.
- The hook's "never raises" guarantee is honest but not absolute: `WHEN OTHERS` does not catch
  QUERY_CANCELED. Kept rare by keeping the lookup a single-row PK read. Deliberately NOT trapped
  (that would mask a genuine timeout).
- **No CI** (#38). 101 tests and nothing runs them automatically. Deferred until after Story 1
  branch 2, so the workflow is written once against a complete target.
- Carried from session 013: anti-drift test blind to HasMaxLength/varchar mismatch; forbidden-type
  architecture test is a name-prefix heuristic; SaasBtp.slnx header comment still says "Empty for now".

## Infrastructure done this session
- Doppler project `saas-btp` created (separate from `freelance` — never mix product and infra
  secrets). Secret `MIGRATOR_DB_CONNECTION` (Doppler forbids `__`, hence not `ConnectionStrings__…`;
  Program.cs already had the fallback).
- **Supabase connection: use the SESSION POOLER**, not `db.<ref>.supabase.co` — the direct host is
  IPv6-only and unreachable from WSL2. Host `aws-0-eu-central-1.pooler.supabase.com:5432`, username
  `postgres.<ref>`, Npgsql key-value format (NOT the `postgresql://` URI — Npgsql cannot parse it).
- **0001 has been applied to real Supabase**: `safety.constats` and the `schemaversions` journal now
  exist on the live database.
- Docker fix: disable "Use containerd for pulling and storing images" in Docker Desktop — the
  containerd snapshotter breaks large image pulls with `failed to copy: httpReadSeeker … EOF`.

## Environment/security note
The Supabase postgres password was exposed in shell history three times during this session (passed
as a CLI argument instead of using Doppler's interactive prompt). It was regenerated. **Always use
`doppler secrets set NAME` without `=value`** — the interactive prompt keeps the secret out of the
command line and out of `~/.zsh_history`.


## Amendment (2026-07-12, session 0006 / ADR-ARCH-007) — the "AFTER INSERT atomicity" premise was false in practice

Recorded here, not erased: the original decision above rejected async provisioning partly on the
premise that "an AFTER INSERT row trigger runs inside the inserting transaction: true transactional
atomicity." That premise was misleading, and it made 0004's trigger `on_auth_user_created` raise
`P0002` on EVERY signup, including the nominal Admin-API path.

The fact (verified in `supabase/auth` source `internal/api/admin.go`, in real Postgres logs, and
tracked by open upstream issue supabase/auth #1280): GoTrue does NOT set `app_metadata` at the INSERT.
It applies the caller's `app_metadata` (our `tenant_id`) by a MERGE UPDATE issued *after* the INSERT,
inside the same transaction. An `AFTER INSERT FOR EACH ROW` trigger fires before that UPDATE, so it
structurally cannot read the `tenant_id`. The transaction is atomic, but the atomicity covers a value
the trigger has not yet been handed — which is exactly what the original premise missed.

What still holds: the async alternatives remain rejected — but on the *corrected* basis (the 0005 JWT
hook READS `access.profiles`, so a profile that does not exist at first token issuance ships a token
without `observer_name`), not on the "true atomicity" wording.

The fix (script 0006, ADR-ARCH-007): split 0004's single trigger into
- `access.provision_profile` on INSERT AND `UPDATE OF raw_app_meta_data` (it sees the `tenant_id` when
  GoTrue's post-INSERT UPDATE writes it; a no-op until then), and
- `access.assert_profile_provisioned`, a `CONSTRAINT TRIGGER DEFERRABLE INITIALLY DEFERRED` that fires
  at COMMIT (after the UPDATE) and rejects a signup that produced no profile, depending only on
  `NEW.id` (immutable) so it can never read a not-yet-written value.

Consequence for the notes below: 0004's `on_auth_user_created` / `access.handle_new_user` are dropped
by 0006 and must not be recreated. The "Add user fails with P0002" hazard is unchanged in outcome, but
the rejection now happens at COMMIT (the deferred guard) rather than at the INSERT. The re-entry prompt
below is SUPERSEDED: applying #39 means applying 0002..0006 together, and the validation checklist has
been rewritten for the two-trigger design.

### Re-entry prompt for the web orchestration chat

"Reprise SaasBtp — suite session 014.

Contexte : lis 014-2026-07-12-access-profiles-observer-claims.md
(et 013 pour la fondation Story 1).

État :
- Story 1 fondation (#34) : mergée (PR #36). safety.constats appliqué sur le vrai Supabase.
- #39 (Access profiles + observer claims) : code mergé, MAIS PAS ENCORE APPLIQUÉ sur Supabase.
- #38 (CI) : créée, différée après Story 1 branche 2.
- Branche 2 de Story 1 (repository, CreateConstat, POST/GET, host wiring, ProblemDetails)
  est bloquée tant que #39 n'est pas appliqué et vérifié.

Prochaine étape immédiate : appliquer #39 contre Supabase.
- doppler run -- dotnet run --project tools/database-migrator/SaasBtp.Database.Migrator
- puis activer le hook dans le dashboard Supabase
- puis la checklist manuelle : UTILISATEUR JETABLE D'ABORD
- puis backfill du user existant (il précède le trigger, pas de profil)
- ATTENTION : le bouton "Add user" du dashboard ne marchera plus (P0002) — c'est voulu.
- Non vérifié : le rôle postgres peut-il créer un trigger sur auth.users ? Ça se saura au
  premier run. Si ça échoue, revoir le design de 0004.

Ensuite : branche 2 de Story 1, qui ferme #34.
Deux choses héritées à ne pas oublier dans le handler :
- convertir les timestamps avec .ToUniversalTime(), JAMAIS DateTime.SpecifyKind()
- passer occurredAt: / createdAt: en arguments nommés (transposables silencieusement)"

# Session — 2026-07-13 (apply #39, GoTrue app_metadata bug, identity model reversed)

Continues: 014-2026-07-12-access-profiles-observer-claims.md.

Purpose on entry: apply #39 (access.profiles + observer claims) against real Supabase, then
unblock Story 1 branch 2.

What actually happened: #39 was applied, it broke every user creation, the cause invalidated a
premise of session 014, the fix worked — and then the whole identity model was reversed. Story 1
branch 2 is deferred behind a new epic (#44).

---

## Part 1 — #39 applied, and it broke

### Pre-flight (the one thing that went right first)

Session 014 flagged an unverified risk: can `postgres` create a trigger on `auth.users`?
Verified BEFORE running the migrator, read-only:

```sql
select tableowner from pg_tables where schemaname='auth' and tablename='users';
-- supabase_auth_admin  -> postgres is NOT the owner
select has_table_privilege(current_user, 'auth.users', 'TRIGGER');
-- true
```

Postgres does not require ownership for CREATE TRIGGER, only the TRIGGER privilege. Risk cleared
empirically, not by hope. (The documented Supabase 42501 failure mode affects projects where that
privilege was never granted. Not this one.)

### Migrations applied

0002..0005 applied cleanly to the real project (ref boxxynsffybaemctpgdt). Journal verified. RLS,
policies, functions, trigger, hook: all six sanity checks green, including the critical
`profiles_auth_admin_select ... USING (true)` policy.

Hook enabled in the dashboard (Authentication -> Hooks -> Customize Access Token (JWT) Claims ->
Postgres -> `access.custom_access_token_hook`).

### Then every user creation failed

```
500 unexpected_failure — "Database error creating new user"
P0002: access.handle_new_user: tenant_id is missing or blank in raw_app_meta_data.
```

Not just the dashboard path. The nominal Admin API path too, with `app_metadata.tenant_id`
explicitly supplied.

---

## Part 2 — the diagnosis (ADR-ARCH-007)

**GoTrue writes caller-supplied `app_metadata` via an UPDATE issued AFTER the INSERT, inside the
same transaction.** An `AFTER INSERT` trigger on `auth.users` therefore cannot see it — ever.

Established three independent ways, in this order:

1. **The Postgres log.** The INSERT statement included `raw_app_meta_data` ($26) AND
   `raw_user_meta_data` ($27), yet the trigger raised P0002 (tenant missing) and NOT P0003
   (full_name missing). So `raw_user_meta_data` was populated at INSERT and `raw_app_meta_data`
   held only the provider blob.
2. **The existing user's row.** `raw_app_meta_data` = `{"provider":"email","providers":["email"],
   "tenant_id":"0000...0001"}`. The Admin API DOES write app_metadata. Combined with (1), the only
   possible mechanic is a post-INSERT write.
3. **The GoTrue source.** `supabase/auth`, `internal/api/admin.go`, `adminUserCreate`:
   `user.AppMetaData` is initialised to the provider blob alone; then inside one
   `db.Transaction(...)`, `tx.Create(user)` (the INSERT) runs, and later
   `user.UpdateAppMetaData(tx, params.AppMetaData)` (the UPDATE) runs with the SAME tx.
   Upstream issue supabase/auth#1280, still open.

**Session 014's premise was wrong.** It rejected async provisioning on the grounds that "an AFTER
INSERT row trigger runs inside the inserting transaction: true transactional atomicity." The
atomicity is real. It just covers data the trigger does not have yet.

Bonus facts from the source, both load-bearing:
- `UpdateAppMetaData` MERGES; the provider blob survives.
- The `if params.AppMetaData != nil` guard means dashboard "Add user", OAuth, magic-link and public
  signup emit NO `raw_app_meta_data` UPDATE at all.

### Competing diagnoses, both wrong

- **GPT** gave 80% to "the Admin API no longer writes app_metadata / expects a different field".
  Refuted by the existing user's own row.
- **Supabase's AI assistant** recommended `ALTER TABLE auth.users ADD COLUMN tenant_id`. Would have
  failed on 42501 (postgres is not the owner), and the `auth` schema is GoTrue-owned and rewritten
  on upstream migrations.

Both were plausible and both were wrong. What settled it was reading the log and the source.

---

## Part 3 — 0006, the fix that worked

Split the two concerns 0004 had wrongly fused:

- `access.provision_profile` — `AFTER INSERT OR UPDATE OF raw_app_meta_data`. No-ops at INSERT (no
  tenant yet), creates the profile on the UPDATE that carries it. Guards run existence-first, so
  later app_metadata writes (identity linking, admin edits) are no-ops. `ON CONFLICT DO NOTHING`.
- `access.assert_profile_provisioned` — `DEFERRABLE INITIALLY DEFERRED` constraint trigger. Fires at
  COMMIT, i.e. after the UPDATE. Raises P0002 if no profile row exists. Reads only `NEW.id`, which
  never changes, so it cannot consume stale data — the exact inversion of 0004's error.

Loud-failure-at-creation preserved: paths carrying no tenant_id still produce no profile and are
still rejected at COMMIT.

Adversarial review: no BLOCKER, no MAJOR. Six MINOR/NIT, all applied. The review independently read
0002 to confirm the two load-bearing assumptions (`forcerowsecurity = false`, `user_id` is PK).

Merged, applied, and `validate-39.sh` passed against the real project. The missing-tenant case
failed at COMMIT and not at INSERT — which IS the empirical proof that deferral holds (the runtime
dependency the review flagged as unenforceable from the repo).

**Also verified along the way:** `access.profiles` has `relrowsecurity = t, relforcerowsecurity = f`.
The `postgres` owner bypasses RLS, so SECURITY DEFINER functions owned by postgres read and write it
regardless of policy.

---

## Part 4 — and then the model was reversed

With 0006 green, the owner raised the real objection: **this is not the model he wants for V1.**

The `tenant_id`-in-`app_metadata` model has a ceiling. Session 012 had ALREADY logged this as debt,
twice:
- "the REAL mechanism (how a genuine user receives tenant_id) is a future Access story, not built"
- "the 'one user = one tenant' assumption has a ceiling; if multi-tenant-per-user becomes real,
  tenant becomes a switchable currentTenant + a user<->tenants membership"

**This epic pays that debt at the point where it became blocking. It is not scope creep.**

### Target model

- `access.profiles` — the human. 1:1 with `auth.users`. **No tenant, no role.** Cross-cutting.
- `access.tenants` + `access.memberships` — the only authorization bridge.
- `site.sites` + `site.site_memberships` — chantier granularity.
- `safety.constats` — shape unchanged. `tenant_id` stays a denormalized column; only its PROVENANCE
  changes (payload-validated instead of JWT claim). Observer identity stays frozen in the row.

The JWT becomes a proof of identity: `sub`, `email`, and the observer claims. No tenant, no role.
Consequence: revocation is immediate (`is_active = false`) instead of waiting for token expiry.

The action context (tenantId, siteId) travels in the command PAYLOAD, never in a header.

**This makes the GoTrue bug disappear rather than working around it.** A trigger that never reads
`raw_app_meta_data` cannot be broken by a post-INSERT write to it. 0006's machinery becomes dead code.

### What was rejected, and why

- **`tenant_id` in `user_metadata`** (the upstream issue's workaround, and Supabase's AI suggestion).
  `user_metadata` is client-writable via `supabase.auth.updateUser()`. Cross-tenant escalation in one
  line of JavaScript. Rejected outright.
- **`X-Organization-Id` header** (recommended by the external architecture doc). Contradicts
  ADR-ARCH-005. `uploadData()` replays a batch captured days ago, possibly across several chantiers,
  in ONE HTTP request. A header is a property of the request, not of the operation. The doc reasons
  about a classic web SaaS and ignores that the product is offline-first.
- **`first_name` / `last_name` as the source of `full_name`.** `full_name` is ENTERED, never computed.
  Concatenation encodes an assumption — one given name, one family name, in that order — false for
  many Ivorian names. OIDC treats `name`, `given_name`, `family_name` as three INDEPENDENT claims for
  exactly this reason. Settled: `full_name` NOT NULL and entered; `given_name` / `family_name`
  nullable and optional; `safety.constats.observer_full_name` stays a FROZEN string, never a live join.
- **Billing, Stripe, SIRET, plan_tier, org hierarchy, sub-contractor tiers.** Out of scope.

### Verified before committing to the model

- **PowerSync.** Parameter queries CAN query source tables, not just claims:
  `parameters: SELECT tenant_id FROM access.memberships WHERE user_id = request.user_id() AND
  is_active = true`. So the JWT can stay identity-only. `access.memberships` must be in the PowerSync
  publication. Constraints: equality lookups only (incl. IN); <= 1000 buckets/user. Both fine.
- **JWKS.** Nothing to migrate. `MicroKit.Auth.Supabase` already validates via JWKS/ES256 asymmetric
  (session 012, proven end-to-end). PowerSync requires asymmetric. Already aligned. A concern I raised
  and then retracted rather than leaving as a phantom task.

---

## Decisions

- Epic #44 created. ADR-ARCH-004 (membership ownership deferred) is SUPERSEDED. ADR-ARCH-005
  (payload not header) is CONFIRMED and reinforced. ADR-ARCH-007 (GoTrue post-INSERT UPDATE) is on
  `dev` and STAYS — the fact remains true and cost a full session to establish.
- Roles (tenant level): `owner`, `admin`, `member`. Site-level roles are #47's business, not this one.
- Tenant status: `active`, `suspended`. No `trial` / `past_due` — that is billing.
- **No trigger on `auth.users` at all** in the target model. `access.profiles` is written by
  `InviteMemberCommand` (#48). ADR-ARCH-005 applied to identity: the .NET domain is the sole write
  authority. A trigger cannot tell "create a company" from "join an existing one" without conditional
  PL/pgSQL driven by client-supplied flags — which is also how role escalation gets in.
- **The app is not deployed and `access.profiles` is empty.** Therefore the schema change is done in
  ONE migration, not staged. Staging protects nothing and creates an incoherent intermediate state.
- **`/me` and tenant-scoped paths WILL break** after the schema lands, because
  `AccessModuleExtensions` still resolves the tenant from `app_metadata.tenant_id`. Accepted. The
  .NET repoint is #49. No fallback is to be added.

---

## Learnings

- **The trigger's atomicity covered data it did not have.** Transactional atomicity is not content
  atomicity. Any trigger on `auth.users` reading `app_metadata` is structurally broken.
- **Three AIs, three wrong diagnoses.** GPT, Supabase's assistant, and Claude all proposed fixes that
  would have failed or made things worse. What settled it was the Postgres log and the GoTrue source.
  The process — refusing to build on the unverified — mattered more than any single model.
- **A correct fix for a problem that should not exist is still waste.** 0006 passed review with no
  blocker and worked in production. It is being deleted. The signal to change models was there before
  it was written; it was not acted on.
- **Verify before writing the brief, not after.** The first #45 implementer prompt described
  `access.profiles` from memory: invented an `email` column, missed `created_at NOT NULL` with no
  default, called `function` `job_function`, and asserted the 0005 hook "only reads full_name" when it
  reads and re-stamps `tenant_id`. Claude Code caught all of it by reading 0002 and 0005. The corrected
  prompt now opens with "read these files; if the brief contradicts them, the FILES win."
- **`function` is a SQL reserved word.** It is a column name in 0002. Unresolved — see below.
- **Supabase API keys:** new `sb_publishable_` / `sb_secret_` keys must be sent on the `apikey` header
  ONLY. Adding `Authorization: Bearer` makes the platform parse them as a JWT and reject with
  "Invalid JWT". Legacy keys require both. Legacy keys are removed end of 2026.
- **Doppler scoping** is per-directory and walks UP the tree. A scratch dir under an already-scoped
  parent inherits silently. Use explicit `-p` / `-c` flags rather than relying on cwd.
- **`HISTORY_IGNORE="(doppler secrets set*)"`** in `.zshrc` closes the secret-in-shell-history class
  of leak structurally, rather than relying on remembering to use interactive mode.

---

## Open / unresolved

- **`function` column (0002).** Reserved word. Rename to `job_function`, or is it already quoted?
  `sql.md` must rule. Claude Code was told to check and report, not decide.
- **What `DROP COLUMN tenant_id` takes with it** — indexes, constraints, RLS policies. Must be dropped
  explicitly and named. No blind CASCADE.
- **Script split.** Claude Code correctly caught a contradiction in the brief: anti-drift tests only
  apply the PORTABLE subset, so an auth-coupled script's `access.` DDL can never be anti-drift tested.
  Resolution: split into `0007_access_identity_model.sql` (PORTABLE — reshape profiles, create
  tenants + memberships; covered by anti-drift tests) and `0008_access_identity_hook_and_teardown.sql`
  (AUTH-COUPLED — drop 0006's triggers/functions, CREATE OR REPLACE the identity-only hook; validated
  by the manual checklist). This is better than the monolith: it puts the access schema under CI cover.
- **Ordering hazard to note in 0008's header:** 0007 drops `tenant_id` while the live 0005 hook still
  reads it. Between the two scripts the hook would fall into its `EXCEPTION WHEN others` branch. No
  users, no logins in that window — acceptable, but it must be documented, not discovered.
- **PowerSync publication** must include `access.memberships`. Not done.
- **Public signup** should be disabled in the Supabase dashboard: once the DB stops enforcing
  invite-only and before #48 lands, nothing guards account creation.
- **MicroKit.Tenancy extension point.** `AddMicroKitAuthMultitenancy` registers
  `AuthTenantResolutionStrategy`, which resolves from CLAIMS. The new model resolves from the PAYLOAD,
  validated against memberships. Is there a clean extension point, or is this a MicroKit follow-up?
  To instruct at #49, not before.
- **RLS `SET LOCAL app.current_tenant_id`** must be spiked against the session pooler in transaction
  mode and against PowerSync CDC before any code (#50).
- Carried from before: ProblemDetails / RFC 9457 (403s have empty bodies); Microsoft.OpenApi 2.10.0
  pin; offline conflict-resolution ADR.

---

## Where things stand

- **#39** — merged and applied. Superseded in substance by #44.
- **0006** — merged, applied, validated. Will be torn down by #45.
- **Epic #44** — created. Milestone 2 ("Increment 0 - Identity, tenancy & request context").
  - **#45** — access schema (absorbed #46: teardown, tenants, memberships, profiles reshaped). IN PROGRESS.
  - #46 — closed as merged into #45.
  - #47 — site schema (sites, site_memberships, real ISiteScopeProvider). Closes #20's deferral.
  - #48 — onboarding CQRS (RegisterTenant, InviteMember, GET /me/workspaces).
  - #49 — request context (payload-carried tenant/site, validated against memberships; ProblemDetails).
  - #50 — RLS (spike first).
  - #51 — Story 1 branch 2, closes #34.
- **#34 / Story 1 branch 2** — deferred behind #49. Several weeks. Accepted: the tenant/membership
  model cannot be retrofitted into immutable evidence records.
- **#38 (CI)** — still deferred.

Current step: Claude Code was asked to PRODUCE A PLAN for #45 (not to implement). The plan is to be
reviewed at the start of the next session.

Branch: `feat/access/tenants-memberships-schema` (created from `dev`, nothing committed).

---

## Carried decisions for #51 (do not lose these)

- POST idempotent on the id alone (replay -> 200; content collision undetected and documented).
- `.ToUniversalTime()`, **never** `DateTime.SpecifyKind()`. SpecifyKind overwrites the Kind without
  converting: 08:00+02:00 stored as 08:00Z is a silent 2-hour corruption of an evidentiary timestamp.
- `occurredAt:` / `createdAt:` as NAMED arguments. Two transposable DateTime parameters; the drift
  guard would still pass if they were swapped.
- GET scoped to tenant. Domain validation failures return 422, not 400.

---

## Re-entry prompt for the web orchestration chat

"Reprise SaaS BTP. Mes regles habituelles s'appliquent (franc, focus roadmap, doc officielle avant
chaque commande, confirmation avant action destructive, gh CLI depuis WSL2, Claude Code ne fait
jamais de git, prompts Claude Code TOUJOURS en anglais, plan valide avant toute implementation).

Lis dans les ressources du projet :
- 015-2026-07-13-gotrue-app-metadata-and-identity-model-reversal.md (derniere session)
- ADR-ARCH-005 (payload pas header), ADR-ARCH-006 (DbUp), ADR-ARCH-007 (GoTrue post-INSERT UPDATE)
- sql.md, naming.md, safety-domain-model.md
- 012 (cablage Supabase, auth JWKS/ES256) pour le contexte .NET existant

ETAT
- #39 applique. 0006 (triggers de provisionnement) applique et valide sur le vrai Supabase.
- MAIS le modele d'identite a ete renverse : epic #44. tenant_id quitte le JWT et quitte
  access.profiles, au profit de access.tenants + access.memberships. Le contexte d'action
  (tenantId, siteId) voyage dans le PAYLOAD des commandes, jamais dans un header.
- 0006 va etre demonte par #45. Ce n'est pas un echec : il a etabli ADR-ARCH-007.
- L'app n'est PAS deployee, access.profiles est VIDE, un seul user jetable dans auth.users.
  Donc : une seule migration, pas d'etapes intermediaires a proteger.

ETAPE EN COURS — #45 (schema access)
Claude Code a produit un PLAN (pas du code). Je te le colle. Analyse-le avant tout go.

Le plan doit couvrir un SPLIT en deux scripts :
- 0007_access_identity_model.sql (PORTABLE) : reshape access.profiles (DROP tenant_id, ADD email,
  ADD given_name/family_name nullables, created_at DEFAULT), CREATE access.tenants,
  CREATE access.memberships. Couvert par les tests anti-derive.
- 0008_access_identity_hook_and_teardown.sql (AUTH-COUPLED) : DROP des triggers/fonctions de 0006,
  CREATE OR REPLACE du hook 0005 pour qu'il cesse de lire/ecrire tenant_id (il ne garde que
  observer_name / observer_function). Valide par la checklist manuelle.

DECISIONS DEJA TRANCHEES, ne pas rouvrir
- roles tenant : owner, admin, member (snake_case). Les roles chantier sont l'affaire de #47.
- statut tenant : active, suspended. Pas de trial/past_due (billing hors scope).
- AUCUN trigger sur auth.users dans le modele cible. access.profiles est ecrite par
  InviteMemberCommand (#48).
- full_name NOT NULL et SAISI, jamais calcule. given_name/family_name nullables.
  observer_full_name reste une chaine GELEE sur le constat.
- /me VA CASSER (AccessModuleExtensions resout encore le tenant depuis les claims). C'est voulu,
  c'est #49. Ne pas ajouter de repli.

PIEGES CONNUS
- La colonne `function` de 0002 est un MOT RESERVE SQL. Trancher via sql.md.
- DROP COLUMN tenant_id peut emporter index/contraintes/policies. Les dropper explicitement,
  jamais de CASCADE aveugle.
- Ordre 0007 -> 0008 : le hook 0005 lit tenant_id et la colonne disparait en 0007. Fenetre sans
  utilisateur, acceptable, mais A DOCUMENTER dans l'en-tete de 0008.
- Ne PAS decrire access.profiles de memoire. Lire 0002 et 0005 d'abord. C'est l'erreur qui a fait
  echouer le premier prompt implementeur.

ENSUITE : #47 (schema site), #48 (onboarding CQRS), #49 (contexte de requete + repoint .NET,
repare /me), #50 (spike RLS), #51 (Story 1 branche 2, ferme #34)."

---

## Fichiers a lire en entree de la prochaine session

Essentiels :
- `015-2026-07-13-gotrue-app-metadata-and-identity-model-reversal.md` (ce fichier)
- `ADR-ARCH-005` (write path : payload, pas header)
- `ADR-ARCH-006` (DbUp seul proprietaire du DDL)
- `ADR-ARCH-007` (GoTrue ecrit app_metadata post-INSERT)
- `sql.md` (conventions ; trancher le cas `function`)
- `naming.md` (UUIDv7, value objects)

Utiles selon la direction :
- `012-2026-07-10-supabase-wiring-story-a-verified.md` (auth JWKS/ES256, ConfigurationTenantStore,
  la dette "un user = un tenant" deja identifiee)
- `014-2026-07-12-access-profiles-observer-claims.md` (design de #39, dont la premisse fausse)
- `safety-domain-model.md` (ObserverSnapshot fige, 422 pas 400)
- `decisions-index.md`
- `profiles-jwt-validation-checklist.md` (a reecrire dans #45)

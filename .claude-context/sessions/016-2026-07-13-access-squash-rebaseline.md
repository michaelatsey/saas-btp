# Session — 2026-07-13 (#45: access schema squash + rebaseline, validated end-to-end)

Continues: 015-2026-07-13-gotrue-app-metadata-and-identity-model-reversal.md.

Purpose on entry: review Claude Code's PLAN for #45, then implement.

What actually happened: the plan was reviewed and corrected, implemented — and then the whole
APPROACH was replaced. Not the model (settled in 015), the migration strategy: forward-migrating
a schema with no data in it was documentary debt. #45 became a SQUASH. It shipped, and it is
validated against the real Supabase.

---

## Part 1 — the plan review

Claude Code's plan was strong. It had actually read 0002/0005/0006 (the failure mode of the
previous prompt), and it produced the single most useful fact of the review:

**`DROP COLUMN tenant_id` takes NOTHING with it.** No index, no constraint, no policy depends on
it — only `pk_profiles` on `user_id`, and 0003's policy is `USING (true)`. No CASCADE needed.
Verified by reading, not assumed.

It also correctly caught a contradiction in the brief and resolved it: anti-drift tests only apply
the PORTABLE subset, so auth-coupled DDL can never be covered. Hence the split.

### What the review found

**3 BLOCKER:**
- Session-file numbering collision (it wanted to write `015`, which already existed) — and more
  importantly, **session traces are not Claude Code's deliverable.** It has no access to the web
  conversation. Removed from scope.
- ADR path invented. It wrote `decisions/architecture/`. STEP 0 later established the truth:
  **`.claude-context/context/decisions/architecture/`**. Both my guess and the owner's earlier
  guess were wrong. This is why STEP 0 exists.
- ".NET impact" asserted, not established. It claimed a green suite without grepping for a single
  C# reference to `profiles`.

**2 MAJOR:**
- **`created_at DEFAULT now()` on profiles.** The owner's own brief had mandated it. It was wrong,
  and he retracted it: the default was a leftover from the trigger world. In the target model
  `InviteMemberCommand` is the only writer, so the domain stamps it, exactly as `safety.constats`
  does. A DB default would MASK a domain that forgot to stamp instead of failing loudly.
  Settled: **no DEFAULT on any of the three tables.**
- **`email` had no owner and no constraint.** Two holes: nothing resynchronises it with
  `auth.users` (it CAN drift), and without a UNIQUE two rows could claim the same e-mail identity.

### The email rationale — I got it wrong first, and it matters

I initially justified `ux_profiles__email` as protecting "the projection from silently diverging
from its source." **That is false.** GoTrue can change the email in `auth.users`; `profiles.email`
stays frozen; the UNIQUE says nothing about it. I had welded two incompatible ideas together.

What the constraint actually does: it prevents **two profile rows claiming the same e-mail
identity**. A LOCAL invariant. Nothing more.

Which forced the real question: **who reads `profiles.email` at all?** Not the hook (the JWT
carries `email` natively). Not `/me`. The answer, confirmed by the owner: **the tenant member-list
screen** — "who is in my company" — joining `memberships -> profiles`. Without the column, listing
a tenant's members would need one GoTrue Admin API call per member. That is the entire
justification, and it is now written as such in the script header and in ADR-ARCH-008.

Had the answer been "that screen doesn't exist", the column would have been dropped.

---

## Part 2 — the approach was replaced

After the corrected prompt was written and the implementer had already produced 0007/0008, the
owner asked the question I should have asked first:

> The tables are empty. Why are we writing ALTER and DROP IF EXISTS as if this were production?

He was right, and I had not challenged my own premise. I had built 0007/0008 on an unexamined
assumption — *an applied migration is immutable*. **That rule protects DATA.** There was none:
`access.profiles` empty, app not deployed, one disposable user in `auth.users`.

The cost of forward-migrating: ~80 lines of ALTER/DROP that would, forever, narrate a history
nobody lived. A future reader would have to replay five scripts to know what `access.profiles`
looks like. **Permanent documentary debt, paid to honour a discipline that protects nothing here.**

**The load-bearing fact I had to state:** DbUp journals by SCRIPT NAME. Rewriting 0002 in place
does NOTHING to the live database — 0002 is already journaled and will never replay. So the squash
is not a file operation. It is a **destructive reset of the live database**. That is the real
decision, and it had to be named as such before it could be taken.

### The renumbering trap — and where I failed, then recovered

The owner then wanted to go further: renumber so `access` comes before `safety` (conceptual reading
order). I initially went along with it. **That was wrong, and he caught me by restating his own
goal: "act as an expert to avoid debt."**

Renumbering buys nothing technical (no cross-context FK exists; `safety.constats.tenant_id` is a
denormalized column with no constraint). And it does not hold: **the next context is `site` (#47),
which lands as `0004` — AFTER `safety`.** The conceptual order would be broken immediately.
Maintaining it would mean renumbering — i.e. wiping the database — at every new context.
**That is the definition of debt: a rule you must keep repaying.**

A DbUp numeric prefix is not a table of contents. It is an execution order. Model legibility is the
job of the ADR and the index, not of a filename.

Settled: **targeted reset (access only).** `safety` untouched, `0001` untouched byte for byte.

---

## Part 3 — the squash

```
0001_safety_create_constats.sql    UNCHANGED
0002_access_identity_model.sql     PORTABLE      profiles + tenants + memberships, final shape
0003_access_identity_auth.sql      AUTH-COUPLED  grants + policy + identity-only JWT hook
```

Deleted: 0002..0006 (the old access set), plus the 0007/0008 that had just been written.

No `ALTER`. No `DROP IF EXISTS`. No trigger, anywhere. Each script declares the shape it creates.

Side benefit, not trivial: the classification is now clean — **the whole `access` schema is
portable**, therefore entirely covered by anti-drift tests. It used to be a patchwork.

### STEP 0 — the four verifications that paid

1. **`IsPortableScript(name) => !AuthCoupledScriptMarkers.Any(name.Contains)`** — a pure substring
   match against an explicit list. No ordering, no contiguity, no numeric-range assumption. The
   renumbering question was mechanically safe (it was rejected on other grounds).
2. **DbUp journal: table `schemaversions`, column `scriptname`** — DbUp's PostgreSQL default,
   not overridden. This is what made step 4 of the reset executable rather than guesswork.
3. **The verbatim pieces carried out of the dying 0003/0005** before deleting them: the exact
   GRANTs, the exact `USING (true)` policy, and the hook's exact attributes
   (`plpgsql · STABLE · SECURITY INVOKER · SET search_path = ''`).
4. **The claim contract does not leak.** The DB column is `job_function`; the CLAIM stays
   `observer_function` (named for its consumer, Safety, not for Access's columns).

### The ADR immutability call — Claude Code was right to refuse

The prompt said "no dangling reference to a deleted script may remain anywhere." Claude Code
refused to scrub ADR-ARCH-007, which cites the now-deleted 0004/0006, on the grounds that
`decisions-index.md` declares ADRs **immutable once accepted, superseded (never edited)**.

**Correct arbitration, and my instruction was too broad.** An ADR is a dated historical document.
Those scripts existed. Citing them is not a dangling reference — it is exact history. Scrubbing it
would be rewriting the past to fit the present, which is precisely what the immutability rule
forbids. The "no dangling reference" rule targets LIVE artifacts (MigrationRunner, tests, `sql.md`,
the checklist) — those LIE if they cite a dead script. ADRs and session traces do not: describing a
superseded state is their function.

---

## Part 4 — validation, and the three false leads

CI: **118/118 green**, Docker available, anti-drift tests ran against a real migrated Postgres.

The manual checklist against the real Supabase took three runs. All three failures were in the
VALIDATION SCRIPT, not in the model — which is its own lesson.

### False lead 1 — `pass()` polluting stdout

`create_and_capture()` printed `PASS ...` to stdout AND returned the UUID on stdout. Captured in
`$( )`, the variable became `"  PASS  ...\n<uuid>"`. The generated SQL was garbage. Fix: diagnostics
to **stderr**.

### False lead 2 — a guard that ASKED instead of CHECKING

The script printed an `INSERT` to paste into the Supabase SQL editor, then asked "type OUI to
continue." That is not a guard, it is a question. The table stayed empty, CASES A/B went red, and
we spent real time suspecting the hook — which was working perfectly.

**I had also justified the copy-paste on a false premise:** "the `access` schema is not exposed to
the Data API, so the script cannot insert." It doesn't need PostgREST. It needs **Postgres** —
the same owner connection the migrator uses.

PHASE 2 was rewritten as a **deterministic fixture loader**: it PROVES the state before, writes,
and PROVES the state after. And the "before" assertion is the best test in the whole suite:

> **After `createUser`, `access.profiles` must contain ZERO rows.**

This tests the **absence of a forbidden mechanism**, not just an outcome. A provisioning trigger
reintroduced someday would make CASES A/B pass for the WRONG reason — the manual insert would
conflict or duplicate, and nobody would notice. Now it fails loudly. (This one came from a GPT
cross-review, and it was the right call.)

### False lead 3 — Npgsql vs libpq

`MIGRATOR_DB_CONNECTION` is in Npgsql key-value form (what .NET consumes). `psql` speaks libpq:
lowercase keywords, space-separated, and `sslmode` values in lowercase (`Require` is rejected,
`require` is not). The secret was NOT changed — the script converts. Keys with no libpq equivalent
(`Trust Server Certificate`) are dropped; with `sslmode=require`, libpq encrypts without validating
the certificate anyway, so behaviour is preserved.

### The results

- **Schema:** 7 columns, `ux_profiles__email`, the policy, RLS on all three tables,
  `ix_memberships__tenant_id` absent, **zero triggers on `auth.users`**, hook `prosecdef = false`.
- **Grants (verified SEPARATELY from the policy — they fail differently):** `supabase_auth_admin`
  has USAGE + SELECT on `profiles` ONLY, EXECUTE on the hook ONLY (`t, f, f`), and **no** SELECT on
  `tenants`/`memberships`. `anon` / `authenticated` hold **zero** privilege on `access.*`.
- **§4** — full profile -> `observer_name` + `observer_function` top-level, **no tenant claim at any
  depth** (checked recursively over every JSON path, not just `app_metadata.tenant_id`).
- **§5** — no `job_function` -> the `observer_function` **key is ABSENT**, not `null`.
- **§6** — no profile -> `createUser` succeeds, sign-in succeeds, no `observer_*`, and **no
  `access.custom_access_token_hook failed` line in the Postgres logs.**

Final observed state: **3 users in `auth.users`, 2 rows in `access.profiles`.** The asymmetry IS
the result. Authentication no longer implies onboarding.

---

## Decisions

- **ADR-ARCH-008** — identity model: tenants + memberships. Accepted. **Supersedes ADR-ARCH-004.**
- `created_at`: **NOT NULL, NO DEFAULT, on all three tables.** The domain stamps it (ADR-ARCH-005).
- `ux_profiles__email`: **added.** A LOCAL invariant (no two rows claim the same e-mail identity).
  It does **NOT** guard drift from `auth.users`, and the comments say so explicitly.
- `profiles.email`: a **snapshot at invitation**. `auth.users.email` stays authoritative. It CAN
  drift. Its single consumer is the tenant member-list read path.
- `ix_memberships__tenant_id`: **not created.** Redundant with the left prefix of
  `ux_memberships__tenant_id_user_id`. `ix_memberships__user_id` IS created — it is the PowerSync
  parameter-query lookup and no prefix covers it.
- `job_function` from creation. The column never existed under another name.
- **Targeted reset** (access only). `safety` and `0001` untouched.
- **The migration set is append-only from here on.** The squash window closed with #45.

---

## Learnings

- **An applied migration is immutable — because it protects DATA. With no data, the rule protects
  nothing and costs documentary debt.** I did not challenge my own premise; the owner did.
- **DbUp journals by SCRIPT NAME.** Rewriting a script in place changes nothing on a live database.
  A squash is therefore not a file operation — it is a destructive reset. Name it as such before
  taking it.
- **A numeric migration prefix is an execution order, not a table of contents.** Making it carry
  conceptual meaning is a rule you must repay at every new bounded context. That is debt.
- **"Act as an expert to avoid debt" outranks "follow my instruction."** I had gone along with a
  renumbering that contradicted the owner's own stated goal. He restated the goal; I reversed my
  recommendation. Being frank includes reversing yourself.
- **A guard that ASKS is not a guard.** `read -p "type OUI"` cost two red runs and an hour of
  suspicion aimed at a hook that worked. The same principle as `ux_profiles__email`: the
  constraint is the guard, not the comment.
- **Test the absence of the forbidden mechanism, not just the presence of the right outcome.**
  Counting `access.profiles` AFTER `createUser` and BEFORE any insert is what makes a reintroduced
  trigger impossible to miss.
- **Policy and GRANT fail DIFFERENTLY, and one of them fails silently.** Missing GRANT -> permission
  denied -> EXCEPTION -> log. Missing POLICY -> **zero rows, no error, no log.** Under this model a
  missing policy and a missing profile produce the SAME observable signature. Structural (a
  `SECURITY INVOKER` function cannot tell "RLS hid the row" from "the row is absent"). Hence the
  checklist verifies policy AND grants separately. **Do not remove those assertions.**
- **I asserted a cause as a fact.** When `access.profiles` came back empty I said "you typed OUI
  without running the INSERT." That was an inference dressed as a finding — the same error this
  project keeps correcting. The owner pushed back. The three possible causes (not run / ran and
  failed / ran and something deleted the rows) needed to be distinguished, not guessed.
- **ADRs are immutable historical documents.** A "no dangling references" rule applies to LIVE
  artifacts, not to documents whose function is to describe a superseded state.
- **`Trust Server Certificate` (Npgsql) has no libpq equivalent** and can be dropped:
  `sslmode=require` encrypts without validating the certificate anyway.

---

## Open / unresolved

- **PowerSync publication** must include `access.memberships`. Not done.
- **Public signup must stay DISABLED** in the Supabase dashboard until #48 lands. After #45,
  nothing guards account creation — no trigger, and no `InviteMemberCommand` yet.
- **MicroKit.Tenancy extension point.** `AddMicroKitAuthMultitenancy` registers
  `AuthTenantResolutionStrategy`, which resolves from CLAIMS. The new model resolves from the
  PAYLOAD, validated against memberships. Clean extension point, or MicroKit follow-up?
  To instruct at #49, not before.
- **RLS `SET LOCAL app.current_tenant_id`** must be spiked against the session pooler in
  transaction mode AND against PowerSync CDC before any code (#50).
- **ProblemDetails / RFC 9457** — 403s still have empty bodies. Now due at #49, which needs it.
- Carried: Microsoft.OpenApi 2.10.0 pin; offline conflict-resolution ADR; `core.hooksPath`.

---

## Where things stand

- **#45** — merged. `access` rebaselined. **KNOWN BROKEN by design:** `GET /me` and every
  tenant-scoped path fail at runtime (`AccessModuleExtensions` still resolves the tenant from a
  claim that no longer exists). That is #49. **No fallback is to be added. Not a regression.**
- **#47** — site schema (sites, site_memberships, real ISiteScopeProvider). **NEXT.**
- **#48** — onboarding CQRS: RegisterTenant, InviteMemberCommand, GET /me/workspaces.
- **#49** — request context + .NET repoint. **Repairs `/me`.**
- **#50** — RLS (spike first).
- **#51** — Story 1 branch 2, closes **#34**.
- **#38 (CI)** — still deferred.

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

```
Reprise SaaS BTP. Mes regles habituelles s'appliquent (franc, focus roadmap, doc officielle avant
chaque commande, confirmation avant action destructive, gh CLI depuis WSL2, Claude Code ne fait
jamais de git, prompts Claude Code TOUJOURS en anglais, plan valide avant toute implementation).

Lis dans les ressources du projet :
- 016-2026-07-13-access-squash-rebaseline.md (derniere session)
- ADR-ARCH-008 (modele d'identite : tenants + memberships, supersede ADR-ARCH-004)
- ADR-ARCH-005 (payload pas header), ADR-ARCH-006 (DbUp seul proprietaire du DDL)
- sql.md, naming.md, safety-domain-model.md
- build-checklist.md (position courante)

ETAT
- #45 merge. Le schema access est REBASELINE par un SQUASH : les scripts 0002..0006 sont
  supprimes, remplaces par 0002_access_identity_model.sql (PORTABLE) et
  0003_access_identity_auth.sql (AUTH-COUPLED). Aucun ALTER, aucun DROP IF EXISTS, AUCUN
  trigger sur auth.users. 0001_safety_create_constats est intact, a l'octet pres.
- La fenetre du squash est FERMEE. Les migrations sont append-only a partir de maintenant.
- Modele : access.profiles = l'humain (1:1 avec auth.users, ni tenant ni role).
  access.tenants + access.memberships = le SEUL pont d'autorisation. Le JWT ne porte que
  l'identite (sub, email, observer_name, observer_function). Aucun claim tenant, jamais.
  Le contexte d'action (tenantId, siteId) voyage dans le PAYLOAD des commandes.
- Rupture assumee : "1 auth user = 1 profil" ne tient plus. Un user peut exister dans
  auth.users, se connecter, avoir un token VALIDE, et n'avoir AUCUNE identite metier.
  Authentification, identite metier et autorisation sont trois choses distinctes.
- Valide de bout en bout : 118/118 en CI + checklist manuelle tout vert sur le vrai Supabase
  (schema, grants separes de la policy, les 3 cas JWT, zero ligne d'erreur dans les logs).

CASSE VOLONTAIREMENT — NE PAS "REPARER"
- GET /me et tout chemin scope tenant echouent au runtime : AccessModuleExtensions resout
  encore le tenant depuis un claim qui n'existe plus. C'est #49. AUCUN repli ne doit etre
  ajoute. Ce n'est pas une regression.

A NE PAS PERDRE
- Public signup DOIT rester desactive dans le dashboard Supabase jusqu'a #48 : plus aucun
  trigger ne garde la creation de compte, et InviteMemberCommand n'existe pas encore.
- access.profiles est ecrite EXCLUSIVEMENT par InviteMemberCommand (#48).
- La policy profiles_auth_admin_select ... USING (true) est porteuse : sans elle le hook lit
  ZERO ligne EN SILENCE, sans erreur. Une policy absente et un profil absent produisent la
  MEME signature observable. C'est pourquoi la checklist verifie policy ET grants separement.
- PowerSync : access.memberships doit entrer dans la publication. Pas fait.

ETAPE EN COURS — #47 (schema site)
sites + site_memberships + le vrai ISiteScopeProvider. Ferme le report de #20.
Point d'attention : les roles CHANTIER se decident ici (les roles TENANT — owner/admin/member —
sont deja tranches et ne se rouvrent pas). siteId voyage dans le payload, jamais dans un header
(ADR-ARCH-005, contexte de rejeu offline par lots).

ENSUITE : #48 (onboarding CQRS, InviteMemberCommand) -> #49 (contexte de requete + repoint .NET,
repare /me, + ProblemDetails RFC 9457) -> #50 (spike RLS) -> #51 (Story 1 branche 2, ferme #34).
```

---

## Fichiers a lire en entree de la prochaine session

Essentiels :
- `016-2026-07-13-access-squash-rebaseline.md` (ce fichier)
- `ADR-ARCH-008` (modele d'identite : tenants + memberships)
- `ADR-ARCH-005` (write path : payload, pas header)
- `ADR-ARCH-006` (DbUp seul proprietaire du DDL)
- `sql.md`, `naming.md`
- `build-checklist.md`

Utiles selon la direction :
- `015-...-gotrue-app-metadata-and-identity-model-reversal.md` (pourquoi le modele a bascule ;
  ADR-ARCH-007 y est etabli)
- `012-...-supabase-wiring-story-a-verified.md` (auth JWKS/ES256, le .NET existant)
- `safety-domain-model.md` (ObserverSnapshot gele, 422 pas 400)
- `profiles-jwt-validation-checklist.md` (la procedure, et le run consigne du 2026-07-13)
- `decisions-index.md`

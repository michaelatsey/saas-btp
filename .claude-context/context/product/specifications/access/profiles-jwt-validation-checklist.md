# Manual validation checklist — access identity model (0002 + 0003), squash rebaseline

Why this exists: `0002_access_identity_model` is PORTABLE and is anti-drift tested in CI (it names no
GoTrue object). `0003_access_identity_auth` is AUTH-COUPLED — it names the `supabase_auth_admin` role
(schema/table grant, the `profiles_auth_admin_select` policy, and the `access.custom_access_token_hook`
JWT hook) — and CANNOT be exercised in CI. It is validated HERE, by hand, against the real Supabase
project.

There is ONE Supabase project. There is no staging. Every step runs against that single project.

What this is: a SQUASH / rebaseline (ADR-ARCH-008). The earlier Access scripts (0002-0006) are deleted
and replaced by 0002 (tables) + 0003 (auth). It is safe only because `access.profiles` is empty, the
app is not deployed, and the one `auth.users` account is disposable — there is no data to protect. The
append-only rule protects DATA; there is none. `0001_safety_create_constats` is NOT touched.

The model after this (read ADR-ARCH-008 first):
- `access.profiles` is the 1:1 human projection of `auth.users` — no tenant, no role. Columns:
  user_id, email, full_name, given_name, family_name, job_function, created_at.
- `access.tenants` + `access.memberships` are the authorization bridge. Nothing reads them yet
  (consumers are #48/#49/#50).
- The JWT hook is IDENTITY-ONLY: stamps `observer_name` (+ `observer_function` when set), NO tenant
  claim, ever. No profile -> returns the token unchanged, silently.
- NO trigger on `auth.users`. Signup no longer provisions or guards a profile.

DbUp journal table (from the migrator config — it is NOT overridden, so DbUp's PostgreSQL default):
`schemaversions`, with the script name in column `scriptname`. The reset below deletes rows from it.

Legend: `[ ]` to do, `[x]` done. Fill the `<...>` placeholders before running anything.

---

## 0. EXPECTED BEHAVIOR — authentication no longer implies onboarding (read before running)

Under the old model a signup without a server-set `tenant_id` was REJECTED (P0002 at commit). That
guard is GONE. New expected behavior:

- Creating a user with NO `app_metadata` now SUCCEEDS and creates NO `access.profiles` row. A user can
  authenticate and hold a valid token while having no business identity (no profile, no tenant, no
  membership). By design (ADR-ARCH-008), not a broken migration.
- Such a user's JWT carries NO `observer_*` claims (the hook finds no profile and returns the event
  unchanged, silently — no Postgres log line; this is not an incident) and NEVER a tenant claim.
- Because nothing now stands between an anonymous visitor and an `auth.users` row, PUBLIC SIGNUP MUST
  be disabled until `InviteMemberCommand` (#48) exists (step 1).

Out of scope for this checklist (do NOT test them as regressions): `GET /me` and tenant-scoped paths
WILL break at runtime — the JWT no longer carries `tenant_id`, and .NET still reads it. That repoint is
#49. Their breakage here is EXPECTED and accepted.

---

## 1. Disable PUBLIC SIGNUP (permanent, until #48 lands — NOT just for the reset window)

Supabase dashboard: Authentication -> Sign In / Providers -> turn OFF "Allow new users to sign up"
(and any self-service email/OAuth signup). After 0003 there is no trigger guarding account creation and
`InviteMemberCommand` does not exist yet, so this is the only thing preventing an anonymous
`auth.users` row.

- [ ] Public signup is disabled. (Leave it disabled; re-enabling is a #48 decision.)

## 2. RESET SEQUENCE (squash rebaseline)

FREEZE for the whole run: NO user may be created by ANY path — not public signup, not the Admin API,
not an invite, not the dashboard "Add user", not a script — from here until step 2.6 completes.

The human runs the migrator (never Claude, never automatically).

1. Dashboard: Authentication -> Hooks -> DISABLE the Custom Access Token hook. Do this BEFORE the hook
   function is dropped in step 2.3 — dropping a still-registered function makes GoTrue call a missing
   function and fails every login harder.
2. Dashboard: delete the disposable user in `auth.users` (Authentication -> Users).
3. SQL (as owner): drop the whole Access schema, then confirm no trigger is left on `auth.users`.
   ```sql
   DROP SCHEMA access CASCADE;

   select tgname from pg_trigger
   where tgrelid = 'auth.users'::regclass and not tgisinternal;   -- must return ZERO rows
   ```
4. DbUp journal: delete ONLY the rows for the squashed scripts. `0001` stays applied;
   `safety.constats` is not touched.
   ```sql
   delete from schemaversions
   where scriptname like '%0002_access_create_profiles%'
      or scriptname like '%0003_access_profiles_permissions%'
      or scriptname like '%0004_access_profiles_trigger%'
      or scriptname like '%0005_access_profiles_jwt_hook%'
      or scriptname like '%0006_access_profiles_provisioning_fix%';
   -- Verify 0001 is still present:
   select scriptname from schemaversions order by scriptname;
   ```
5. Rerun the migrator. It applies the two new pending scripts (0002 then 0003) in journaled
   order. Secrets come from Doppler — NEVER pass a connection string as a CLI argument or an
   inline env var: it lands in the shell history in cleartext.
   Note: Doppler scope walks UP the directory tree silently. Use explicit -p/-c if unsure.
  ```
   doppler run -- dotnet run --project tools/database-migrator/SaasBtp.Database.Migrator -c Release
   ```
6. Dashboard: Authentication -> Hooks -> RE-ENABLE the Custom Access Token hook, pointing at
   `access.custom_access_token_hook`.

- [ ] Steps 2.1-2.6 done in order; migrator run reports success.
- [ ] `schemaversions` now lists `0001_safety_create_constats`, `0002_access_identity_model`,
      `0003_access_identity_auth`.

If any step fails: fix the cause and REPEAT FROM STEP 2.3. There is no data, so the reset is
idempotent and re-running it costs nothing.

## 3. Sanity (psql, as owner)

```sql
-- profiles final shape: no tenant_id, no function; email + given/family + job_function present.
select column_name, data_type, character_maximum_length, is_nullable
from information_schema.columns
where table_schema = 'access' and table_name = 'profiles'
order by column_name;
-- expected: created_at, email, family_name, full_name, given_name, job_function, user_id

-- unique email; the auth-admin SELECT policy exists (its absence is the silent-truncation bug).
select conname from pg_constraint where conrelid='access.profiles'::regclass and contype='u';  -- ux_profiles__email
select polname, polcmd from pg_policy where polrelid='access.profiles'::regclass;               -- profiles_auth_admin_select, r

-- bridge tables exist, RLS enabled; memberships has the FKs + composite unique + user_id index,
-- and NOT a standalone tenant_id index.
select relname, relrowsecurity from pg_class
where oid in ('access.tenants'::regclass, 'access.memberships'::regclass);   -- both t
select conname, contype from pg_constraint
where conrelid='access.memberships'::regclass order by contype, conname;
-- ck_memberships__role (c), fk_memberships__profiles (f), fk_memberships__tenants (f),
-- pk_memberships (p), ux_memberships__tenant_id_user_id (u)
select indexname from pg_indexes where schemaname='access' and tablename='memberships';
-- expected: ix_memberships__user_id, pk_memberships, ux_memberships__tenant_id_user_id
-- NOT expected: ix_memberships__tenant_id

-- NO trigger on auth.users; hook is SECURITY INVOKER; the old provisioning functions are gone.
select tgname from pg_trigger where tgrelid='auth.users'::regclass and not tgisinternal;   -- zero rows
select proname, prosecdef from pg_proc where pronamespace='access'::regnamespace order by proname;
-- expected: custom_access_token_hook (f)   NOT expected: provision_profile / assert_profile_provisioned / handle_new_user

-- Les GRANTs, distincts de la policy. Absent => permission denied, pas zero-row.
select has_schema_privilege('supabase_auth_admin', 'access', 'USAGE')           as schema_usage,
       has_table_privilege ('supabase_auth_admin', 'access.profiles', 'SELECT') as profiles_select;
-- attendu : t, t

-- Le hook exécutable par GoTrue, et par personne d'autre.
select has_function_privilege('supabase_auth_admin', 'access.custom_access_token_hook(jsonb)', 'EXECUTE') as auth_admin,
       has_function_privilege('authenticated',       'access.custom_access_token_hook(jsonb)', 'EXECUTE') as authenticated,
       has_function_privilege('anon',                'access.custom_access_token_hook(jsonb)', 'EXECUTE') as anon;
-- attendu : t, f, f

-- Le rôle GoTrue ne doit PAS voir tenants/memberships.
select has_table_privilege('supabase_auth_admin', 'access.tenants',     'SELECT') as ta,
       has_table_privilege('supabase_auth_admin', 'access.memberships', 'SELECT') as me;
-- attendu : f, f

-- Defense-in-depth : anon/authenticated n'ont AUCUN privilège sur access.*
select grantee, table_name, privilege_type
from information_schema.role_table_grants
where table_schema = 'access' and grantee in ('anon','authenticated');
-- attendu : ZÉRO ligne
```

## 4-6. Tests d'identité — exécution

Ces trois cas ne sont PAS exécutés à la main. Ils sont automatisés par un script JETABLE, hors
repo, dans un dossier scratch :

```
~/workspace/scratch/saasbtp-44/
  validate-44.sh   # non destructif — crée les users, guide l'insertion des profils, assert les JWT
  cleanup-44.sh    # DESTRUCTIF — supprime profils PUIS users auth, double confirmation
```

Ces scripts ne sont pas versionnés et ne doivent jamais être commités : ils sont dérivés de
CETTE checklist, qui reste la référence canonique. Si le modèle change, c'est ce fichier qui
fait foi, pas le script.

```bash
# le hook DOIT être réactivé (étape 2.6) avant de lancer, sinon le CAS A échoue pour rien
doppler run -p saas-btp -c dev -- ./validate-44.sh
# Exemple: 
doppler run -p saas-btp -c dev -- ./cleanup-44.sh \
  b3bd663a-a064-4a20-bc92-cd9116df791d \
  77482dd3-6f74-4ded-9d95-c1ef9456f9e9 \
  7188b228-2075-4d7b-9cbd-376a5940f583
```

Le script s'interrompt une fois pour te faire coller un `INSERT` dans l'éditeur SQL Supabase.
C'est structurel, pas un défaut : il n'y a plus de trigger de provisionnement, et le schéma
`access` n'est pas exposé à la Data API (`anon` / `authenticated` n'ont aucun privilège dessus —
vérifié en §3). `InviteMemberCommand` (#48) fera ce travail plus tard. En attendant, l'insertion
d'un profil est manuelle, par le rôle owner. `created_at` n'a pas de DEFAULT : il faut le fournir.

Aucun compte réel ne doit entrer dans cette séquence. Tous les emails sont `throwaway+*`.

---

### 4. Profil complet → claims d'identité, aucun tenant

Un user avec `full_name` ET `job_function` renseignés.

- [ ] Sign-in réussit.
- [ ] `observer_name` == `Jeanne Test`, au niveau TOP-LEVEL du JWT (pas dans `app_metadata`).
- [ ] `observer_function` == `Chef de chantier`, top-level.
- [ ] **AUCUN claim contenant `tenant`, à aucune profondeur du JWT.** C'est le changement de
      modèle : le tenant n'est plus une identité, c'est une autorisation, et elle vit dans
      `access.memberships`. La vérification est récursive sur tous les chemins du JSON, pas un
      simple test de `app_metadata.tenant_id` — un claim tenant qui réapparaîtrait ailleurs
      serait tout aussi faux.
- [ ] Les claims obligatoires Supabase sont tous présents : `iss`, `aud`, `exp`, `iat`, `sub`,
      `role`, `aal`, `session_id`, `email`. Le hook enrichit, il ne retire jamais.

### 5. Profil sans `job_function` → la CLÉ `observer_function` est ABSENTE

Un user avec `full_name` mais `job_function` à NULL.

- [ ] Sign-in réussit.
- [ ] `observer_name` == `Sans Fonction`.
- [ ] La clé `observer_function` est **absente du JWT**. Absente, pas présente à `null` : le hook
      ne pose la clé que si la valeur existe. Un consommateur qui teste la présence de la clé
      doit obtenir "pas de fonction", pas "fonction inconnue".
- [ ] Aucun claim tenant.

### 6. AUCUN profil → succès, et dégradation SILENCIEUSE

Un user créé sans `app_metadata`, sans profil, sans membership.

**C'est le test le plus important des trois.** Il prouve la rupture assumée de
« 1 auth user = 1 profil » (ADR-ARCH-008) : l'utilisateur s'authentifie, obtient un token
parfaitement valide, et n'a **aucune identité métier**. L'authentification n'implique plus
l'onboarding.

- [ ] `createUser` **RÉUSSIT**. L'ancien modèle refusait ce cas avec P0002 au COMMIT. Cette garde
      est supprimée, volontairement — c'est `InviteMemberCommand` (#48) qui garde désormais
      l'onboarding, plus la base.
- [ ] Aucune ligne dans `access.profiles` pour ce user.
- [ ] **Sign-in RÉUSSIT.** Le token est valide.
- [ ] Le JWT ne contient NI `observer_name`, NI `observer_function`, NI aucun claim tenant.
- [ ] **Dashboard → Logs → Postgres : AUCUNE ligne `access.custom_access_token_hook failed` pour
      ce user.** Vérification manuelle, le script ne peut pas la faire.

Ce dernier point n'est pas cosmétique. Un profil manquant est le chemin **nominal** : le hook
sort par `IF NOT FOUND`, retourne l'event inchangé, et n'écrit rien. Il ne passe PAS par la
branche `EXCEPTION`. Donc une ligne `failed` dans les logs signifierait que le hook échoue pour
une **autre** raison — grant révoqué, colonne renommée, table absente — et il faudrait comprendre
laquelle.

Angle mort à connaître : sous ce modèle, un profil manquant et une **policy RLS absente**
produisent la même signature observable (token sans claims, aucun log). C'est pourquoi la §3
vérifie séparément la policy ET les grants. Ne pas sauter la §3 en pensant que la §6 la couvre.

### Hors périmètre — ne pas remonter comme régression

`GET /me` et tout chemin scopé tenant échouent à l'exécution : le JWT ne porte plus `tenant_id`
et le .NET continue de le lire. Attendu, accepté, corrigé par #49.

### Nettoyage — obligatoire

```bash
doppler run -p saas-btp -c dev -- ./cleanup-44.sh <id-full> <id-nofunc> <id-noprof>
```

**Il n'y a PLUS de cascade.** `0002` ne pose aucune FK entre `access.profiles` et `auth.users`
(la frontière auth ne se traverse pas avec une contrainte). Supprimer un user auth ne supprime
donc plus son profil : la ligne resterait orpheline, et son email bloquerait `ux_profiles__email`
au prochain run. Le script supprime les profils EN SQL d'abord, les users auth ensuite.

- [ ] `select count(*) from access.profiles;` renvoie 0.
- [ ] Plus aucun user `throwaway+*` dans `auth.users`.
- [ ] `rm -rf ~/workspace/scratch/saasbtp-44`
- [ ] Run consigné (date, projet, pass/fail par section) dans le fichier de session.

## 7. Known-broken until #49 (do NOT file as a regression)

- `GET /me` and any tenant-scoped path fail at runtime: the JWT no longer carries `tenant_id` and the
  .NET tenant resolution still reads it. Expected and accepted (ADR-ARCH-008). Fixed by #49.

---

## 8. Cleanup

- [ ] Delete every `throwaway+...@example.com` user (dashboard or `admin.auth.admin.deleteUser`) and
      any profile row inserted by hand in steps 4-5.
- [ ] Record the run (date, project, pass/fail per step) in the session file.

Note on incidents: if the hook ever fails on a GENUINE error (renamed column, revoked grant, dropped
table), it does NOT break login — it returns the token unmodified — but it writes
`access.custom_access_token_hook failed for user ...: <SQLERRM> (<SQLSTATE>)` to the Postgres logs
(RAISE LOG). A missing profile is NOT such an error and produces no log line.

---

## Run consigné

| | |
|---|---|
| **Date** | 2026-07-13 |
| **Projet** | Supabase `boxxynsffybaemctpgdt` (unique — pas de staging) |
| **Scripts** | `0001_safety_create_constats` (intact) · `0002_access_identity_model` · `0003_access_identity_auth` |
| **Journal DbUp** | `schemaversions` — 3 lignes, dans l'ordre |

| Section | Résultat |
|---|---|
| §1 — public signup désactivé | ✅ |
| §2 — reset (squash rebaseline) | ✅ hook off → drop schema → purge journal 0002..0006 → migrator → hook on |
| §3 — sanité schéma | ✅ 7 colonnes, `ux_profiles__email`, policy `profiles_auth_admin_select`, RLS activée sur les 3 tables, `ix_memberships__tenant_id` absent, 0 trigger sur `auth.users`, `prosecdef = false` |
| §3 — sanité grants | ✅ `supabase_auth_admin` : USAGE + SELECT sur `profiles` uniquement · EXECUTE sur le hook uniquement (`t, f, f`) · **aucun** SELECT sur `tenants`/`memberships` · `anon`/`authenticated` : **zéro** privilège sur `access.*` |
| §4 — profil complet | ✅ `observer_name` = "Jeanne Test", `observer_function` = "Chef de chantier", top-level, aucun claim tenant à aucune profondeur |
| §5 — sans `job_function` | ✅ `observer_name` présent, clé `observer_function` **absente** (pas `null`) |
| §6 — sans profil | ✅ createUser réussit · sign-in réussit · aucun `observer_*` · **aucune ligne `access.custom_access_token_hook failed`** dans les logs Postgres |
| §8 — cleanup | ✅ 0 profil, 0 user `throwaway+*` |

**Preuve directe de l'absence de trigger** : le script de validation compte les lignes de
`access.profiles` APRÈS `createUser` et AVANT toute insertion. Résultat : 0. Un trigger de
provisionnement réintroduit un jour ferait échouer cette assertion.

**État final observé, et c'est le résultat attendu** : 3 users dans `auth.users`, 2 lignes dans
`access.profiles`. L'asymétrie EST le modèle (ADR-ARCH-008) : l'authentification n'implique plus
l'onboarding.

**Exécution** : scripts jetables `validate-44.sh` / `cleanup-44.sh` dans un dossier scratch hors
repo, supprimés après le run. Cette checklist reste la référence canonique.

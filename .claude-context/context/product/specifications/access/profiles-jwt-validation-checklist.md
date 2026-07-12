# Manual validation checklist — access.profiles + observer claims (#39, provisioning fix ADR-ARCH-007)

Why this exists: scripts `0003_access_profiles_permissions`, `0004_access_profiles_trigger`,
`0005_access_profiles_jwt_hook` and `0006_access_profiles_provisioning_fix` are auth-coupled (they
touch `auth.users` and `supabase_auth_admin`) and CANNOT be exercised in CI — the Testcontainer run
applies only the portable subset (0001, 0002). The triggers and the JWT hook are validated HERE, by
hand, against a real Supabase project. Do this on staging first. A mis-granted hook can break EVERY
login, so step 3 (throwaway user first) is not optional.

What changed vs. the original #39 design (read ADR-ARCH-007): GoTrue writes `app_metadata` by an
UPDATE issued AFTER the INSERT into `auth.users`, in the same transaction. The old single
`AFTER INSERT` trigger (0004, `on_auth_user_created`) therefore never saw the `tenant_id` and raised
`P0002` on EVERY signup. 0006 replaces it with:
- `on_auth_user_provisioned` -> `access.provision_profile()` — fires on INSERT AND on
  `UPDATE OF raw_app_meta_data`, so it sees the `tenant_id` when GoTrue's post-INSERT UPDATE writes it;
- `on_auth_user_profile_required` -> `access.assert_profile_provisioned()` — a CONSTRAINT TRIGGER
  DEFERRABLE INITIALLY DEFERRED that fires at COMMIT and rejects a signup that produced no profile.

Net effect for this checklist: a signup with no server-set `tenant_id` is still rejected with
`P0002`, but the rejection now surfaces at COMMIT (the deferred guard), not at the INSERT. A signup
with a `tenant_id` but a blank `full_name` is rejected with `P0003` when the provisioning trigger runs
on the app_metadata UPDATE. The Admin-API createUser call fails in both cases just as before.

Legend: `[ ]` to do, `[x]` done. Fill the `<...>` placeholders before running anything.

---

## 0. EXPECTED BEHAVIOR — users can ONLY be created via the Admin API (this is NOT a broken migration)

Read this before touching the project, and re-read it the first time you see `P0002`. V1 is
invite-only, server-provisioned: a user can be created ONLY through the Admin API with BOTH set
server-side:
- `app_metadata.tenant_id`  (SERVER-set, never client-supplied)
- `user_metadata.full_name` (the mandatory observer identity)

As a direct consequence, these flows WILL fail until the #39 constraint is revisited. This is
EXPECTED behavior, not a fault:
- Supabase Dashboard -> "Add user": the modal sets only email/password (no app_metadata) -> the
  deferred guard rejects it with `P0002` at COMMIT.
- OAuth first login (Google, etc.): no server-set tenant_id -> `P0002` at COMMIT.
- Magic-link / OTP signup of a NEW email -> `P0002` at COMMIT.
- Anonymous sign-in -> `P0002` at COMMIT.
- Bulk import: every record must carry `tenant_id` + `full_name`. The guard is a DEFERRED constraint
  trigger evaluated at COMMIT, so if the import runs as a single transaction, ONE non-conforming record
  rolls back the WHOLE batch at COMMIT (not just that row). Do not triage this as "find the one bad
  row persisted alongside the good ones" — nothing from that transaction persisted.

These are UPDATEs to an existing `auth.users` row, not inserts, so no signup guard fires — safe:
- Existing users signing in, password recovery, email change, re-confirmation.
  (Note: `on_auth_user_provisioned` also fires on `UPDATE OF raw_app_meta_data`. For an existing user
  who ALREADY HAS a profile — the normal case, and every user backfilled per step 7 — this is a no-op:
  `access.provision_profile` short-circuits on its first guard and does nothing, so an app_metadata
  change never overwrites the captured identity. CAVEAT for a pre-existing user NOT yet backfilled (no
  profile row): that same `UPDATE OF raw_app_meta_data` takes the provisioning path, not the
  short-circuit. If the update carries a `tenant_id` but the user's `raw_user_meta_data.full_name` is
  blank, it raises `P0003` and the admin metadata update FAILS. Backfill such users (step 7) before
  editing their app_metadata.)

If you see `P0002` while adding a user, the migration is working as designed — create the user via the
Admin API (step 3) with a server-set `tenant_id`, not through the Dashboard.

---

## 1. Apply migrations 0001-0006

The human runs the migrator (never Claude, never automatically). It applies every pending script in
order (0001..0006) in one journaled history.

```
# from tools/database-migrator/SaasBtp.Database.Migrator
ConnectionStrings__MigratorPostgres='<direct-owner-postgres-connection-string>' \
  dotnet run -c Release
```

- [ ] Output ends with "Migration complete — all pending scripts applied."
- [ ] `schemaversions` now lists 0002..0006 (0001 was already applied in session 013).

Sanity (psql, as owner):
```sql
select scriptname from schemaversions order by applied desc limit 6;
select relrowsecurity from pg_class where oid = 'access.profiles'::regclass;          -- t
select polname, polcmd from pg_policy where polrelid = 'access.profiles'::regclass;   -- profiles_auth_admin_select, r (SELECT)

-- 0004's objects are gone; 0006's two functions exist, both SECURITY DEFINER; the hook stays INVOKER.
select proname, prosecdef from pg_proc where pronamespace = 'access'::regnamespace order by proname;
-- expected: assert_profile_provisioned (t), custom_access_token_hook (f), provision_profile (t)
-- NOT expected: handle_new_user (dropped by 0006)

-- Two triggers on auth.users; the guard is a deferred constraint trigger.
select tgname,
       (tgconstraint <> 0) as is_constraint,
       tgdeferrable,
       tginitdeferred
from pg_trigger
where tgrelid = 'auth.users'::regclass and not tgisinternal
order by tgname;
-- expected: on_auth_user_profile_required (is_constraint t, deferrable t, initdeferred t)
--           on_auth_user_provisioned      (is_constraint f)
-- NOT expected: on_auth_user_created (dropped by 0006)
```

## 2. Enable the access token hook

Supabase dashboard: Authentication -> Hooks (Auth Hooks) -> Customize Access Token (JWT) Claims ->
enable, and select the Postgres function `access.custom_access_token_hook`.

- [ ] Hook enabled and pointing at `access.custom_access_token_hook`.

## 3. CREATE A THROWAWAY USER FIRST (do NOT test on a real account)

A mis-granted hook or a wrong function signature can break login for everyone. Prove the happy path on
a disposable user before trusting any real account.

Create the user with `tenant_id` in app_metadata (server-set) and `full_name` (+ optional `function`)
in user_metadata. Admin API (service-role key, server side only):

```js
// throwaway-signup.mjs — run with the SERVICE ROLE key, never in a browser
import { createClient } from '@supabase/supabase-js'
const admin = createClient(process.env.SUPABASE_URL, process.env.SUPABASE_SERVICE_ROLE_KEY)
const { data, error } = await admin.auth.admin.createUser({
  email: 'throwaway+ok@example.com',
  password: 'Throwaway-123!',
  email_confirm: true,
  app_metadata:  { tenant_id: '<existing-tenant-uuid>' },   // SERVER-set -> raw_app_meta_data (via GoTrue's post-INSERT UPDATE)
  user_metadata: { full_name: 'Jeanne Test', function: 'Chef de chantier' },
})
console.log(error ?? data.user.id)
```

- [ ] User created without error. (The provisioning trigger inserted the profile on GoTrue's
      app_metadata UPDATE; the deferred guard then found it at COMMIT and allowed the signup.)
- [ ] Then sign IN as that user (password grant) to mint an access token carrying the hook's claims.

## 4. Decode the throwaway user's JWT

Decode the access token locally (do not paste real tokens into web decoders; a throwaway is fine, but
local is the habit to keep):

```
# payload = 2nd dot-separated segment, base64url; add padding then decode
printf '%s' "<access_token>" | cut -d. -f2 | tr '_-' '/+' | \
  awk '{ while (length($0)%4) $0=$0"="; print }' | base64 -d | jq
```

Assert on the payload:
- [ ] `app_metadata.tenant_id` == `<existing-tenant-uuid>` (NESTED, a JSON string).
- [ ] `app_metadata` still carries anything it had before (e.g. `provider`/`providers`) — not clobbered.
- [ ] top-level `observer_name` == "Jeanne Test".
- [ ] top-level `observer_function` == "Chef de chantier".

## 5. Verify the profiles row (same transaction as signup)

```sql
select user_id, tenant_id, full_name, function, created_at
from access.profiles
where user_id = '<throwaway-user-id>';
```

- [ ] Exactly one row; `full_name`/`function`/`tenant_id` match what was sent; `created_at` is set.

## 6. Negative tests (validation + graceful degradation)

6a. Missing/blank tenant_id -> signup REJECTED with SQLSTATE P0002 AT COMMIT (the deferred guard):
```js
await admin.auth.admin.createUser({
  email: 'throwaway+no-tenant@example.com', password: 'Throwaway-123!', email_confirm: true,
  user_metadata: { full_name: 'No Tenant' },        // no app_metadata.tenant_id
})
```
- [ ] createUser fails; the error traces back to `P0002` from `access.assert_profile_provisioned`
      (no profile row) in the DB logs — raised at COMMIT, not at the INSERT.
- [ ] No `auth.users` row and no `access.profiles` row persisted (the whole signup rolled back).

6b. tenant_id present but missing/blank full_name -> signup REJECTED with SQLSTATE P0003:
```js
await admin.auth.admin.createUser({
  email: 'throwaway+no-name@example.com', password: 'Throwaway-123!', email_confirm: true,
  app_metadata: { tenant_id: '<existing-tenant-uuid>' },   // no user_metadata.full_name
})
```
- [ ] createUser fails; error traces back to `P0003` from `access.provision_profile` (full_name
      missing/blank), raised when the trigger runs on GoTrue's app_metadata UPDATE.
- [ ] No row persisted (rolled back).

SQLSTATE change vs 0004 (diagnostic note, no separate test): a MALFORMED (non-uuid) `tenant_id` no
longer surfaces as `P0002` — 0006 validates it by the `v_tenant_text::uuid` cast in
`access.provision_profile`, which raises `22P02` (invalid_text_representation) and rolls the signup
back. And because the full_name check (Guard 3) precedes that cast, a malformed `tenant_id` combined
with a blank `full_name` is MASKED by `P0003`. When debugging a rejected signup, read the SQLSTATE:
`P0002` = no profile at COMMIT (missing tenant), `P0003` = blank full_name, `22P02` = malformed tenant.

6c. Missing optional function -> login STILL SUCCEEDS, `observer_function` simply absent:
```js
await admin.auth.admin.createUser({
  email: 'throwaway+no-func@example.com', password: 'Throwaway-123!', email_confirm: true,
  app_metadata: { tenant_id: '<existing-tenant-uuid>' }, user_metadata: { full_name: 'Sans Fonction' },
})
```
- [ ] Signup OK; sign in OK; decoded JWT has `observer_name` and NO `observer_function` key.

## 7. Backfill the pre-existing test user (predates the trigger)

The account created before #39 fired no signup event, so it has no `access.profiles` row and its token
would hit the hook's nominal "no profile -> return event unchanged" path (login fine, but no
observer_name). Backfill it once, as owner:

```sql
-- Reads tenant_id/full_name/function from the user's existing GoTrue metadata.
-- If full_name is absent there, replace the coalesce fallback with an explicit literal.
insert into access.profiles (user_id, tenant_id, full_name, function, created_at)
select u.id,
       (u.raw_app_meta_data  ->> 'tenant_id')::uuid,
       coalesce(nullif(btrim(u.raw_user_meta_data ->> 'full_name'), ''), '<explicit full name>'),
       nullif(btrim(u.raw_user_meta_data ->> 'function'), ''),
       now()
from auth.users as u
where u.email = '<existing-test-user-email>'
on conflict (user_id) do nothing;
```

- [ ] One row inserted for the existing user.
- [ ] Re-login the existing user; its JWT now carries `observer_name` (+ `observer_function` if set).

## 8. Regression — tenancy still resolves

- [ ] `GET /me` returns 200 and the expected user (store-validated pipeline).
- [ ] `GET /context` resolves the tenant (proves the NESTED `app_metadata.tenant_id` shape is intact
      and the hook did not clobber it).

## 9. Rollback (if provisioning misbehaves)

ALWAYS disable the hook in the dashboard FIRST (stops GoTrue calling it), THEN drop objects explicitly
— never the inverse. Drop each trigger BEFORE its function; do not rely on CASCADE:

```sql
-- 1. dashboard: Authentication -> Hooks -> disable the Custom Access Token hook. THEN:
drop trigger  if exists on_auth_user_provisioned      on auth.users;
drop trigger  if exists on_auth_user_profile_required on auth.users;
drop function if exists access.provision_profile();
drop function if exists access.assert_profile_provisioned();
drop function if exists access.custom_access_token_hook(jsonb);
-- access.profiles + its grant/policy can stay; drop only if fully reverting #39.
-- (Reverting to 0004's on_auth_user_created is NOT a rollback — that trigger is the bug ADR-ARCH-007
--  documents. It was dropped by 0006 and must not be recreated.)
```

- [ ] Logins succeed again after disabling the hook (confirms the hook was the cause, if debugging).

---

## 10. Cleanup

- [ ] Delete every `throwaway+...@example.com` user (dashboard or `admin.auth.admin.deleteUser`).
- [ ] Record the run (date, project/env, pass/fail per step) in the session file.

Note on incidents: if the hook ever fails on a genuine error (renamed column, revoked grant, dropped
table), it does NOT break login — it returns the token unmodified — but it writes
`access.custom_access_token_hook failed for user ...: <SQLERRM> (<SQLSTATE>)` to the Postgres logs
(RAISE LOG). If users report "logged in but cannot file a constat", check those logs first.

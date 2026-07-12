# Manual validation checklist — access.profiles + observer claims (#39)

Why this exists: scripts `0003_access_profiles_permissions`, `0004_access_profiles_trigger`, and
`0005_access_profiles_jwt_hook` are auth-coupled (they touch `auth.users` and `supabase_auth_admin`)
and CANNOT be exercised in CI — the Testcontainer run applies only the portable subset (0001, 0002).
The trigger and the JWT hook are validated HERE, by hand, against a real Supabase project. Do this on
staging first. A mis-granted hook can break EVERY login, so step 3 is not optional.

Legend: `[ ]` to do, `[x]` done. Fill the `<...>` placeholders before running anything.

Prerequisites
- A DIRECT, privileged Postgres connection to the target project (owner/`postgres` role, session
  pooler — NOT PostgREST). This is the same connection the migrator needs for DDL + RLS.
- The `tenant_id` used for signups is an existing tenant's UUID (server-set; never client-typed).

---

## 0. EXPECTED BEHAVIOR — users can ONLY be created via the Admin API (this is NOT a broken migration)

Read this before touching the project, and re-read it the first time you see `P0002`. The signup
trigger (0004) fires on EVERY insert into `auth.users` and RAISEs `P0002` when `app_metadata.tenant_id`
is absent (and `P0003` when `user_metadata.full_name` is absent). By design — V1 is invite-only,
server-provisioned — a user can be created ONLY through the Admin API with BOTH set server-side:
- `app_metadata.tenant_id`  (SERVER-set, never client-supplied)
- `user_metadata.full_name` (the mandatory observer identity)

As a direct consequence, these flows WILL fail with `P0002` until the #39 constraint is revisited.
This is EXPECTED behavior, not a fault:
- Supabase Dashboard -> "Add user": the modal sets only email/password (no app_metadata) -> fails EVERY time.
- OAuth first login (Google, etc.): no server-set tenant_id -> signup fails.
- Magic-link / OTP signup of a NEW email -> fails.
- Anonymous sign-in -> fails.
- Bulk import: every record must carry `tenant_id` + `full_name`, or that row fails.

These are UPDATEs to an existing `auth.users` row, not inserts, so the trigger does NOT fire — safe:
- Existing users signing in, password recovery, email change, re-confirmation.

If you see `P0002` while adding a user, the migration is working as designed — create the user via the
Admin API (step 3) with a server-set `tenant_id`, not through the Dashboard.

---

## 1. Apply migrations 0001–0005

The human runs the migrator (never Claude, never automatically). It applies every pending script in
order (0001..0005) in one journaled history.

```
# from tools/database-migrator/SaasBtp.Database.Migrator
ConnectionStrings__MigratorPostgres='<direct-owner-postgres-connection-string>' \
  dotnet run -c Release
```

- [ ] Output ends with "Migration complete — all pending scripts applied."
- [ ] `schemaversions` now lists 0002..0005 (0001 was already applied in session 013).

Sanity (psql, as owner):
```sql
select scriptname from schemaversions order by applied desc limit 5;
select relrowsecurity from pg_class where oid = 'access.profiles'::regclass;          -- t
select polname, polcmd from pg_policy where polrelid = 'access.profiles'::regclass;   -- profiles_auth_admin_select, r (SELECT)
select proname, prosecdef from pg_proc where pronamespace = 'access'::regnamespace;   -- handle_new_user (secdef t), custom_access_token_hook (secdef f)
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
  app_metadata:  { tenant_id: '<existing-tenant-uuid>' },   // SERVER-set → raw_app_meta_data
  user_metadata: { full_name: 'Jeanne Test', function: 'Chef de chantier' },
})
console.log(error ?? data.user.id)
```

- [ ] User created without error (the `AFTER INSERT` trigger accepted it).
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

6a. Missing/blank tenant_id -> signup REJECTED with SQLSTATE P0002:
```js
await admin.auth.admin.createUser({
  email: 'throwaway+no-tenant@example.com', password: 'Throwaway-123!', email_confirm: true,
  user_metadata: { full_name: 'No Tenant' },        // no app_metadata.tenant_id
})
```
- [ ] createUser fails; the error traces back to `P0002` (tenant_id missing/blank) in the DB logs.
- [ ] No `auth.users` row and no `access.profiles` row persisted (the whole signup rolled back).

6b. Missing/blank full_name -> signup REJECTED with SQLSTATE P0003:
```js
await admin.auth.admin.createUser({
  email: 'throwaway+no-name@example.com', password: 'Throwaway-123!', email_confirm: true,
  app_metadata: { tenant_id: '<existing-tenant-uuid>' },   // no user_metadata.full_name
})
```
- [ ] createUser fails; error traces back to `P0003` (full_name missing/blank).

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

## 9. Rollback (if the hook misbehaves)

First disable the hook in the dashboard (stops GoTrue calling it), then drop objects explicitly (drop
the trigger BEFORE its function; do not rely on CASCADE):

```sql
drop trigger if exists on_auth_user_created on auth.users;
drop function if exists access.handle_new_user();
drop function if exists access.custom_access_token_hook(jsonb);
-- access.profiles + its grant/policy can stay; drop only if fully reverting #39.
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

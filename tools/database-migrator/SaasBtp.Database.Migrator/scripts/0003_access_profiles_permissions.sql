-- 0003_access_profiles_permissions.sql
-- Owner: DbUp (ADR-ARCH-006). Applied by tools/database-migrator with the privileged owner role.
--
-- AUTH-COUPLED script (conventions/sql.md §Auth-coupled scripts): it names the GoTrue role
-- supabase_auth_admin, which does NOT exist on bare Postgres. Therefore it is EXCLUDED from the
-- anti-drift Testcontainer run and validated only against a real Supabase project (manual checklist).
--
-- WHY THIS FILE EXISTS AT ALL — the crux of story #39:
--   access.profiles has a NON-PRIVILEGED consumer: access.custom_access_token_hook (0005) runs as
--   supabase_auth_admin under SECURITY INVOKER. supabase_auth_admin is NOT a superuser and does NOT
--   bypass RLS. With RLS enabled (0002) and no policy, its SELECT returns ZERO ROWS — not an error,
--   a SILENT empty read — so the access token would ship without observer_name and the user could
--   log in yet be unable to file a constat. That is the exact silent failure #39 exists to remove.
--   safety.constats deliberately differs: it has no non-privileged consumer, so RLS-on + no-policy
--   is correct there. Here we need exactly ONE policy.

-- The hook only READS. SELECT only — NEVER GRANT ALL: writes to access.profiles go solely through
-- the SECURITY DEFINER signup trigger (0004), which runs as the table owner and needs no grant.
GRANT USAGE ON SCHEMA access TO supabase_auth_admin;
GRANT SELECT ON access.profiles TO supabase_auth_admin;

-- The one policy that makes the hook's read non-empty. USING (true) is correct because
-- supabase_auth_admin is trusted GoTrue INFRASTRUCTURE, not a tenant: tenant isolation is not this
-- policy's job (the Supabase Data API is off; no client role reads access.profiles — there is no
-- anon / authenticated grant or policy). Scoped to SELECT only, matching the grant above.
--
-- DO NOT "fix" USING (true) by adding a tenant_id predicate — it is NOT a security hole. The hook runs
-- as supabase_auth_admin, which has NO tenant context (it looks a user up by user_id, across tenants).
-- A tenant_id predicate would match ZERO ROWS on every call, so the hook would find no profile and
-- ship each token silently truncated (no observer_name): every user would log in yet be unable to file
-- a constat, with no error anywhere. That silent truncation is the EXACT failure #39 exists to remove.
CREATE POLICY profiles_auth_admin_select
    ON access.profiles
    FOR SELECT
    TO supabase_auth_admin
    USING (true);

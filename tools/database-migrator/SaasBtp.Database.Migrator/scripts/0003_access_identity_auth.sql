-- 0003_access_identity_auth.sql
-- Owner: DbUp (ADR-ARCH-006). Applied by tools/database-migrator with the privileged owner (postgres)
-- role, which therefore OWNS the hook function.
--
-- AUTH-COUPLED script (conventions/sql.md §Auth-coupled scripts): it names the GoTrue role
-- supabase_auth_admin (grant, policy, and a SECURITY INVOKER function it executes), which does NOT
-- exist on bare Postgres. It is EXCLUDED from the anti-drift Testcontainer run (registered in
-- MigrationRunner.AuthCoupledScriptMarkers) and validated only against a real Supabase project, by the
-- manual checklist (profiles-jwt-validation-checklist.md). Its portable sibling 0002 (the tables) IS
-- anti-drift tested.
--
-- WHAT: the auth-boundary layer of the identity model (ADR-ARCH-008) — the read grant + policy that let
-- the JWT hook see access.profiles, and the hook itself. The hook is IDENTITY-ONLY: it stamps the
-- observer claims Safety needs and carries NO tenant. There is NO trigger on auth.users (onboarding is
-- InviteMemberCommand, #48).

-- ---------------------------------------------------------------------------------------------------
-- a. Grant the hook's read path. The hook only READS. SELECT only — NEVER GRANT ALL: access.profiles is
--    written solely by the .NET domain (ADR-ARCH-005), which connects as the owner and needs no grant.
-- ---------------------------------------------------------------------------------------------------
GRANT USAGE ON SCHEMA access TO supabase_auth_admin;
GRANT SELECT ON access.profiles TO supabase_auth_admin;

-- ---------------------------------------------------------------------------------------------------
-- b. The ONE policy that makes the hook's read non-empty.
--
--    CRITICAL — DO NOT REMOVE, DO NOT ADD A PREDICATE. access.custom_access_token_hook (below) runs as
--    supabase_auth_admin under SECURITY INVOKER. supabase_auth_admin is NOT a superuser and does NOT
--    bypass RLS. With RLS enabled (0002) and NO policy, its SELECT returns ZERO ROWS — not an error, a
--    SILENT empty read — so the token ships without observer_name and the user logs in yet cannot file a
--    constat, with no error anywhere. That silent truncation is the single most expensive bug of the
--    project so far; this policy is the fix.
--
--    USING (true) is correct: supabase_auth_admin is trusted GoTrue INFRASTRUCTURE, not a tenant. Tenant
--    isolation is not this policy's job (the Supabase Data API is off; no anon/authenticated role reads
--    access.profiles). Adding a tenant_id predicate to "harden" it would be doubly wrong here: there is
--    NO tenant column on profiles anymore (identity model), and even a user-scoped predicate would match
--    zero rows on every call (the hook looks a user up by user_id across the whole table) and resurrect
--    the exact silent-truncation bug. Scoped to SELECT only, matching the grant above.
-- ---------------------------------------------------------------------------------------------------
CREATE POLICY profiles_auth_admin_select
    ON access.profiles
    FOR SELECT
    TO supabase_auth_admin
    USING (true);

-- ---------------------------------------------------------------------------------------------------
-- c. The Custom Access Token hook — IDENTITY-ONLY. On every token issuance GoTrue calls this with the
--    pending token `event` and uses the `claims` it returns; we enrich them with the observer identity so
--    the Safety context can stamp a constat's ObserverSnapshot straight from the JWT.
--
--    CREATE (not CREATE OR REPLACE): this is the function's first definition in the rebaselined schema,
--    so ownership and privileges are NOT inherited from anything — the GRANT/REVOKE in (d) is RESTATED
--    explicitly. Attributes are load-bearing and match the prior hook exactly: LANGUAGE plpgsql, STABLE,
--    SECURITY INVOKER (Supabase recommends AGAINST security definer for hooks — the read runs as
--    supabase_auth_admin, which is precisely why (a)/(b) grant that role SELECT + the policy),
--    SET search_path = '' with every identifier fully qualified (a mutable search_path on a hook is
--    hijackable).
--
--    CLAIM CONTRACT: observer_name / observer_function are TOP-LEVEL and named for their CONSUMER
--    (Safety), NOT after Access's columns. The source column is job_function; the CLAIM stays
--    observer_function. Access may rename its columns without touching the token contract.
-- ---------------------------------------------------------------------------------------------------
CREATE FUNCTION access.custom_access_token_hook(event jsonb)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SECURITY INVOKER
    SET search_path = ''
AS $$
DECLARE
    v_claims       jsonb;
    v_full_name    text;
    v_job_function text;
BEGIN
    SELECT p.full_name, p.job_function
      INTO v_full_name, v_job_function
      FROM access.profiles AS p
     WHERE p.user_id = (event ->> 'user_id')::uuid;

    IF NOT FOUND THEN
        -- No profile row. Under the identity model (ADR-ARCH-008) this is EXPECTED, not an incident:
        -- authentication no longer implies onboarding, so a user can sign in with NO business identity
        -- (no profile, no membership). Return the token UNCHANGED and SILENTLY — no log, no raise, no
        -- email fallback. The observer claims are simply absent; a profile-less user cannot file a
        -- constat anyway (safety.constats.observer_full_name is NOT NULL, sourced from observer_name),
        -- so the .NET domain, not this hook, refuses the write. The silent degrade is intentional; it is
        -- NOT a missing guard.
        RETURN event;
    END IF;

    v_claims := event -> 'claims';

    -- Top-level observer claims (named for the Safety consumer). observer_name always; observer_function
    -- only when present. NO tenant_id read, NO app_metadata merge — the tenant is not in the JWT.
    v_claims := pg_catalog.jsonb_set(v_claims, '{observer_name}', pg_catalog.to_jsonb(v_full_name), true);
    IF v_job_function IS NOT NULL THEN
        v_claims := pg_catalog.jsonb_set(v_claims, '{observer_function}', pg_catalog.to_jsonb(v_job_function), true);
    END IF;

    RETURN pg_catalog.jsonb_set(event, '{claims}', v_claims, true);
EXCEPTION WHEN others THEN
    -- Genuine incident (renamed column, revoked grant, dropped table): make it observable, then degrade
    -- to an unmodified token so login survives. A raising hook would break EVERY login on the project.
    -- (A missing profile is NOT such an incident — it is handled above without a log.)
    RAISE LOG 'access.custom_access_token_hook failed for user %: % (%)',
        event ->> 'user_id', SQLERRM, SQLSTATE;
    RETURN event;
END;
$$;

-- ---------------------------------------------------------------------------------------------------
-- d. Hook permissions per Supabase auth-hooks docs: callable by GoTrue only, never via the Data API.
--    RESTATED explicitly because (c) is a fresh CREATE — nothing is inherited.
-- ---------------------------------------------------------------------------------------------------
GRANT EXECUTE ON FUNCTION access.custom_access_token_hook(jsonb) TO supabase_auth_admin;
REVOKE EXECUTE ON FUNCTION access.custom_access_token_hook(jsonb) FROM authenticated, anon, public;

-- ---------------------------------------------------------------------------------------------------
-- e. NO trigger on auth.users (onboarding is InviteMemberCommand, #48). NO grant on access.tenants or
--    access.memberships — the hook reads neither (they carry no JWT claim; their non-owner policy is #50).
-- ---------------------------------------------------------------------------------------------------

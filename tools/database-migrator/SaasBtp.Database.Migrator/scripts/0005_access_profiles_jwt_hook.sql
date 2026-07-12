-- 0005_access_profiles_jwt_hook.sql
-- Owner: DbUp (ADR-ARCH-006). Applied by tools/database-migrator with the privileged owner role.
--
-- AUTH-COUPLED script (conventions/sql.md §Auth-coupled scripts): it grants to / revokes from the
-- GoTrue roles supabase_auth_admin / authenticated / anon, absent from bare Postgres. EXCLUDED from
-- the Testcontainer run; validated only against a real Supabase project (manual checklist).
--
-- WHAT: the Supabase Custom Access Token hook. On every token issuance GoTrue calls this function
-- with the pending token `event` and uses the `claims` it returns; we enrich those claims with the
-- observer identity so the Safety context can stamp a constat's ObserverSnapshot straight from the
-- JWT. The `event` shape is { user_id, claims { app_metadata, ... }, authentication_method } and the
-- hook returns the whole event with claims modified (verified against Supabase docs, 2026-07-12:
-- auth/auth-hooks/custom-access-token-hook).
--
-- SECURITY INVOKER (runs as supabase_auth_admin), SET search_path = '' (qualify refs). Supabase
--   RECOMMENDS AGAINST security definer for hooks (it would run with postgres' broad rights);
--   instead the hook reads under supabase_auth_admin, which is exactly why 0003 grants that role
--   SELECT + a FOR SELECT policy on access.profiles.
--
-- CLAIM-SHAPE CONTRACT (do NOT "harmonize" the two shapes — that silently breaks tenancy):
--   * tenant_id is written NESTED inside claims.app_metadata, as a JSON STRING, because the existing
--     MicroKit tenancy pipeline (MicroKit.Auth.Supabase.SupabaseClaimsMapper) reads it there via
--     Guid.TryParse(GetString()), never top-level. Changing that shape means a .NET change +
--     re-verification. We MERGE into the existing app_metadata (it already carries
--     provider/providers/roles) — never clobber it.
--   * observer_name / observer_function are TOP-LEVEL: greenfield, consumed by Safety. Claims are
--     named for their CONSUMER, not after Access's columns (full_name/function) — Access stays free
--     to rename its columns without touching the token contract.
--
-- STALENESS WINDOW (by design, not a bug): a later full_name change reaches the JWT only at the next
--   login/refresh. Harmless here — a constat's ObserverSnapshot is frozen at capture time anyway, so
--   a slightly stale claim is arguably the correct value. Documented so nobody debugs a transient
--   "DB != JWT" mismatch.
--
-- FAILURE POLICY — never raises on a DATA or LOGIC error (a raising hook breaks EVERY login on the
--   project), but never fails SILENTLY either. Honest caveat, NOT an absolute: Postgres's WHEN OTHERS
--   does not catch QUERY_CANCELED / ASSERT_FAILURE, so a statement cancellation (statement_timeout, an
--   admin cancel) can still escape the handler and fail THAT one login. Kept rare by keeping this a
--   single-row primary-key lookup — NOT by trapping query_canceled (the Postgres docs call that
--   unwise, and it would mask a genuine timeout). Two distinct paths, see the body:
--     (a) nominal missing profile (a user predating 0004, awaiting backfill) -> return event
--         unchanged, NO log: this is expected, not an incident.
--     (b) genuine incident (renamed column, revoked grant, dropped table, typo) -> RAISE LOG then
--         return event: a bare `WHEN OTHERS THEN RETURN event` would ship a silently truncated token
--         (user logs in fine, cannot file a constat, zero signal) — the exact failure #39 exists to
--         kill. The LOG makes it observable while keeping login available.
--
-- ROLLBACK (manual, explicit — same order as the checklist; the two MUST agree):
--   1. Disable the hook in the Supabase dashboard FIRST (Authentication -> Hooks). This stops GoTrue
--      calling it. Dropping the function while it is still registered makes GoTrue invoke a missing
--      function -> EVERY login fails HARDER than before, mid-incident.
--   2. DROP FUNCTION IF EXISTS access.custom_access_token_hook(jsonb);

CREATE OR REPLACE FUNCTION access.custom_access_token_hook(event jsonb)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SECURITY INVOKER
    SET search_path = ''
AS $$
DECLARE
    v_claims       jsonb;
    v_app_metadata jsonb;
    v_full_name    text;
    v_function     text;
    v_tenant_id    text;
BEGIN
    SELECT p.full_name, p.function, p.tenant_id::text
      INTO v_full_name, v_function, v_tenant_id
      FROM access.profiles AS p
     WHERE p.user_id = (event ->> 'user_id')::uuid;

    IF NOT FOUND THEN
        -- (a) Nominal: no profile yet (a user predating 0004, awaiting backfill). Not an incident,
        --     so NO log — only genuine incidents (handler below) should reach the log.
        RETURN event;
    END IF;

    v_claims := event -> 'claims';

    -- Merge tenant_id into the EXISTING app_metadata (never clobber provider/providers/roles). Stored
    -- as a JSON STRING to match SupabaseClaimsMapper's Guid.TryParse(GetString()).
    v_app_metadata := coalesce(v_claims -> 'app_metadata', '{}'::jsonb);
    v_app_metadata := pg_catalog.jsonb_set(v_app_metadata, '{tenant_id}', pg_catalog.to_jsonb(v_tenant_id), true);
    v_claims := pg_catalog.jsonb_set(v_claims, '{app_metadata}', v_app_metadata, true);

    -- Top-level observer claims (named for the Safety consumer). observer_function only when present.
    v_claims := pg_catalog.jsonb_set(v_claims, '{observer_name}', pg_catalog.to_jsonb(v_full_name), true);
    IF v_function IS NOT NULL THEN
        v_claims := pg_catalog.jsonb_set(v_claims, '{observer_function}', pg_catalog.to_jsonb(v_function), true);
    END IF;

    RETURN pg_catalog.jsonb_set(event, '{claims}', v_claims, true);
EXCEPTION WHEN others THEN
    -- (b) Genuine incident: make it observable, then degrade to an unmodified token (login survives).
    RAISE LOG 'access.custom_access_token_hook failed for user %: % (%)',
        event ->> 'user_id', SQLERRM, SQLSTATE;
    RETURN event;
END;
$$;

-- Permission setup per Supabase auth-hooks docs (2026-07-12): the hook is callable by GoTrue only,
-- never via the Data API (no authenticated / anon / public execute).
GRANT EXECUTE ON FUNCTION access.custom_access_token_hook(jsonb) TO supabase_auth_admin;
REVOKE EXECUTE ON FUNCTION access.custom_access_token_hook(jsonb) FROM authenticated, anon, public;

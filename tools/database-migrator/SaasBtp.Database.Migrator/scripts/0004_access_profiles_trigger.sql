-- 0004_access_profiles_trigger.sql
-- Owner: DbUp (ADR-ARCH-006). Applied by tools/database-migrator with the privileged owner
-- (postgres) role, which therefore OWNS access.handle_new_user() below.
--
-- AUTH-COUPLED script (conventions/sql.md §Auth-coupled scripts): it creates a trigger ON the GoTrue
-- table auth.users, absent from bare Postgres. EXCLUDED from the Testcontainer run; validated only
-- against a real Supabase project (manual checklist). Testing this against a fabricated auth.users
-- would prove nothing about the real GoTrue lifecycle and would give false confidence.
--
-- WHAT: projects the minimal business identity into access.profiles the instant a user is created,
-- INSIDE the signup transaction, so a logged-in user ALWAYS already has the observer identity a
-- constat requires. A blank/missing name is rejected here (signup fails), not discovered later at
-- constat time.
--
-- BOUNDARY (ADR-ARCH-005 auth-boundary exception — read it before touching this):
--   ALLOWED   : extending the GoTrue Auth lifecycle with a trigger that only READS NEW and WRITES
--               our own schema (access.profiles).
--   FORBIDDEN : mutating the GoTrue model itself (UPDATE/INSERT auth.users) from a migration.
--   The function's LOGIC is deliberately thin: NO business computation, NO business decision, NO
--   authorization, NO orchestration — ONLY the minimal validation the identity projection needs.
--   The mandatory observer name is ONE functional rule and is allowed; membership, roles,
--   permissions and any computed state stay OUT (domain logic hidden in a trigger is invisible from
--   .NET — exactly what ADR-ARCH-005 exists to prevent).
--
-- WHY THE RAISE IS A V1 PRODUCT CONSTRAINT, NOT A TECHNICAL LAW:
--   Every human user of the V1 product files constats, and a constat requires an observer name; a
--   blank-name or missing profile would merely relocate the failure to constat time (opaque, late).
--   This MUST be revisited if enterprise SSO/OIDC or service accounts arrive — they may legitimately
--   carry no full_name. Nobody should read this rule as immutable.
--
-- TRUST ASSUMPTION on the inputs:
--   full_name comes from raw_user_meta_data — the client's OWN signup payload. Acceptable: it is the
--   user's own data, like any registration form. The SECURITY boundary is tenant_id, which is NEVER
--   client-supplied: it is read from raw_app_meta_data (app metadata is server-set; GoTrue does not
--   let a client write it at signup).
--
-- SECURITY DEFINER (owned by postgres) + SET search_path = '' is MANDATORY hardening: a definer
--   function with a mutable search_path is hijackable, so every reference is schema-qualified
--   (pg_catalog.now(), pg_catalog.btrim(), access.profiles). DEFINER is required HERE (unlike the
--   0005 hook): the writer at signup is supabase_auth_admin, which has NO write grant on
--   access.profiles; running the body as the postgres owner lets the INSERT succeed and bypass RLS
--   by ownership — without ever handing GoTrue a write grant on our table.
--
-- SQLSTATEs use Postgres's application-defined class P0xxx (not an invented ACxxx class, which could
--   collide with a future Postgres class): P0002 = tenant_id missing/blank/malformed, P0003 =
--   full_name missing/blank. Diagnosable from GoTrue / .NET logs.
--
-- ROLLBACK (manual, explicit — do NOT rely on CASCADE; same order as the checklist, the two MUST agree):
--   1. Disable the hook in the Supabase dashboard FIRST (Authentication -> Hooks) — stops GoTrue
--      calling the 0005 hook before any function is dropped.
--   2. DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;   -- drop the trigger BEFORE its function
--   3. DROP FUNCTION IF EXISTS access.handle_new_user();            -- then the function

CREATE OR REPLACE FUNCTION access.handle_new_user()
    RETURNS trigger
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = ''
AS $$
DECLARE
    v_tenant_text text;
    v_full_name   text;
    v_function    text;
    v_tenant_id   uuid;
BEGIN
    v_tenant_text := pg_catalog.btrim(coalesce(NEW.raw_app_meta_data  ->> 'tenant_id', ''));
    v_full_name   := pg_catalog.btrim(coalesce(NEW.raw_user_meta_data ->> 'full_name', ''));
    v_function    := NULLIF(pg_catalog.btrim(coalesce(NEW.raw_user_meta_data ->> 'function', '')), '');

    -- tenant_id: present, non-blank, and a well-formed uuid. Server-set (raw_app_meta_data).
    IF v_tenant_text = '' THEN
        RAISE EXCEPTION USING
            MESSAGE = 'access.handle_new_user: tenant_id is missing or blank in raw_app_meta_data.',
            ERRCODE = 'P0002';
    END IF;

    BEGIN
        v_tenant_id := v_tenant_text::uuid;
    EXCEPTION WHEN others THEN
        RAISE EXCEPTION USING
            MESSAGE = 'access.handle_new_user: tenant_id is not a valid uuid.',
            ERRCODE = 'P0002';
    END;

    -- full_name: the mandatory observer identity (the one functional rule allowed here).
    IF v_full_name = '' THEN
        RAISE EXCEPTION USING
            MESSAGE = 'access.handle_new_user: full_name is missing or blank in raw_user_meta_data.',
            ERRCODE = 'P0003';
    END IF;

    -- AFTER INSERT runs INSIDE the inserting transaction: any RAISE above rolls the signup back, so
    -- there is no window where an auth.users row exists without its access.profiles projection.
    INSERT INTO access.profiles (user_id, tenant_id, full_name, function, created_at)
    VALUES (NEW.id, v_tenant_id, v_full_name, v_function, pg_catalog.now());

    RETURN NEW;  -- ignored by AFTER triggers; returned by convention.
END;
$$;

CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW
    EXECUTE FUNCTION access.handle_new_user();

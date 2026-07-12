-- 0006_access_profiles_provisioning_fix.sql
-- Owner: DbUp (ADR-ARCH-006). Applied by tools/database-migrator with the privileged owner
-- (postgres) role, which therefore OWNS the two functions below.
--
-- AUTH-COUPLED script (conventions/sql.md §Auth-coupled scripts): it drops/creates triggers ON the
-- GoTrue table auth.users and reasons about the supabase_auth_admin role, neither present on bare
-- Postgres. EXCLUDED from the anti-drift Testcontainer run; validated only against a real Supabase
-- project (manual checklist). Fabricating an auth.users to "test" this would only test the mock.
--
-- WHAT THIS FIXES (see ADR-ARCH-007 — read it before touching this file):
--   0004's on_auth_user_created was AFTER INSERT ON auth.users and read
--   NEW.raw_app_meta_data->>'tenant_id', raising P0002 when absent. It raised on EVERY signup,
--   including the nominal Admin-API path. Reason, verified in supabase/auth source
--   (internal/api/admin.go, adminUserCreate): app_metadata is written by an UPDATE that GoTrue
--   issues AFTER the INSERT, inside the SAME transaction. An AFTER INSERT row trigger runs before
--   that UPDATE, so it can NEVER see the tenant_id. The transaction is atomic, but at INSERT time
--   the data the trigger needs does not exist yet. ADR-ARCH-007 records the fact and its proof.
--
-- THE FIX — split the two responsibilities 0004 wrongly fused into one AFTER INSERT trigger:
--   1. PROVISION (access.provision_profile) reacts to the GoTrue lifecycle. It fires on INSERT AND
--      on UPDATE OF raw_app_meta_data, and does nothing until the tenant_id is actually present.
--      Listening to UPDATE is the whole point: the tenant_id arrives by GoTrue's post-INSERT UPDATE
--      (ADR-ARCH-007), and that UPDATE fires this trigger again, this time with the data present.
--   2. GUARD (access.assert_profile_provisioned) enforces the V1 invariant "every auth user has a
--      profile" as a CONSTRAINT TRIGGER DEFERRABLE INITIALLY DEFERRED. A deferred constraint trigger
--      fires at COMMIT — AFTER GoTrue's app_metadata UPDATE, hence after provisioning has had its
--      chance. It depends only on NEW.id (a value that never changes), so unlike 0004 it can never
--      read a value that is merely stale-at-this-instant. That was 0004's exact mistake.
--
-- V1 PRODUCT CONSTRAINT, UNCHANGED (see 0004 header, ADR-ARCH-005, session 014): every human user of
--   V1 files constats and a constat requires an observer identity, so a user with no profile is
--   rejected. What changes is WHEN and by WHICH object the rejection happens, not WHETHER it happens.
--   A signup with no server-set tenant_id (Dashboard "Add user", OAuth first login, magic-link/OTP,
--   anonymous) still fails — now with P0002 at COMMIT (the guard), not at INSERT. Must be revisited
--   if enterprise SSO/OIDC or service accounts arrive; nobody should read this rule as immutable.
--
-- ROLLBACK (manual, explicit — do NOT rely on CASCADE). The authoritative full sequence is the
-- checklist §9; this block MUST agree with it (checklist §9 drops the 0005 hook function too — step 6).
--   1. Disable the hook in the Supabase dashboard FIRST (Authentication -> Hooks). This is required
--      specifically for step 6 — dropping the 0005 hook FUNCTION while it is still registered makes
--      GoTrue call a missing function and fails EVERY login harder, mid-incident. It is NOT a
--      dependency of steps 2-5: provision_profile / assert_profile_provisioned are trigger functions
--      GoTrue never calls directly, and the hook reads only the access.profiles TABLE (plus 0003's
--      grant/policy), not these functions. Kept as step 1 anyway so there is ONE fixed rollback order.
--   2. DROP TRIGGER IF EXISTS on_auth_user_provisioned ON auth.users;
--   3. DROP TRIGGER IF EXISTS on_auth_user_profile_required ON auth.users;
--   4. DROP FUNCTION IF EXISTS access.provision_profile();
--   5. DROP FUNCTION IF EXISTS access.assert_profile_provisioned();
--   6. To FULLY revert #39: DROP FUNCTION IF EXISTS access.custom_access_token_hook(jsonb); and, if
--      abandoning the feature, access.profiles with its grant/policy. This is the drop that step 1's
--      "disable the hook first" actually protects.
--   (Reverting to 0004's design is NOT a rollback — 0004 is the bug. It was dropped by section (a)
--    below and must not be recreated.)

-- ---------------------------------------------------------------------------------------------------
-- a. Clean up 0004's broken objects. IF EXISTS so the script is safe whether or not 0004 ran on this
--    target (on real Supabase 0004 is applied immediately before 0006 in the same migrator run; the
--    broken trigger therefore never gets a chance to reject a real signup).
-- ---------------------------------------------------------------------------------------------------
DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;
DROP FUNCTION IF EXISTS access.handle_new_user();

-- ---------------------------------------------------------------------------------------------------
-- b. access.provision_profile — projects the minimal business identity into access.profiles.
--
--    SECURITY DEFINER (owned by postgres) + SET search_path = '' is MANDATORY hardening, identical to
--    0004's rationale: the writer at signup is supabase_auth_admin, which has NO write grant on
--    access.profiles; running the body as the postgres owner lets the INSERT succeed and bypass RLS
--    by ownership, without ever handing GoTrue a write grant on our table. A definer function with a
--    mutable search_path is hijackable, so every reference is schema-qualified (access.profiles,
--    pg_catalog.btrim, pg_catalog.now). search_path = '' is the module convention (0002-0005, validated
--    by the security review); it is NOT replaced by pg_catalog.
--
--    The tenant_id comes from raw_app_meta_data (server-set; GoTrue does not let a client write it) —
--    NEVER from raw_user_meta_data, which the user controls (reading the tenant from there would be a
--    cross-tenant escalation). full_name and function come from raw_user_meta_data (the user's own
--    signup payload, acceptable like any registration form).
-- ---------------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION access.provision_profile()
    RETURNS trigger
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = ''
AS $$
DECLARE
    v_tenant_text text;
    v_full_name   text;
    v_function    text;
BEGIN
    -- Guard 1 (FIRST, and structural): a profile already exists for this user -> no-op. This fires on
    -- both INSERT and UPDATE OF raw_app_meta_data, so a later app_metadata change (OAuth identity
    -- linking, an admin metadata edit) re-invokes this function; the existence test makes those a
    -- no-op. access.profiles is NOT re-synced from GoTrue — the identity is captured ONCE, when the
    -- user first becomes product-eligible. Revalidating historical data on every GoTrue sync was
    -- exactly 0004's error, transposed. Idempotence lives here, before anything else runs.
    IF EXISTS (SELECT 1 FROM access.profiles WHERE user_id = NEW.id) THEN
        RETURN NEW;
    END IF;

    v_tenant_text := pg_catalog.btrim(coalesce(NEW.raw_app_meta_data ->> 'tenant_id', ''));

    -- Guard 2: no tenant_id yet. This is the NORMAL state at INSERT time, NOT an error: GoTrue writes
    -- app_metadata by an UPDATE issued after the INSERT (ADR-ARCH-007). We simply wait — that UPDATE
    -- fires this same trigger again (AFTER UPDATE OF raw_app_meta_data), and by then tenant_id is set.
    -- Raising here would reject every signup, which is precisely the 0004 bug.
    IF v_tenant_text = '' THEN
        RETURN NEW;
    END IF;

    v_full_name := pg_catalog.btrim(coalesce(NEW.raw_user_meta_data ->> 'full_name', ''));
    v_function  := NULLIF(pg_catalog.btrim(coalesce(NEW.raw_user_meta_data ->> 'function', '')), '');

    -- Guard 3: tenant_id is present, so we ARE provisioning for real -> the observer identity is
    -- mandatory (the one functional rule allowed at this boundary; ADR-ARCH-005). P0003, stable
    -- message prefixed with the function name so it is diagnosable from GoTrue / .NET logs.
    IF v_full_name = '' THEN
        RAISE EXCEPTION USING
            MESSAGE = 'access.provision_profile: full_name is missing or blank in raw_user_meta_data.',
            ERRCODE = 'P0003';
    END IF;

    -- Guard 4: project the identity. ON CONFLICT (user_id) DO NOTHING, NEVER DO UPDATE — two reasons:
    --   * concurrency/idempotence backstop for Guard 1 (a second lifecycle event racing in).
    --   * access.profiles is NOT a mirror of GoTrue: it is the business identity captured at the moment
    --     the user becomes product-eligible. A rename or a tenant change is an explicit business
    --     operation, never a silent side effect of a GoTrue app_metadata update. DO UPDATE would turn
    --     every future metadata write into an unaudited overwrite of the captured identity.
    -- v_tenant_text::uuid casts at write time; a malformed server-set tenant_id (a provisioning bug)
    -- raises 22P02 and rolls the signup back, which is the correct loud failure for that case.
    INSERT INTO access.profiles (user_id, tenant_id, full_name, function, created_at)
    VALUES (NEW.id, v_tenant_text::uuid, v_full_name, v_function, pg_catalog.now())
    ON CONFLICT (user_id) DO NOTHING;

    RETURN NEW;  -- ignored by AFTER triggers; returned by convention.
END;
$$;

-- ---------------------------------------------------------------------------------------------------
-- c. access.assert_profile_provisioned — the deferred invariant guard. Fired by a CONSTRAINT TRIGGER
--    DEFERRABLE INITIALLY DEFERRED, so it runs at COMMIT, after GoTrue's post-INSERT app_metadata
--    UPDATE and therefore after access.provision_profile has had its chance to create the row.
--
--    SECURITY DEFINER is a deliberate decision, not a reflex. Under the constraint trigger the current
--    role is supabase_auth_admin, which today sees access.profiles only through the 0003 policy
--    USING (true). If that policy is ever hardened with a tenant_id predicate, an INVOKER function
--    would read ZERO ROWS and would then block EVERY user creation (the guard would always fire). A
--    hard invariant must not depend on a mutable RLS policy, so this runs as the postgres owner (which
--    bypasses RLS). The body is a bare existence test on a primary key: no parameters, no dynamic SQL,
--    zero attack surface. It reads access.profiles ONLY — it never touches auth.users.
--
--    RAISE is safe HERE (unlike the 0005 hook, which must never raise): a raise from this trigger
--    aborts one signup transaction; it does not break existing users' logins.
-- ---------------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION access.assert_profile_provisioned()
    RETURNS trigger
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = ''
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM access.profiles WHERE user_id = NEW.id) THEN
        RAISE EXCEPTION USING
            MESSAGE = 'access.assert_profile_provisioned: no access.profiles row for auth user. '
                   || 'Probable cause: app_metadata.tenant_id was absent when the user was created, so '
                   || 'provisioning was skipped by design. V1 users are created ONLY via the Admin API '
                   || 'with a server-set app_metadata.tenant_id (Dashboard "Add user", OAuth, '
                   || 'magic-link and anonymous sign-up carry none and are rejected here on purpose).',
            ERRCODE = 'P0002';
    END IF;
    RETURN NULL;  -- AFTER (constraint) trigger: return value is ignored.
END;
$$;

-- ---------------------------------------------------------------------------------------------------
-- d. The two triggers.
--    on_auth_user_provisioned reacts to the lifecycle: INSERT (creates nothing yet — no tenant) plus
--    UPDATE OF raw_app_meta_data (the event that actually carries the tenant_id — ADR-ARCH-007).
--    on_auth_user_profile_required is the deferred guard: AFTER INSERT only, evaluated at COMMIT.
--    DROP ... IF EXISTS precedes each CREATE so the script is re-runnable by hand: a partial-failure
--    re-application during manual validation (this auth-coupled script is applied outside DbUp's
--    journaled once-only run) does not trip on "trigger already exists".
-- ---------------------------------------------------------------------------------------------------
DROP TRIGGER IF EXISTS on_auth_user_provisioned ON auth.users;
CREATE TRIGGER on_auth_user_provisioned
    AFTER INSERT OR UPDATE OF raw_app_meta_data ON auth.users
    FOR EACH ROW
    EXECUTE FUNCTION access.provision_profile();

DROP TRIGGER IF EXISTS on_auth_user_profile_required ON auth.users;
CREATE CONSTRAINT TRIGGER on_auth_user_profile_required
    AFTER INSERT ON auth.users
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    EXECUTE FUNCTION access.assert_profile_provisioned();

-- ---------------------------------------------------------------------------------------------------
-- e. Grants — NONE required, verified against 0003 and the PostgreSQL docs (do not add any):
--    * 0003 already grants supabase_auth_admin USAGE ON SCHEMA access + SELECT ON access.profiles for
--      the 0005 hook's read path; nothing here needs more.
--    * A trigger function needs EXECUTE granted to the role that CREATES the trigger (here postgres,
--      which owns both functions) — NOT to the role that causes the trigger to fire. So
--      supabase_auth_admin (which performs the signup INSERT/UPDATE) needs no EXECUTE grant, exactly
--      as 0004's handle_new_user had none.
--    * Both functions are SECURITY DEFINER owned by postgres, so their bodies read/write
--      access.profiles as the owner (bypassing RLS by ownership) and need no per-role table grant.
-- ---------------------------------------------------------------------------------------------------

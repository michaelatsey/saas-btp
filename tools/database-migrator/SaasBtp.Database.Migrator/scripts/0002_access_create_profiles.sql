-- 0002_access_create_profiles.sql
-- Owner: DbUp (ADR-ARCH-006). DbUp is the single authority for this schema; EF Core maps schemas it
-- owns, never creates them (access.profiles has no EF mapping today). Applied by
-- tools/database-migrator with the privileged owner role.
--
-- PORTABLE script: references NO GoTrue role or table, so the anti-drift Testcontainer harness runs
-- it on bare Postgres. The auth-coupled objects (the supabase_auth_admin SELECT policy, the signup
-- trigger on auth.users, the JWT hook) live in 0003/0004/0005, which are EXCLUDED from that harness
-- (conventions/sql.md §Auth-coupled scripts).
--
-- WHAT: access.profiles is the MINIMAL business identity projected for every human user — the
-- observer_name printed on a constat plus an optional function, scoped by tenant_id. It is NOT a
-- user-management table: no roles, no membership, no permissions, no preferences surface.
--
-- NO FOREIGN KEY on user_id -> auth.users(id) — deliberate, same rationale as 0001:
--   * app DDL never references the GoTrue (auth) schema; that boundary is owned by Supabase Auth,
--     not by our migrations (ADR-ARCH-005 auth-boundary exception; conventions/sql.md).
--   * user_id EQUALS auth.users.id (the row is provisioned from the signup event in 0004), but that
--     link is enforced by the trigger on the write path, not by a cross-boundary FK.
--
-- Insert-only projection: no updated_at / deleted_at / version (sql.md §Audit, §Optimistic
-- concurrency). created_at is stamped server-side by the trigger (pg_catalog.now() in 0004),
-- consistent with the "created_at NOT NULL, server-stamped" rule.

-- First table of the Access context's DB surface: create its schema just-in-time (sql.md §Casing & naming).
CREATE SCHEMA IF NOT EXISTS access;

CREATE TABLE access.profiles (
    user_id     uuid          NOT NULL,
    tenant_id   uuid          NOT NULL,
    full_name   varchar(200)  NOT NULL,
    function    varchar(150)  NULL,
    created_at  timestamptz   NOT NULL,

    -- user_id is the identity (equals auth.users.id); exactly one profile per user.
    CONSTRAINT pk_profiles PRIMARY KEY (user_id)
);

-- No CHECK on full_name non-blank: CHECKs here serve closed sets (sql.md §Closed sets), and this
-- table has none. full_name non-blank is a signup-time rule enforced by the trigger's RAISE (0004);
-- NOT NULL is the DB-level guarantee, consistent with Safety (0001). "function" is a non-reserved
-- PostgreSQL keyword and is legal unqualified as a column name; the trigger/hook always reference it
-- through a table alias.

-- RLS enabled now. Unlike safety.constats (RLS on, NO policy — it has no non-privileged consumer),
-- access.profiles HAS a non-privileged consumer: the JWT hook (0005), which runs as
-- supabase_auth_admin — a role that does NOT bypass RLS. With RLS enabled and no policy that role
-- would read ZERO ROWS silently, and the token would ship without observer_name. The single
-- FOR SELECT TO supabase_auth_admin policy that makes the read non-empty lives in 0003 (it names a
-- GoTrue role absent from bare Postgres, so it cannot live in this portable script).
ALTER TABLE access.profiles ENABLE ROW LEVEL SECURITY;

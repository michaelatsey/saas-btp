-- 0002_access_identity_model.sql
-- Owner: DbUp (ADR-ARCH-006). DbUp is the single authority for this schema; EF Core maps schemas it
-- owns, never creates them (access.* has no EF mapping today). Applied by tools/database-migrator with
-- the privileged owner role.
--
-- PORTABLE script: references NO GoTrue role or table, so the anti-drift Testcontainer harness runs it
-- on bare Postgres (conventions/sql.md §Auth-coupled scripts). The auth-coupled objects (the
-- supabase_auth_admin grant + SELECT policy, and the JWT hook) live in 0003, EXCLUDED from that harness.
--
-- THE MODEL (identity model, ADR-ARCH-008). Three concepts, kept separate:
--   * access.profiles is the pure 1:1 human projection of auth.users: WHO the person is. It carries NO
--     tenant and NO role. Identity is not authorization.
--   * access.tenants + access.memberships are the authorization bridge: "user U belongs to tenant T
--     with role R", as ROWS. A user's tenants and roles are data queried per request, never JWT claims,
--     so removing someone (memberships.is_active = false) takes effect on the next request, not at token
--     expiry.
--   * There is NO trigger on auth.users. Onboarding is an explicit application operation:
--     InviteMemberCommand (#48) creates the profile + membership. A trigger cannot tell "create a new
--     company" from "join an existing one" without a client-supplied flag, and branching provisioning on
--     a client flag IS the role-escalation path — so it does not belong at the auth boundary
--     (ADR-ARCH-008). This is a squash/rebaseline of the earlier tenant-in-profile design: the table is
--     empty and the app is not deployed, so the final shape is DECLARED here directly rather than
--     migrated forward.

-- First table of the Access context's DB surface: create its schema just-in-time (sql.md §Casing & naming).
CREATE SCHEMA IF NOT EXISTS access;

-- ---------------------------------------------------------------------------------------------------
-- access.profiles — the 1:1 human projection of auth.users. Identity only.
-- ---------------------------------------------------------------------------------------------------
CREATE TABLE access.profiles (
    user_id      uuid          NOT NULL,
    email        varchar(255)  NOT NULL,
    full_name    varchar(200)  NOT NULL,
    given_name   varchar(200)  NULL,
    family_name  varchar(200)  NULL,
    job_function varchar(150)  NULL,
    created_at   timestamptz   NOT NULL,

    -- user_id is the identity (equals auth.users.id); exactly one profile per user. NO FK to
    -- auth.users(id): app DDL never crosses the auth boundary with a foreign key (ADR-ARCH-005; the
    -- boundary is owned by Supabase Auth, not by our migrations). The link is enforced on the write
    -- path (InviteMemberCommand, #48), not by a cross-boundary constraint.
    CONSTRAINT pk_profiles PRIMARY KEY (user_id),

    -- email is a SNAPSHOT taken at invitation. auth.users.email stays AUTHORITATIVE; GoTrue permits an
    -- email change and NOTHING resynchronises this column, so it CAN drift. It is stored anyway for ONE
    -- consumer: the tenant member-list read path (memberships -> profiles) cannot reach auth.users, so
    -- without this column listing a tenant's members would need one GoTrue Admin API call per member.
    -- That is the entire justification. Nothing else reads it — the JWT already carries `email` natively.
    --
    -- ux_profiles__email does NOT prevent that drift and must not be read as if it did. It prevents a
    -- purely LOCAL fault: two profile rows claiming the same e-mail identity. A local invariant, nothing
    -- more. (GoTrue separately enforces email uniqueness on auth.users.)
    CONSTRAINT ux_profiles__email UNIQUE (email)
);

-- full_name is ENTERED, never a concatenation of given_name/family_name: the one-given-name +
-- one-family-name assumption is false for many users (e.g. Ivorian names). OIDC treats name /
-- given_name / family_name as three INDEPENDENT claims for exactly this reason, so full_name is
-- NOT NULL and authored directly, while given_name/family_name are optional structured extras.
--
-- created_at is NOT NULL with NO default: the .NET domain stamps it (ADR-ARCH-005), exactly as
-- safety.constats does. A DB default (now()) would MASK a domain that forgot to stamp it, instead of
-- failing loudly at INSERT. There is no writer at the DB level anymore (no trigger).

-- RLS enabled now. access.profiles HAS a non-privileged consumer: the JWT hook (0003), which runs as
-- supabase_auth_admin — a role that does NOT bypass RLS. Enabling RLS is portable; the single
-- FOR SELECT policy that makes that read non-empty names a GoTrue role and therefore lives in the
-- auth-coupled 0003, not here.
ALTER TABLE access.profiles ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------------------------------
-- access.tenants — the company/organization a person can belong to. The tenant is product data (a row),
-- not a JWT claim. Owned by the Access context.
-- ---------------------------------------------------------------------------------------------------
CREATE TABLE access.tenants (
    id          uuid          NOT NULL,
    name        varchar(200)  NOT NULL,
    status      varchar(20)   NOT NULL,
    created_at  timestamptz   NOT NULL,

    -- id is a UUIDv7 generated by the domain at creation (naming.md); NO DB default. created_at is
    -- NOT NULL with NO default — domain-stamped (ADR-ARCH-005), like safety.constats.
    CONSTRAINT pk_tenants PRIMARY KEY (id),

    -- Closed set stored as a lowercase snake_case token + CHECK (sql.md §Closed sets); the domain
    -- Value Object is the source of truth, this CHECK is defense-in-depth. Only active/suspended:
    -- 'suspended' freezes a tenant without deleting it. NO trial/past_due — billing is out of scope.
    CONSTRAINT ck_tenants__status CHECK (status IN ('active', 'suspended'))
);

-- RLS enabled, NO policy: the owner (migrator/app) bypasses RLS and is the only consumer today; the
-- non-owner read policy (PowerSync) is #50. RLS-on/no-policy denies every non-owner role by default
-- (sql.md §RLS) — correct for a table with no non-privileged consumer yet.
ALTER TABLE access.tenants ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------------------------------
-- access.memberships — the authorization bridge: "user U belongs to tenant T with role R". THE only
-- place a user's tenants and roles live (never the JWT). is_active makes revocation immediate.
-- ---------------------------------------------------------------------------------------------------
CREATE TABLE access.memberships (
    id          uuid          NOT NULL,
    tenant_id   uuid          NOT NULL,
    user_id     uuid          NOT NULL,
    role        varchar(20)   NOT NULL,
    is_active   boolean       NOT NULL,
    created_at  timestamptz   NOT NULL,

    -- id + created_at: UUIDv7 + domain-stamped, NO defaults (as tenants/constats). is_active NOT NULL,
    -- NO default — the domain sets it explicitly at creation; an implicit default would hide the choice.
    CONSTRAINT pk_memberships PRIMARY KEY (id),

    -- Intra-context FKs (both targets live in schema access), so the cross-bounded-context / auth FK
    -- ban (0001) does NOT apply: these are ordinary references INSIDE one context and are allowed. NO
    -- CASCADE — a membership is insert-then-deactivate (is_active), never hard-deleted as a DB side
    -- effect of tenant/user loss; lifecycle is a deliberate domain operation (ADR-ARCH-005).
    CONSTRAINT fk_memberships__tenants
        FOREIGN KEY (tenant_id) REFERENCES access.tenants (id),
    CONSTRAINT fk_memberships__profiles
        FOREIGN KEY (user_id) REFERENCES access.profiles (user_id),

    -- Closed set: the role within the tenant. Lowercase snake_case token + CHECK (sql.md §Closed sets);
    -- the domain VO is the source of truth. owner/admin/member is a product hierarchy, not a
    -- DB-enforced order.
    CONSTRAINT ck_memberships__role CHECK (role IN ('owner', 'admin', 'member')),

    -- One membership per (tenant, user): a user holds at most one role in a given tenant. This unique
    -- index's B-tree also answers tenant-scoped lookups by its LEFT PREFIX (tenant_id).
    CONSTRAINT ux_memberships__tenant_id_user_id UNIQUE (tenant_id, user_id)
);

-- The user-first lookup index. This is the PowerSync parameter-query path
-- (WHERE user_id = request.user_id() -> "which tenants/roles does this user have?"): NO existing
-- index's left prefix is user_id (the unique index above leads with tenant_id), so it needs its own.
CREATE INDEX ix_memberships__user_id ON access.memberships (user_id);

-- Deliberately NO ix_memberships__tenant_id: it would be redundant. Postgres answers
-- WHERE tenant_id = ? from the LEFT PREFIX of ux_memberships__tenant_id_user_id (tenant_id, user_id),
-- so a standalone tenant_id index would only duplicate that prefix and slow writes for no read gain.

-- RLS enabled, NO policy (same rationale as access.tenants above; the non-owner policy is #50).
ALTER TABLE access.memberships ENABLE ROW LEVEL SECURITY;

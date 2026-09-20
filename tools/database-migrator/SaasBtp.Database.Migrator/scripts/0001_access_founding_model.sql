-- 0001_access_founding_model.sql
-- Owner: DbUp (ADR-ARCH-006). DbUp is the single authority for this schema; EF Core maps schemas it
-- owns, never creates them. Applied by tools/database-migrator with the privileged owner role.
--
-- PORTABLE script: references NO GoTrue role or table, so the anti-drift Testcontainer harness runs it
-- on bare Postgres (conventions/sql.md §Auth-coupled scripts). BP-001 needs NO auth-coupled companion:
-- the identity projection (access.profiles) is written by the .NET founding handler over the owner
-- connection, and the JWT-hook / observer read path belongs to the (dormant) login/Safety flow, out of
-- BP-001's boundary. So AuthCoupledScriptMarkers is empty for this baseline (MigrationRunner).
--
-- THE MODEL — BP-001 "Créer son espace entreprise" (docs/business-processes/BP-001). The founding
-- process produces, atomically, ONE identity projection plus FIVE business facts on THREE separate axes
-- that are never conflated (Design Package §0):
--   * access.profiles              — the 1:1 human projection of auth.users (INFRASTRUCTURE, not a
--                                     business fact). Identity is neither belonging nor authorization.
--   * access.organizations         — the client company (Status = Declared at founding).
--   * access.workspaces            — an organization's autonomous operational space (Organization 1:N
--                                     Workspace). BP-001 creates the FIRST one.
--   * access.ownerships            — Ownership (property): "to whom the organization belongs".
--   * access.organization_memberships — Organization Membership (belonging): "who is part of the
--                                     organization's collective". NOT authorization.
--   * access.workspace_access      — Workspace Access (authorization): "may act in this workspace".
--                                     The single access edge BP-001 produces (ADR-ARCH-013).
-- None of Ownership / Membership / Access derives from another (member_of -> access and owns -> access
-- are forbidden model rules). This is a clean-slate baseline: the abandoned identity model's scripts
-- (0001-0005) were deliberately removed; the final shape is DECLARED here directly.

-- First table of the Access context's DB surface: create its schema just-in-time (sql.md §Casing & naming).
CREATE SCHEMA IF NOT EXISTS access;

-- ---------------------------------------------------------------------------------------------------
-- access.profiles — the 1:1 human projection of auth.users. Identity only (infrastructure, not a
-- business fact). The three relations below attach the person THROUGH this projection.
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
    -- boundary is owned by Supabase Auth, not by our migrations). The link is upheld on the write path
    -- (the founding handler projects the createUser/adoption result), not by a cross-boundary constraint.
    CONSTRAINT pk_profiles PRIMARY KEY (user_id),

    -- email is stored NORMALIZED (lowercase, trimmed) by the domain (ADR-ARCH-011). ux_profiles__email
    -- is a LOCAL invariant: it stops two profile rows claiming the same e-mail identity. It does not
    -- resynchronise auth.users (nothing does; the snapshot may drift, accepted).
    CONSTRAINT ux_profiles__email UNIQUE (email)
);

-- created_at is NOT NULL with NO default: the .NET domain stamps it (ADR-ARCH-005), exactly as
-- safety.constats does. A DB default (now()) would MASK a domain that forgot to stamp it instead of
-- failing loudly at INSERT. There is no DB-level writer (no trigger).

-- RLS enabled on every business table (sql.md §RLS). The owner/app connection bypasses RLS; RLS is
-- defense-in-depth for any other path. The FOR SELECT policy that a future non-privileged consumer
-- (the JWT hook read path) needs names a GoTrue role and is therefore auth-coupled — out of BP-001.
ALTER TABLE access.profiles ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------------------------------
-- access.organizations — the client company. BP-001 creates it Declared (INV-1); Verified is a future,
-- out-of-boundary verification process (Design Package §Non Goals).
-- ---------------------------------------------------------------------------------------------------
CREATE TABLE access.organizations (
    id           uuid          NOT NULL,
    display_name varchar(200)  NOT NULL,
    status       varchar(32)   NOT NULL,
    created_at   timestamptz   NOT NULL,

    -- id is a UUIDv7 generated by the domain at creation (naming.md); NO DB default. created_at is
    -- NOT NULL with NO default — domain-stamped (ADR-ARCH-005).
    CONSTRAINT pk_organizations PRIMARY KEY (id),

    -- Closed set as a lowercase snake_case token + CHECK (sql.md §Closed sets); the domain Value Object
    -- (OrganizationStatus) is the source of truth, the CHECK is defense-in-depth. Only 'declared' is
    -- admitted now: 'verified' is a FUTURE process with no writer yet, so adding it here would be
    -- speculative (sql.md §Audit/lifecycle) — the verification process amends this CHECK when it lands.
    CONSTRAINT ck_organizations__status CHECK (status IN ('declared'))
);

ALTER TABLE access.organizations ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------------------------------
-- access.workspaces — an organization's autonomous operational space (Organization 1:N Workspace, a
-- BTP domain fact). BP-001 creates the FIRST workspace; further workspaces are a different process.
-- ---------------------------------------------------------------------------------------------------
CREATE TABLE access.workspaces (
    id              uuid          NOT NULL,
    organization_id uuid          NOT NULL,
    display_name    varchar(200)  NOT NULL,
    created_at      timestamptz   NOT NULL,

    CONSTRAINT pk_workspaces PRIMARY KEY (id),

    -- Intra-context FK (both targets live in schema access): an ordinary reference INSIDE one context.
    -- NO CASCADE — an organization is never hard-deleted as a DB side effect (ADR-ARCH-005); deletion
    -- strategy is a deliberate, deferred decision (Design Package §5). Default ON DELETE (RESTRICT)
    -- forbids orphaning a workspace's organization, which suits INV-4.
    CONSTRAINT fk_workspaces__organizations
        FOREIGN KEY (organization_id) REFERENCES access.organizations (id)

    -- Deliberately NO uniqueness on (organization_id, display_name): a workspace's navigation identity
    -- is unique within its organization as a DOMAIN fact, but the FORM of resolution (slug alone,
    -- org/workspace, public UUID, ...) is explicitly left open (Design Package §"Organization and
    -- workspaces"; Implementation Design §3.4 lists no such uniqueness invariant). Not added speculatively.
);

ALTER TABLE access.workspaces ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------------------------------
-- access.ownerships — Ownership (property axis): "this person owns this organization". An AUTONOMOUS
-- persisted relation, never an attribute of Organization or Profile (Implementation Design §3.1).
-- ---------------------------------------------------------------------------------------------------
CREATE TABLE access.ownerships (
    id              uuid         NOT NULL,
    organization_id uuid         NOT NULL,
    user_id         uuid         NOT NULL,
    created_at      timestamptz  NOT NULL,

    CONSTRAINT pk_ownerships PRIMARY KEY (id),

    CONSTRAINT fk_ownerships__organizations
        FOREIGN KEY (organization_id) REFERENCES access.organizations (id),
    CONSTRAINT fk_ownerships__profiles
        FOREIGN KEY (user_id) REFERENCES access.profiles (user_id),

    -- Exactly ONE ownership per organization (INV-2/INV-3: exactly one initial owner). A person MAY own
    -- several organizations over time, so there is deliberately no uniqueness on user_id.
    CONSTRAINT ux_ownerships__organization_id UNIQUE (organization_id)
);

ALTER TABLE access.ownerships ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------------------------------
-- access.organization_memberships — Organization Membership (belonging axis): "this person is part of
-- the organization's collective". OUTSIDE the authorization path — never read as access. An autonomous
-- persisted relation. No revocability form is imposed (no current process produces it; Design Package §5).
-- ---------------------------------------------------------------------------------------------------
CREATE TABLE access.organization_memberships (
    id              uuid         NOT NULL,
    organization_id uuid         NOT NULL,
    user_id         uuid         NOT NULL,
    created_at      timestamptz  NOT NULL,

    CONSTRAINT pk_organization_memberships PRIMARY KEY (id),

    CONSTRAINT fk_organization_memberships__organizations
        FOREIGN KEY (organization_id) REFERENCES access.organizations (id),
    CONSTRAINT fk_organization_memberships__profiles
        FOREIGN KEY (user_id) REFERENCES access.profiles (user_id),

    -- Belonging is binary: one membership fact per (person, organization). Profile-first so the unique
    -- index's left prefix (user_id) also answers the person-centric "which organizations does this user
    -- belong to?" lookup.
    CONSTRAINT ux_organization_memberships__user_id_organization_id UNIQUE (user_id, organization_id)
);

ALTER TABLE access.organization_memberships ENABLE ROW LEVEL SECURITY;

-- ---------------------------------------------------------------------------------------------------
-- access.workspace_access — Workspace Access (authorization axis): "this person may act in this
-- workspace's scope" (ADR-ARCH-013). The SINGLE access edge BP-001 produces. An autonomous persisted
-- relation. Revocable without destroying history is a business need; its FORM (soft flag, temporal
-- window, controlled delete) is deferred (Design Package §5) — so no revocation column is added now.
-- ---------------------------------------------------------------------------------------------------
CREATE TABLE access.workspace_access (
    id           uuid         NOT NULL,
    workspace_id uuid         NOT NULL,
    user_id      uuid         NOT NULL,
    created_at   timestamptz  NOT NULL,

    CONSTRAINT pk_workspace_access PRIMARY KEY (id),

    CONSTRAINT fk_workspace_access__workspaces
        FOREIGN KEY (workspace_id) REFERENCES access.workspaces (id),
    CONSTRAINT fk_workspace_access__profiles
        FOREIGN KEY (user_id) REFERENCES access.profiles (user_id),

    -- "Can act here" is binary: one access fact per (person, workspace). Profile-first: the left prefix
    -- (user_id) is exactly the PowerSync parameter-query path ("which workspaces may this user sync?").
    CONSTRAINT ux_workspace_access__user_id_workspace_id UNIQUE (user_id, workspace_id)
);

-- RLS ENABLED with NO policy, from day one. This is the LOAD-BEARING constraint (ADR-ARCH-013, sql.md
-- §RLS): the moment a non-privileged consumer (PowerSync) reads this edge, RLS-with-no-policy returns
-- ZERO ROWS SILENTLY. Enabling RLS now is portable; the policy CONTENT is deferred to the consuming
-- slice (the C# provider + parameter query + the C#/SQL agreement test all land together). BP-001
-- produces the edge; it does not build its consumers.
ALTER TABLE access.workspace_access ENABLE ROW LEVEL SECURITY;

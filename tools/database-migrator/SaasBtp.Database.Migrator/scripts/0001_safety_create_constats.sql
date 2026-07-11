-- 0001_safety_create_constats.sql
-- Owner: DbUp (ADR-ARCH-006). DbUp is the single authority for this schema; EF Core maps it, never
-- creates it. Applied by tools/database-migrator with the privileged owner role.
--
-- NO FOREIGN KEYS — deliberate:
--   * tenant_id references the tenancy/Access boundary; site_id references the Site bounded context.
--     Cross-bounded-context FKs are forbidden (modular-monolith isolation, safety-domain-model.md
--     §2/§8). Referential integrity for these is a domain/application concern enforced on the write
--     path (ADR-ARCH-005), not by the database.
--   * FK-free also suits offline capture / future PowerSync replication, where a row may arrive
--     before or without the referenced aggregate.
--
-- Insert-only aggregate: no updated_at / deleted_at / version columns (sql.md §Audit, §Optimistic
-- concurrency). created_at is server-stamped by the .NET domain, NOT a DB default (ADR-ARCH-005).

-- First table of the Safety context: create its schema just-in-time (sql.md §Casing & naming).
CREATE SCHEMA IF NOT EXISTS safety;

CREATE TABLE safety.constats (
    id                   uuid          NOT NULL,
    tenant_id            uuid          NOT NULL,
    site_id              uuid          NOT NULL,
    type                 varchar(32)   NOT NULL,
    severity             varchar(10)   NOT NULL,
    observer_user_id     uuid          NOT NULL,
    observer_full_name   varchar(200)  NOT NULL,
    observer_function    varchar(150)  NULL,
    occurred_at          timestamptz   NOT NULL,
    location_description varchar(300)  NOT NULL,
    short_description    varchar(300)  NOT NULL,
    detailed_description text          NULL,
    observations         text          NULL,
    created_at           timestamptz   NOT NULL,

    CONSTRAINT pk_constats PRIMARY KEY (id),

    -- Closed sets stored as lowercase snake_case tokens + CHECK (sql.md §Closed sets). The domain
    -- Value Object is the source of truth; these CHECKs are defense-in-depth against any write path
    -- that bypasses the domain. varchar(32) on type leaves margin over the longest token
    -- 'dangerous_situation' (19 chars).
    CONSTRAINT ck_constats__type
        CHECK (type IN ('incident', 'accident', 'dangerous_situation')),
    CONSTRAINT ck_constats__severity
        CHECK (severity IN ('minor', 'major', 'critical'))
);

-- RLS enabled now; the tenant-isolation policy is deliberately deferred to a later story (there is
-- NO policy yet). With RLS enabled and no policy, every non-owner role is denied by default; the
-- migrator/app owner role bypasses RLS by design (sql.md §RLS, ADR-ARCH-005). Defense-in-depth for
-- any future non-owner access path (PostgREST, PowerSync).
ALTER TABLE safety.constats ENABLE ROW LEVEL SECURITY;

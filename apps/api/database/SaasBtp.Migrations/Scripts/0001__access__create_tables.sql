-- The Access tables (contract.md BP-001 §5, §7; ADR-ARCH-031, ADR-ARCH-033 §5, §6), with row
-- level security enabled and forced (ADR-ARCH-027 §3) and the grants saasbtp_api needs
-- (ADR-ARCH-028 §2). The policies come in 0003; until then the tables admit no row.
--
-- Every table maps an aggregate root, auditable (sql.md, Audit / lifecycle): created_at and
-- created_by set at creation, updated_at and updated_by null until the first modification, and
-- the aggregate version (sql.md, Optimistic concurrency). No column has a database default: the
-- API sets every value before the write. created_by and updated_by hold an Actor, at most 200
-- characters (MicroKit.Domain, Actor.MaxLength). A name holds at most 200 Unicode scalar values
-- (BP001-R7); varchar counts characters, which in UTF-8 are scalar values.

create schema if not exists access;

create table access.profiles (
  id uuid not null,
  -- The identity provider's identifier of the person (ADR-ARCH-031 §2).
  user_id uuid not null,
  created_at timestamptz not null,
  created_by varchar(200) not null,
  updated_at timestamptz,
  updated_by varchar(200),
  aggregate_version bigint not null,
  constraint pk_profiles primary key (id),
  -- INV-ACCESS-01: one profile per person.
  constraint ux_profiles__user_id unique (user_id)
);

create table access.organizations (
  id uuid not null,
  -- No unique key: two founders may declare the same company name (BP001-R5).
  name varchar(200) not null,
  -- The longest token is 8 characters; a longer one is added by a script that widens the column.
  status varchar(8) not null,
  current_ownership_id uuid not null,
  created_at timestamptz not null,
  created_by varchar(200) not null,
  updated_at timestamptz,
  updated_by varchar(200),
  aggregate_version bigint not null,
  constraint pk_organizations primary key (id),
  constraint ck_organizations__status check (status in ('declared', 'verified'))
);

create table access.ownerships (
  id uuid not null,
  profile_id uuid not null,
  organization_id uuid not null,
  created_at timestamptz not null,
  created_by varchar(200) not null,
  updated_at timestamptz,
  updated_by varchar(200),
  aggregate_version bigint not null,
  constraint pk_ownerships primary key (id),
  -- The target of the organization's composite key to its current ownership (ADR-ARCH-033 §6).
  constraint ux_ownerships__organization_id_id unique (organization_id, id),
  constraint fk_ownerships__profiles foreign key (profile_id) references access.profiles (id),
  -- Deferred: the ownership is inserted before its organization, so that the organization is
  -- visible to its founder when it is read back (ADR-ARCH-033 §5).
  constraint fk_ownerships__organizations foreign key (organization_id) references access.organizations (id)
    deferrable initially deferred
);

-- INV-ACCESS-02: exactly one current owner. The organization cannot be committed without an
-- ownership of its own; an ownership no organization references makes no one an owner
-- (ADR-ARCH-033 §6).
alter table access.organizations
  add constraint fk_organizations__ownerships foreign key (id, current_ownership_id)
    references access.ownerships (organization_id, id)
    deferrable initially deferred;

create table access.memberships (
  id uuid not null,
  profile_id uuid not null,
  organization_id uuid not null,
  -- Null while the membership is current; no process of BP-001 sets it (contract.md §5).
  ended_at timestamptz,
  created_at timestamptz not null,
  created_by varchar(200) not null,
  updated_at timestamptz,
  updated_by varchar(200),
  aggregate_version bigint not null,
  constraint pk_memberships primary key (id),
  constraint fk_memberships__profiles foreign key (profile_id) references access.profiles (id),
  constraint fk_memberships__organizations foreign key (organization_id) references access.organizations (id)
);

-- INV-ACCESS-06: one current membership per person and organization.
create unique index ux_memberships__profile_id_organization_id
  on access.memberships (profile_id, organization_id)
  where ended_at is null;

create table access.workspaces (
  id uuid not null,
  -- INV-ACCESS-04: set at creation; no policy changes it.
  organization_id uuid not null,
  name varchar(200) not null,
  created_at timestamptz not null,
  created_by varchar(200) not null,
  updated_at timestamptz,
  updated_by varchar(200),
  aggregate_version bigint not null,
  constraint pk_workspaces primary key (id),
  constraint fk_workspaces__organizations foreign key (organization_id) references access.organizations (id),
  -- INV-ACCESS-04: a name is unique among the workspaces of one organization.
  constraint ux_workspaces__organization_id_name unique (organization_id, name),
  -- The target of the access's composite key (ADR-ARCH-033 §5).
  constraint ux_workspaces__id_organization_id unique (id, organization_id)
);

create table access.workspace_accesses (
  id uuid not null,
  profile_id uuid not null,
  workspace_id uuid not null,
  -- The organization of the workspace (ADR-ARCH-033 §5).
  organization_id uuid not null,
  -- Null while the access is current; an access is ended, never deleted (contract.md §5, §7).
  ended_at timestamptz,
  created_at timestamptz not null,
  created_by varchar(200) not null,
  updated_at timestamptz,
  updated_by varchar(200),
  aggregate_version bigint not null,
  constraint pk_workspace_accesses primary key (id),
  constraint fk_workspace_accesses__profiles foreign key (profile_id) references access.profiles (id),
  -- Deferred: the access is inserted before its workspace, and names a workspace of that
  -- organization (ADR-ARCH-033 §5).
  constraint fk_workspace_accesses__workspaces foreign key (workspace_id, organization_id)
    references access.workspaces (id, organization_id)
    deferrable initially deferred
);

-- INV-ACCESS-07: one current access per person and workspace.
create unique index ux_workspace_accesses__profile_id_workspace_id
  on access.workspace_accesses (profile_id, workspace_id)
  where ended_at is null;

-- The access read by access.may_act_in and the workspace policies, and by synchronization later
-- (ADR-ARCH-033, Consequences).
create index ix_workspace_accesses__profile_id_workspace_id_organization_id
  on access.workspace_accesses (profile_id, workspace_id, organization_id);

alter table access.profiles enable row level security;
alter table access.profiles force row level security;
alter table access.organizations enable row level security;
alter table access.organizations force row level security;
alter table access.ownerships enable row level security;
alter table access.ownerships force row level security;
alter table access.memberships enable row level security;
alter table access.memberships force row level security;
alter table access.workspaces enable row level security;
alter table access.workspaces force row level security;
alter table access.workspace_accesses enable row level security;
alter table access.workspace_accesses force row level security;

-- The commands contract.md §5 gives a policy, and nothing else: no update, no delete.
grant usage on schema access to saasbtp_api;
grant select, insert on
  access.profiles,
  access.organizations,
  access.ownerships,
  access.memberships,
  access.workspaces,
  access.workspace_accesses
  to saasbtp_api;

-- The policies of contract.md BP-001 §5 for saasbtp_api on the Access tables: one per table and
-- command, insert and select only. No update or delete policy: no founded record can be changed
-- or removed through the API until a process adds one.
--
-- Every column of the row under check is written with its table's name, and every table read in a
-- sub-query has an alias that qualifies each of its columns: PostgreSQL binds an unqualified name
-- to the innermost table that has it, which would widen a check silently. The subject is read in
-- a sub-select, so that it is evaluated once per statement (ADR-ARCH-032, Consequences).
--
-- "The subject is the current owner of organization X" is written out in each insert policy that
-- needs it: the organization's current ownership names the subject's profile (ADR-ARCH-032 §4,
-- ADR-ARCH-033 §5, contract.md §3). It is not left to the organizations' select policy, so that a
-- later, wider select policy on organizations does not widen these insert policies.

-- Profiles: the subject's own. They read the setting directly: through current_profile_id they
-- would read the profiles they guard.
create policy profiles__insert on access.profiles
  for insert to saasbtp_api
  with check (profiles.user_id = (select nullif(current_setting('app.user_id', true), '')::uuid));

create policy profiles__select on access.profiles
  for select to saasbtp_api
  using (profiles.user_id = (select nullif(current_setting('app.user_id', true), '')::uuid));

-- Ownerships: naming the subject's profile. The select policy reads no organization, so that the
-- organizations' select policy can read it without recursion.
create policy ownerships__insert on access.ownerships
  for insert to saasbtp_api
  with check (ownerships.profile_id = (select access.current_profile_id()));

create policy ownerships__select on access.ownerships
  for select to saasbtp_api
  using (ownerships.profile_id = (select access.current_profile_id()));

-- Organizations: any row at insert, since none can be committed without an ownership of its own
-- and it grants nothing until its owner reads it through that ownership (ADR-ARCH-033 §5, §6);
-- seen only by its current owner (contract.md §3).
create policy organizations__insert on access.organizations
  for insert to saasbtp_api
  with check (true);

create policy organizations__select on access.organizations
  for select to saasbtp_api
  using (exists (
    select 1
    from access.ownerships o
    where o.id = organizations.current_ownership_id
      and o.organization_id = organizations.id
      and o.profile_id = (select access.current_profile_id())
  ));

-- Memberships: naming the subject, under an organization the subject currently owns.
create policy memberships__insert on access.memberships
  for insert to saasbtp_api
  with check (
    memberships.profile_id = (select access.current_profile_id())
    and exists (
      select 1
      from access.organizations g
      join access.ownerships o on o.id = g.current_ownership_id and o.organization_id = g.id
      where g.id = memberships.organization_id
        and o.profile_id = (select access.current_profile_id())
    )
  );

create policy memberships__select on access.memberships
  for select to saasbtp_api
  using (memberships.profile_id = (select access.current_profile_id()));

-- Workspace accesses: naming the subject, under an organization the subject currently owns. That
-- the founder's access covers the first workspace only is the domain's rule (BP001-R4).
create policy workspace_accesses__insert on access.workspace_accesses
  for insert to saasbtp_api
  with check (
    workspace_accesses.profile_id = (select access.current_profile_id())
    and exists (
      select 1
      from access.organizations g
      join access.ownerships o on o.id = g.current_ownership_id and o.organization_id = g.id
      where g.id = workspace_accesses.organization_id
        and o.profile_id = (select access.current_profile_id())
    )
  );

create policy workspace_accesses__select on access.workspace_accesses
  for select to saasbtp_api
  using (workspace_accesses.profile_id = (select access.current_profile_id()));

-- Workspaces: created under an organization the subject currently owns; seen only through a
-- current access of the subject whose organization is the workspace's own, so that an access
-- naming another organization's workspace grants no sight of it before its deferred key fails
-- (ADR-ARCH-033 §5).
create policy workspaces__insert on access.workspaces
  for insert to saasbtp_api
  with check (exists (
    select 1
    from access.organizations g
    join access.ownerships o on o.id = g.current_ownership_id and o.organization_id = g.id
    where g.id = workspaces.organization_id
      and o.profile_id = (select access.current_profile_id())
  ));

create policy workspaces__select on access.workspaces
  for select to saasbtp_api
  using (exists (
    select 1
    from access.workspace_accesses a
    where a.workspace_id = workspaces.id
      and a.organization_id = workspaces.organization_id
      and a.profile_id = (select access.current_profile_id())
      and a.ended_at is null
  ));

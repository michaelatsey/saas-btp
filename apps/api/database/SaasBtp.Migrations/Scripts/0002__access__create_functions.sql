-- The two functions Access publishes (ADR-ARCH-032 §2, §3; ADR-ARCH-033 §5).
--
-- A function saasbtp_migrator creates is executable by PUBLIC by default. The first statement of
-- the first script that creates a function closes that default for every later one, before any
-- function exists (ADR-ARCH-028 and ADR-ARCH-032, Consequences); roles.sql cannot, since only the
-- role itself may change its own default privileges. access.may_act_in is the first function.
alter default privileges revoke execute on functions from public;

-- True when the subject of the current transaction holds a current access to that workspace,
-- read on the pair of workspace and organization, never as "the workspace is visible"
-- (ADR-ARCH-033 §5). It reads the subject's profile itself rather than through
-- access.current_profile_id, so that it is the first function created. The parameter is always
-- written may_act_in.workspace_id: unqualified, the name would bind to the column of the access.
-- Without a subject it is false; a subject that is not a uuid makes the statement fail.
create function access.may_act_in(workspace_id uuid)
returns boolean
language sql
stable
security invoker
as $$
  select exists (
    select 1
    from access.workspace_accesses a
    join access.workspaces w on w.id = a.workspace_id and w.organization_id = a.organization_id
    join access.profiles p on p.id = a.profile_id
    where a.workspace_id = may_act_in.workspace_id
      and a.ended_at is null
      and p.user_id = (select nullif(current_setting('app.user_id', true), '')::uuid)
  )
$$;

-- The profile of the subject of the current transaction, or null. On a reused pooled connection
-- a setting defined once reads as an empty string after its transaction, hence nullif
-- (ADR-ARCH-032 §3).
create function access.current_profile_id()
returns uuid
language sql
stable
security invoker
as $$
  select p.id
  from access.profiles p
  where p.user_id = (select nullif(current_setting('app.user_id', true), '')::uuid)
$$;

grant execute on function access.may_act_in(uuid) to saasbtp_api;
grant execute on function access.current_profile_id() to saasbtp_api;

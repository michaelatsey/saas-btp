-- The custom access token hook that sets the contact_confirmed claim (ADR-ARCH-038 decision 5 and
-- Consequences). Supabase Auth calls it before it issues each access token, refreshes included,
-- as supabase_auth_admin: true when the user is not anonymous and has a confirmed e-mail address
-- or phone number, false otherwise, overwriting any claim of that name.
--
-- Auth-coupled (sql.md, "Auth-coupled scripts"): it names supabase_auth_admin, anon and
-- authenticated, which a bare PostgreSQL does not have. It is listed by name in
-- apps/api/tests/SaasBtp.Testing/AuthCoupledScripts.cs, which every Testcontainers migration and
-- the local Migrate step exclude; a deployment applies it. It is validated on the staging project
-- by the checklist of ADR-ARCH-038 decision 9, never against a faked auth schema.
--
-- In the schema access, one schema per bounded context (sql.md): Access is the context whose
-- founding requires the level. ADR-ARCH-038, Consequences, leaves the hook's schema to this
-- script, and the hook URI names the schema (Supabase, "Auth Hooks"). The hook is enabled per
-- project, outside this script, at pg-functions://postgres/access/custom_access_token_hook.
--
-- language plpgsql: creating the function resolves nothing in its body, so saasbtp_migrator needs
-- no grant on auth; auth.users is resolved when supabase_auth_admin, which owns it, executes the
-- hook. The body declares only a scalar: a type taken from an auth relation would be resolved at
-- creation. Not security definer (ADR-ARCH-032; Supabase, "Auth Hooks"). search_path is empty, so
-- that every name the body uses is the one written. One read of the user record and nothing else:
-- an error or a timeout of the hook fails the sign-in (ADR-ARCH-038, Consequences).
create function access.custom_access_token_hook(event jsonb)
returns jsonb
language plpgsql
stable
security invoker
set search_path = ''
as $$
declare
  confirmed boolean;
begin
  -- No row, or an anonymity that is not stated false, gives false: the claim fails closed.
  select u.is_anonymous is false
         and (u.email_confirmed_at is not null or u.phone_confirmed_at is not null)
    into confirmed
    from auth.users u
   where u.id = (event ->> 'user_id')::uuid;

  -- The hook returns the claims (Supabase, "Custom Access Token Hook", Outputs).
  return jsonb_build_object(
    'claims',
    jsonb_set(event -> 'claims', '{contact_confirmed}', to_jsonb(coalesce(confirmed, false))));
end;
$$;

-- What Supabase Auth needs to call it, and nothing more (ADR-ARCH-038, Consequences). The usage on
-- access gives supabase_auth_admin no table of the schema, and no other function: the default
-- execute of PUBLIC is revoked since 0002, and the other functions are granted to saasbtp_api only.
grant usage on schema access to supabase_auth_admin;
grant execute on function access.custom_access_token_hook(jsonb) to supabase_auth_admin;
revoke execute on function access.custom_access_token_hook(jsonb) from public, anon, authenticated;

-- A revoke that removes nothing only warns, and the script would be journaled: the outcome is
-- checked instead, and a privilege missing or still held fails the script.
do $$
declare
  role_name text;
begin
  if not has_schema_privilege('supabase_auth_admin', 'access', 'usage')
      or not has_function_privilege('supabase_auth_admin', 'access.custom_access_token_hook(jsonb)', 'execute') then
    raise exception 'supabase_auth_admin cannot call access.custom_access_token_hook';
  end if;

  foreach role_name in array array['anon', 'authenticated', 'saasbtp_api'] loop
    if has_function_privilege(role_name, 'access.custom_access_token_hook(jsonb)', 'execute') then
      raise exception '% can execute access.custom_access_token_hook', role_name;
    end if;
  end loop;
end
$$;

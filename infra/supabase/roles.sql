-- Database roles of one Supabase project: the migration runner and the API (ADR-ARCH-027,
-- ADR-ARCH-028).
--
-- Run once per project, by the maintainer, as `postgres`, outside the DbUp journal. It is not
-- idempotent: a second run stops at the first `create role`, and the single transaction leaves
-- nothing half done. From the repository root:
--
--   psql "host=<session pooler host> port=5432 dbname=postgres user=postgres.<project ref> sslmode=require" \
--     --single-transaction --set ON_ERROR_STOP=1 --file infra/supabase/roles.sql
--
-- Copy the session pooler host from the project's Connect dialog. No password is written here:
-- then open an interactive psql session with the same connection string and set each role's
-- password with `\password`, which prompts for it and sends it encrypted, so it reaches neither
-- this file nor the shell history:
--
--   \password saasbtp_migrator
--   \password saasbtp_api
--
-- Record both passwords in Bitwarden, then build each connection string from them for Doppler.

-- The runner. It creates and owns the journal schema `dbup` and every application schema and
-- object; owning tables does not exempt it from RLS, since every application table is created
-- with `force row level security`.
create role saasbtp_migrator with
  login
  nosuperuser
  nocreatedb
  nocreaterole
  noreplication
  nobypassrls;

grant create on database postgres to saasbtp_migrator;

-- A choice, not a platform limit: a role's own setting overrides the platform's default of two
-- minutes, which applies only to roles without one.
-- The runner's command timeout (MigrationRunner.cs) is set above it so that the server cancels
-- first. A script that needs longer raises both, here and in the runner, before it is merged.
alter role saasbtp_migrator set statement_timeout = '110s';

-- The API. It owns nothing and is subject to RLS. Each DbUp script that creates an object grants
-- it what it needs on that object; it receives nothing on `dbup`.
create role saasbtp_api with
  login
  nosuperuser
  nocreatedb
  nocreaterole
  noreplication
  nobypassrls;

-- What every role receives through PUBLIC. `connect` stays: Supabase's services connect through
-- that grant, and revoking it disconnects the Data API, Auth and Storage ("Custom role inherits
-- privileges that were not explicitly granted"). `postgres`, `anon`, `authenticated` and
-- `service_role` keep their own `usage` on `public`. Neither role of this file uses temporary
-- tables or `public`, so neither is granted either back.
revoke temporary on database postgres from public;
revoke usage on schema public from public;

-- A revoke that removes nothing only warns, and the transaction would commit: the outcome is
-- checked instead, and a privilege still held rolls everything back.
do $$
declare
  role_name text;
begin
  foreach role_name in array array['saasbtp_migrator', 'saasbtp_api'] loop
    if has_database_privilege(role_name, 'postgres', 'temporary')
        or has_schema_privilege(role_name, 'public', 'usage') then
      raise exception '% still holds temporary on the database or usage on public', role_name;
    end if;
  end loop;
end
$$;

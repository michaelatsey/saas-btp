# ADR-ARCH-053 — The Identity module's persistence on the shared database, and the session check the API reads

Status: accepted
Date: 2026-11-27
Refs: issue #248; ADR-ARCH-019; ADR-ARCH-025; ADR-ARCH-027; ADR-ARCH-028; ADR-ARCH-032;
ADR-ARCH-033; ADR-ARCH-034; ADR-ARCH-042; ADR-ARCH-043; ADR-ARCH-044; ADR-ARCH-054.

## Context
ADR-ARCH-042 places the Identity module in its own host, with the schema `identity` in the shared
database, and its stores on the EF Core integrations of ASP.NET Core Identity and OpenIddict.
ADR-ARCH-043 has the API check, on every authenticated request, that the session of the token is
still active. The persistence decisions in force were written for the API's modules:

- DbUp is the only owner of the schema; a module's EF Core context never creates or alters it, and
  has the test `MappingMatchesMigratedSchema` (ADR-ARCH-019, ADR-ARCH-033 §3).
- Every application table that enables row level security forces it (ADR-ARCH-027 §3).
- A runtime role owns nothing, has no `bypassrls`, connects through Supavisor in session mode
  (ADR-ARCH-028 §1, §3, §4); application schemas grant nothing to `anon`, `authenticated` nor
  `service_role` (ADR-ARCH-028 §5).
- "No function runs as `security definer` and no role bypasses row level security" (ADR-ARCH-032 §4).
- The API sets `app.user_id` as the first statement of every transaction, every database access of
  the API runs inside an explicit transaction, and policies fail closed without a subject
  (ADR-ARCH-032 §3).
- A unit of work opens one explicit transaction, its interceptors set the subject first and refuse a
  command without a transaction; "the readiness check of the host (`DatabaseHealthCheck.cs`) ... is
  the one database access outside them" (ADR-ARCH-033 §4).

Most of the identity service's flows have no signed-in person: a code request names a contact by
value, the token endpoint exchanges a code, the stop page is reached from a notice without signing
in (BP002-R11). A policy keyed on `app.user_id` would admit none of them.

OpenIddict's EF Core integration maps four entities, applications, authorizations, scopes and tokens
(OpenIddict documentation, "Entity Framework Core integration", read 2026-11-27); "By default, the
Entity Framework Core integration uses `string` primary keys", and `ReplaceDefaultEntities<Guid>()`
with `UseOpenIddict<Guid>()` uses `Guid` keys. ASP.NET Core Identity maps its user, login and token tables, and a passkey table from schema
version 3: "update the Identity configuration to use schema version 3, which includes passkey
support" (Microsoft Learn, "Enable passkeys in Blazor", aspnetcore-10.0, read 2026-11-27); its EF
model maps the passkey's data as an owned type stored as JSON (`OwnsOne(p => p.Data).ToJson()`,
`dotnet/aspnetcore` `release/10.0` 500a5c64, `IdentityUserContext.cs`). No Microsoft page read states
how that JSON column behaves on the Npgsql provider.

## Decision
1. **The role.** The identity host connects as `saasbtp_identity`, with the attributes ADR-ARCH-028
   §1 gives `saasbtp_api`: `login`; no `superuser`, `createdb`, `createrole`, `replication` nor
   `bypassrls`; no membership in another role; it owns nothing. It connects through Supavisor in
   session mode, as `saasbtp_identity.<project ref>`, with `SSL Mode=Require`, its pool capped below
   the project's pool size (ADR-ARCH-028 §4). The maintainer creates it in
   `infra/supabase/roles.sql`, with its password set interactively (ADR-ARCH-027 §6). It receives
   `usage` on `identity` and the privileges its tables need, granted by the DbUp scripts that create
   them, and nothing on any other application schema except the one read ADR-ARCH-054 grants it on
   `access`. Its connection string lives in the
   `saas-btp-identity` Doppler project (ADR-ARCH-042).
2. **The schema is DbUp's.** Every table of `identity`, the ASP.NET Core Identity and OpenIddict
   store tables included, is created by DbUp scripts named after the module (ADR-ARCH-019,
   ADR-ARCH-033 §2). EF Core migrations are never generated nor run. OpenIddict's entities use `Guid`
   keys. The Identity module's context has `MappingMatchesMigratedSchema` (ADR-ARCH-033 §3), which
   covers the store tables like the module's own.
3. **Row level security.** It is enabled and forced on every table of `identity` (ADR-ARCH-027 §3).
   Its policies admit the role `saasbtp_identity`, by role, and no policy of `identity` reads
   `app.user_id`, except the one of decision 5 for `saasbtp_api`. This is the Identity context's
   rule: ADR-ARCH-032 §3's subject rules govern the API's contexts and workspace data, and hold there
   unchanged. No function of `identity` runs as `security definer`, and no role bypasses row level
   security (ADR-ARCH-032 §4 holds). `anon`, `authenticated` and `service_role` receive nothing on
   `identity` (ADR-ARCH-028 §5 extended to it).
4. **Transactions in the identity host.** Its contexts keep ADR-ARCH-033 §4's rules, except the
   subject: EF's automatic transactions are disabled, a unit of work opens one explicit transaction,
   and a command without a transaction is refused; no subject is set. ADR-ARCH-033 §4's rule that the
   interceptor sets the subject first, and that a transaction without a subject is an error, is
   superseded for the identity host only.
5. **The session check.**
   - `identity.session_is_active(sid uuid) returns boolean`, `language sql`, `stable`,
     `security invoker`, empty `search_path`. It returns true when OpenIddict's authorization `sid`
     exists, is valid, and belongs to the current subject; false otherwise.
   - `saasbtp_api` receives `usage` on `identity`, `execute` on that function, and `select` on the
     authorization table's identifier, subject and status columns only. A policy for `saasbtp_api`
     alone admits a row only when its subject is
     `nullif(current_setting('app.user_id', true), '')`. The API can therefore learn whether one of
     the caller's own sessions is active, and nothing else: no other person's session, no contact, no
     code, no other table.
   - The API runs it after validating the token (ADR-ARCH-043), on its own connection, without a
     context, in an explicit read-only transaction whose first statement is
     `select set_config('app.user_id', @sub, true)`, with `sub` from the validated token as a
     parameter. ADR-ARCH-032 §3 holds for it.
   - ADR-ARCH-033 §4's sentence naming the readiness check as "the one database access outside them"
     is superseded: the session check is a second access outside a unit of work, with the rules just
     stated. This is also the synchronous read across modules that ADR-ARCH-034 allows only through
     an ADR.
   - Every unit of work of the API also reads it inside its own transaction, after a shared advisory
     lock on the caller's account (ADR-ARCH-054 decision 4), with the same grants.
6. **Pruning.** OpenIddict's tokens and authorizations that are revoked, redeemed or expired are pruned
   once they are older than 14 days, with its own pruning (`PruneAsync` on the token manager, then the
   authorization manager, or `OpenIddict.Quartz`, whose default is 14 days); a pruned authorization is
   absent, and the session check answers false for it (ADR-ARCH-043).

## Consequences
- The maintainer adds `saasbtp_identity` to `infra/supabase/roles.sql` and creates it in each
  project (ADR-ORG-004 §4 for production).
- The module's scripts must reproduce exactly the tables OpenIddict 7.9.0 and Identity's schema
  version 3 map; the mapping test fails otherwise, so an upgrade of either library that changes its
  model comes with a DbUp script. OpenIddict's 7.0 upgrade notes such a change (`Tokens.Type`
  lengthened from 50 to 150), as an example of what an upgrade can require.
- Identity's schema version 3 is used, for the operators' passkeys (ADR-ARCH-050). Its passkey data
  column, mapped as JSON, is not documented for Npgsql on the pages read: the mapping test and an
  integration test of an operator passkey on PostgreSQL establish it, before the operator console is
  relied on.
- The delivery tests the grants: `anon`, `authenticated` and `service_role` read nothing of
  `identity`; `saasbtp_api` reads only the three columns, only for its subject, and executes only the
  session function; `saasbtp_identity` reads nothing outside `identity` but `access.profiles(user_id)` (ADR-ARCH-054); the session check's
  transaction sets the subject first.
- One database round trip per authenticated request of the API, in addition to its unit of work. No
  cache: a cached answer would keep an ended session accepted for the cache's lifetime.

## Alternatives considered
- **The session check as `security definer`.** Rejected by ADR-ARCH-032 §4.
- **A policy admitting `saasbtp_api` to every authorization.** Rejected: the API role could read every
  person's session status; keyed on the subject, it reads only the caller's.
- **The check inside each endpoint's unit of work only.** Rejected as the only check: it would run
  after the body is read and only on endpoints that open a unit of work, while authentication runs
  first for every endpoint (ADR-ARCH-038 decision 1). ADR-ARCH-054 adds it inside every unit of work,
  besides the request's check, for BP002-R14.
- **OpenIddict's introspection, or its validation handler with entry validation, in the API.**
  Rejected: introspection is a call to the identity host on every request; entry validation needs
  the validation handler "a direct access to the server database where tokens are stored" (OpenIddict,
  "Token storage", read 2026-11-27) and would replace the JwtBearer handler ADR-ARCH-038 decides.
- **EF Core migrations for the store tables.** Rejected by ADR-ARCH-019 and ADR-ARCH-033 §3.

## References
Read on 2026-11-27:
- OpenIddict: https://documentation.openiddict.com/integrations/entity-framework-core;
  https://documentation.openiddict.com/configuration/token-storage;
  https://documentation.openiddict.com/configuration/authorization-storage;
  https://documentation.openiddict.com/integrations/quartz;
  https://documentation.openiddict.com/guides/migration/60-to-70.
- Microsoft Learn: "Customize the Identity model in ASP.NET Core"
  (https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0);
  "Enable passkeys in Blazor"
  (https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/blazor?view=aspnetcore-10.0).
  Source: https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/ (500a5c64,
  `EntityFrameworkCore/src/IdentityUserContext.cs`).
- `infra/supabase/roles.sql`; ADR-ARCH-027, ADR-ARCH-028, ADR-ARCH-032 §3 and §4, ADR-ARCH-033 §2 to §4.

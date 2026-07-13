# Session — 2026-07-14 (#47: site schema + relationship-based authorization, ADR-ARCH-009)

Continues: 016-2026-07-13-access-squash-rebaseline.md.

Implementation record for #47 (site schema + real `ISiteScopeProvider`). The plan was reviewed and
corrected over several rounds before implementation; the settled decisions and the review history live
in ADR-ARCH-009 and the approved plan. This file records what was built, what was verified, and the
debt handed forward.

---

## What #47 delivers

The Site module (Story A / #17) had a provisional, config-backed `ISiteScopeProvider` answering "which
sites belong to this tenant?" — and over-permitted: every authenticated user in a tenant saw every site
in it. #47 replaces it with a real, membership-backed authorization check and lays down the `site`
schema it reads from.

### The schema — `0004_site_model.sql` (PORTABLE, append-only)

- `site.sites` — id (UUIDv7, no DB default), tenant_id (denormalized, NO cross-context FK), name
  varchar(200), status varchar(20) CHECK (active|closed — nothing reads it in #47), created_at
  (domain-stamped, no default). Plus `ux_sites__id_tenant_id` UNIQUE (id, tenant_id) — its sole purpose
  is to be the matching target of the composite FK below; not a redundant read-path index.
- `site.site_memberships` — the SITE EDGE. id, site_id, tenant_id (denormalized), user_id (NO FK to
  access.profiles — cross-context), origin_tenant_id (NULL; the tenant the person comes from — internal
  iff = tenant_id; captured now because it cannot be retrofitted), role varchar(20) CHECK
  (site_manager|member), is_active, valid_from (business start, domain-stamped), valid_until (planned end,
  distinct from is_active), created_at.
  - Composite FK `fk_site_memberships__sites (site_id, tenant_id) -> sites (id, tenant_id)` — makes the
    denormalized tenant_id physically unable to diverge from the site's tenant. Intra-context, allowed.
  - `ux_site_memberships__site_id_user_id` UNIQUE (one role per user per site).
  - `ck_site_memberships__valid_window` CHECK (valid_until IS NULL OR valid_until > valid_from).
  - `ix_site_memberships__user_id` — the PowerSync user-first parameter-query lookup ONLY (no existing
    index has user_id as a left prefix). NOT motivated by the provider's branch-(a) probe, which is an
    equality on both user_id AND site_id and is served by the composite unique. `ix_...__site_id` and
    `ix_...__tenant_id` deliberately absent.
- RLS enabled, NO policy on both tables. The only consumer is the privileged .NET owner, which bypasses
  RLS. RLS-on/no-policy returns ZERO ROWS SILENTLY to any non-bypassing role — so the day PowerSync (a
  non-bypassing role) reads these, a policy is MANDATORY (#50). The #45 lesson.

Portable (names no GoTrue object) => NOT added to `AuthCoupledScriptMarkers`; the anti-drift harness
applies it on bare Postgres automatically.

### The authorization model — ADR-ARCH-009

`ISiteScopeProvider` is now `Task<bool> CanActInSiteScopeAsync(userId, tenantId, siteId)` — a
relationship-based check, answer OR-ed across TWO INDEPENDENT edges:

- (a) an ACTIVE, window-valid site membership on (user_id, site_id) — the branch that covers people
  EXTERNAL to the site's tenant (a subcontractor holds the site edge with no organization edge).
- (b) an ACTIVE tenant membership on (user_id, tenant_id) whose role confers cross-site scope — today
  `owner` only, held in C# (`SiteMembershipScopeProvider.CrossSiteScopeRoles`), never in SQL and never
  in a migration.

`tenantId` is an explicit argument (not `(userId, siteId)` as #47 originally framed it) because branch
(b) cannot be evaluated without it.

`SiteMembershipScopeProvider` reads BOTH edge tables directly with plain Npgsql (one EXISTS-OR-EXISTS
query) on the owner connection. It couples to the SCHEMA, not the Access module — the same coupling
PowerSync's parameter queries will have. An Access-owned query port was deliberately NOT invented (it
would need a shared contracts project and Access's first C# membership read path — premature). This is a
one-file refactor if #48/#49 ever justify a port.

ADR-ARCH-009 AMENDS ADR-ARCH-008 (its "access.memberships is the ONLY authorization bridge" is now one
of two edges; #008's identity-only-JWT and per-request-data decisions stand) and retires the
ADR-ARCH-004 "Site owns no Membership model" guardrail.

---

## Teardown (minimal — request-context rebuild stays #49)

Deleted: `ConfigurationSiteScopeProvider`, `CurrentSiteMiddleware`, `SiteCatalogueOptions`, `SiteRecord`,
their two unit tests, and the `SiteCatalogue` appsettings section. The `X-Site-Id` header is gone for
good — grep confirmed `CurrentSiteMiddleware` was its ONLY executable reader (all other hits are XML doc
comments + the `.http` scratch file). ADR-ARCH-005 forbids resolving the site from a header.

Left DORMANT for #49 (compiling, unused): `GET /context`, `ResolveCurrentSite*`, `ICurrentSiteContext` /
`CurrentSiteContext`, `SiteScope`, `SiteResolution`, and the now-passthrough `UseSiteModule`. The handler
depends only on the site CONTEXT (not the provider), so it still compiles once the middleware is gone.

### DI wiring — read before registering

Verified: there is NO `NpgsqlDataSource` in the container today (0 grep hits; Safety's EF datasource is
internal and `AddSafetyModule` is not even Host-composed). The provider is the first DB consumer.
Registered a NON-AMBIENT, Site-owned `SiteScopeDataSource` wrapper (never a bare
`AddSingleton<NpgsqlDataSource>` which would be a last-wins global). Connection key `ConnectionStrings:SiteDb`
(owner role, bypasses RLS), mirroring Safety's `SafetyDb`. A dev placeholder was added to appsettings so
the Host still starts; the data source opens no connection until first query (there is none in #47).

### Architecture tests — handled honestly, not gamed

- `SiteArchitectureTests` #10 (was `SiteScopeProvider_TakesTenantOnly_NoUserIdentityParameter`, encoding
  ADR-ARCH-004's "no user parameter") REWRITTEN to assert the new 4-arg predicate signature.
- #9 (`Site_ContainsNoMembershipModel`, its comment literally citing ADR-ARCH-004) is a DEAD guardrail —
  Site now owns the site edge — so it is DELETED, not kept green by naming the provider around it. Its
  retirement is recorded in ADR-ARCH-009. Tests #1–#8 (no EF/DbContext, no cross-module reference,
  dependencies inward) STAND and stay green: the provider is plain Npgsql and references no other
  module's assembly.

---

## Verification

Docker is NOT available in this environment, so the Testcontainers-backed tests could not be executed
here. They are WRITTEN and COMPILE; run them where Docker is present (there is no CI yet — #38).

- Build: `dotnet build SaasBtp.slnx -c Release` — 0 warnings, 0 errors (Release treats warnings as
  errors).
- Ran (non-Docker), all green: Architecture 24/24 (rewritten #10 included), migrator classification 5/5
  (new `[InlineData("0004_site_model")]`), Site unit 10/10 (dormant surface intact), Access 8/8,
  Safety 40/40.
- NOT run here (Docker-gated), written + compiling:
  - `SiteSchemaTests` — anti-drift over `site.sites` + `site.site_memberships` (columns, types, PK, the
    composite FK, both uniques, both CHECKs, RLS, no-default on domain-stamped columns, and the two
    negative redundant-index assertions).
  - `SiteMembershipScopeProviderTests` — 10 cases including the five mandatory ones: owner-no-site =>
    ALLOWED (branch b), member-no-site => DENIED, external-no-tenant => ALLOWED (branch a),
    valid_until-past => DENIED, is_active=false => DENIED. Plus admin-only, inactive-owner,
    valid_from-future, internal-active, no-edge.

VERIFIED (by reading, not asserted): `GET /me` and `GET /context` stay 403, NOT a new 500.
`ResolveCurrentSiteHandler` guards user (401) then tenant (403, early return) BEFORE any
`ICurrentSiteContext` read, so with tenant resolution dead (#45) it returns 403 and never touches the
now-unpopulated site context. Repair is #49; no fallback added.

---

## Debt handed forward (schema-first, intentional — stated so it is inherited, not rediscovered)

1. The two tables have NO WRITER after #47. No create-site, no assign-site-member command; populated by
   tests only. Write commands are #48.
2. `ISiteScopeProvider` has NO PRODUCTION CALLER after #47. Registered in DI, proven by tests, invoked by
   nothing. Its first caller is the constat write path (#49+), which validates the payload-carried
   `siteId` against it.

Both are recorded in three places: the approved plan, this session file, and `build-checklist.md`.

---

## Where things stand

- #47 — implemented on branch `feature/site/site-schema-and-scope-provider`, UNCOMMITTED. The human runs
  every git operation (Claude never touches git). Docker-gated tests to be run in a Docker environment
  before merge.
- The migration set is append-only. 0004 is the first script added since the #45 squash closed the window.
- Next: #48 (onboarding CQRS: RegisterTenant, InviteMemberCommand) -> #49 (request context + .NET
  repoint, repairs /me, ProblemDetails) -> #50 (RLS spike) -> #51 (Story 1 branch 2, closes #34).
- Carried, unchanged: public signup stays DISABLED until #48; PowerSync publication must include
  `access.memberships` AND the site tables (#50 / first sync slice); the C#/SQL authorization duplication
  (provider vs PowerSync sync rules) needs an agreement test when the sync slice lands (ADR-ARCH-009).

---

## Files touched

Added: `0004_site_model.sql`; `SiteMembershipScopeProvider.cs`, `SiteScopeDataSource.cs` (Site.Infrastructure/Authorization);
`SiteSchemaTests.cs` (migrator tests); `SaasBtp.Site.IntegrationTests/` (new project: csproj, GlobalUsings,
PostgresTestImage, SiteMembershipScopeProviderTests) + its `SaasBtp.slnx` entry;
`ADR-ARCH-009-relationship-based-authorization.md`.
Modified: `ISiteScopeProvider.cs`; `SiteModuleExtensions.cs`; `SaasBtp.Site.Infrastructure.csproj`
(+Npgsql); `SiteArchitectureTests.cs` (#9 deleted, #10 rewritten); `MigrationScriptClassificationTests.cs`
(+InlineData); Host `appsettings.json` (SiteCatalogue -> ConnectionStrings:SiteDb); `ADR-ARCH-008` (Amended
by line); `decisions-index.md`; `build-checklist.md`.
Deleted: `ConfigurationSiteScopeProvider.cs`, `CurrentSiteMiddleware.cs`, `SiteCatalogueOptions.cs`,
`SiteRecord.cs`, `ConfigurationSiteScopeProviderTests.cs`, `CurrentSiteMiddlewareTests.cs`.

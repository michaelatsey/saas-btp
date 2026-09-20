# ADR-ARCH-009 — Authorization model: claims-based authentication, relationship-based authorization over two independent edges

Status: accepted
Date: 2026-07-14
Amends: ADR-ARCH-008 (tenants + memberships as THE authorization bridge — now ONE of two independent
edges; #009 amends, does not supersede)
Refs: ADR-ARCH-005 (.NET domain = write authority; action context in the payload), ADR-ARCH-006
(DbUp owns the DDL; EF is runtime ORM only), ADR-ARCH-008 (identity model; JWT identity-only),
migrations 0002_access_identity_model (the organization edge: access.memberships) and
0004_site_model (the site edge: site.sites + site.site_memberships, portable), issues #47 (this),
#48 (write commands: create-site / assign-site-member), #49 (.NET request-context repoint),
#50 (RLS policies + PowerSync publication).
Amended by: ADR-ARCH-010 (the site is the authorization grain — no sub-perimeter column on the edge)

## Context

ADR-ARCH-008 made the JWT identity-only and declared `access.tenants` + `access.memberships` the
authorization bridge — a user's tenants and roles are DATA queried per request, never token claims.
It also stated, explicitly, that site-level assignment "remains a separate, later concern under the
Site context; this ADR does not annex it." #47 is that later concern.

Three facts force the model now, before the Site read/write stories are built on top of it:

- A site is worked by people who are NOT members of the site's tenant. A subcontractor or an
  external QHSE inspector is assigned to a chantier while belonging to another organization (or none
  the product knows). If authorization required a tenant membership, they could not be represented
  without falsely enrolling them in the client's tenant.
- The offline client authorizes per action, not per session. `uploadData()` replays a batch of
  commands captured across SEVERAL sites in ONE HTTP request (ADR-ARCH-005), so "the current site"
  is not ambient request state — the site is a property of each command, and the check takes it as an
  argument.
- PowerSync cannot call the .NET domain. Its sync rules are SQL parameter queries over the source
  tables, so whatever "may this user see this site's data" means, it will be expressed a second time
  in SQL. That duplication is unavoidable and must be made visible rather than discovered.

The Story-A (#17) Site module answered a DIFFERENT question — "which sites belong to this tenant?" —
from a config-backed catalogue, and over-permitted: every authenticated user in a tenant saw every
site in it. That provisional machinery is removed here.

## Decision

Authentication and authorization are separated, and authorization is relationship data over two
independent edges.

- Claims-based AUTHENTICATION only. The JWT proves identity (`sub`, `email`, `observer_name`,
  `observer_function`) and nothing else. It carries no tenant, no site, no role, no permission —
  ever. (Reaffirms ADR-ARCH-008.)
- Relationship-based AUTHORIZATION over TWO INDEPENDENT edges, queried per request:
  - `access.memberships` — user -> tenant (the organization edge, 0002).
  - `site.site_memberships` — user -> site (the site edge, 0004).
  Neither implies the other. A site membership can exist ALONE: an external person holds the site
  edge with no organization edge in the site's tenant, and nothing rejects it.
- Externality is a RELATIONAL FACT, not a role. A person is external to the site's tenant iff they
  hold no active tenant membership in it. There is no `sub_contractor` token. `site_memberships`
  records `origin_tenant_id` (the tenant the person comes from) as data on the read path — equal to
  `tenant_id` means internal, different means external, null means independent/unknown — with no FK
  and no CHECK, because externality is read, not constrained.
- Permission and scope RESOLUTION lives in C#, not the database. There is no `permissions`, `roles`,
  or `role_permissions` table. The role -> scope matrix is code: today the only cross-site role is
  `owner` (`SiteMembershipScopeProvider.CrossSiteScopeRoles`). Changing the matrix requires no
  migration.
- Role tokens are `varchar` + CHECK (sql.md §Closed sets); the GRAIN is frozen, the VALUES are not.
  Adding a token later is a one-line append-only migration. One relation = one role, enforced by a
  unique constraint (`ux_site_memberships__site_id_user_id`); moving to N roles per relation later is
  DROPPING that unique — append-only and lossless.
- The site scope check is a single port, `ISiteScopeProvider.CanActInSiteScopeAsync(userId, tenantId,
  siteId)`, answering "may this user act in this site's scope" as branch (a) OR branch (b):
  - (a) an ACTIVE site membership on `(user_id, site_id)` with `now()` inside `[valid_from,
    valid_until)` — the branch that covers EXTERNAL people;
  - (b) an ACTIVE tenant membership on `(user_id, tenant_id)` whose role is in the C# cross-site set
    (today `owner` only) — the seam for "sees all sites without being assigned to any".
  `tenantId` is an explicit argument (not `(userId, siteId)` as #47 originally framed it) because
  branch (b) cannot be evaluated without it.

### The provider reads the source tables directly — it couples to the schema, not to Access

`SiteMembershipScopeProvider` reads BOTH edge tables — `site.site_memberships` and
`access.memberships` — with plain Npgsql on the owner connection. This crosses into the `access`
schema, but it does NOT reference the Access module's code: the coupling is to the SCHEMA, exactly the
coupling PowerSync's parameter queries will have when they read the same tables in SQL. An
Access-owned C# query port for branch (b) is deliberately NOT invented now (see Alternatives). If a
later issue (#48/#49) builds a real Access membership read path and a port becomes justified, the
provider is refactored then — the cost is one infrastructure file.

## Consequences

- ADR-ARCH-008's "`access.tenants` + `access.memberships` are the ONLY authorization bridge" is no
  longer accurate: there are TWO edges. This ADR AMENDS that sentence; it does not supersede
  ADR-ARCH-008, whose identity-only-JWT and per-request-data decisions stand.
- ACCEPTED DUPLICATION, with a SILENT failure mode. The site-access semantics are written twice —
  once in C# (`ISiteScopeProvider`), once in SQL (the PowerSync sync rules, later). When they diverge
  the failure is silent: the server authorizes and the device syncs nothing, or the reverse. This ADR
  MANDATES a test that proves the two agree on the same dataset (Testcontainers, the same pattern as
  the anti-drift schema tests). That test is NOT built in #47 — it lands with the PowerSync slice —
  but the obligation is recorded now.
- RLS is enabled with NO policy on `site.sites` and `site.site_memberships` (as on `access.*`). The
  only consumer today is the privileged .NET owner, which bypasses RLS. RLS-on/no-policy returns ZERO
  ROWS SILENTLY to any non-bypassing role, so the day a non-privileged consumer (PowerSync) reads
  these tables, a policy is MANDATORY (#50) — the #45 lesson.
- `origin_tenant_id` is captured from day one because it cannot be retrofitted: once memberships exist
  without it, the person's origin is lost. Nothing reads it in #47; it exists to preserve the fact.
- Retires the ADR-ARCH-004 "Site owns no Membership model" guardrail. Site now legitimately owns the
  site edge, so the architecture test that encoded it (`SiteArchitectureTests.Site_ContainsNoMembershipModel`)
  is DELETED — recorded here as a documented decision, not a silent loosening. The remaining Site
  boundary tests (no EF/DbContext, no cross-module reference, dependencies inward) stand: the provider
  is plain Npgsql and references no other module's assembly.
- Removes the Story-A provisional site machinery: the config-backed `ConfigurationSiteScopeProvider`,
  the `CurrentSiteMiddleware`, and the `X-Site-Id` header are gone for good (ADR-ARCH-005 forbids
  resolving the site from a header). `GET /context` and its supporting types are left dormant and
  already 403 at runtime since #45 (dead tenant claim); their rebuild is #49. This ADR does not repair
  that 403 — no fallback is added.
- DEBT, schema-first and intentional, stated so it is inherited rather than rediscovered:
  - The two tables have NO WRITER after #47. There is no create-site or assign-site-member command;
    they are populated by tests only until #48.
  - The provider has NO PRODUCTION CALLER after #47. `ISiteScopeProvider` is registered in DI and
    proven by tests, but nothing invokes it in production; its first caller is the write path (#49 and
    the command stories that follow), which validates the payload-carried `siteId` against it.

## Alternatives considered

- A Zanzibar-style relationship engine (OpenFGA / SpiceDB). Rejected: PowerSync cannot query it — its
  sync rules are SQL over the source tables — so instead of removing the C#/SQL duplication it would
  AMPLIFY it into a three-way divergence (C#, SQL, policy engine) and add an external dependency and a
  new failure mode. The relationship model here is small and lives in tables PowerSync can read.
- A `permissions` / `roles` / `role_permissions` table in the database. Rejected: it makes every
  matrix change a migration, and still needs a query layer to be usable from C#. The role -> scope
  matrix is small and belongs in the domain; keeping it in code is what lets it change with no
  migration.
- An Access-owned query port for branch (b) (e.g. `IOrganizationScopeReader` in a shared contracts
  project). Cleaner bounded-context boundary — the `access.memberships` read would stay inside Access.
  Rejected FOR NOW: it requires inventing a shared contracts assembly and Access's first C# membership
  read path (greenfield), which is premature while the provider needs only a boolean EXISTS. The
  provider reads the schema directly; revisit if #48/#49 build the Access read path and the coupling
  earns a port.
- Authorization carried in the JWT (tenant/site/roles as claims). Rejected: revocation would wait for
  token expiry, and the token would grow with the relationship set — already rejected in
  #45/ADR-ARCH-008 and reaffirmed here.

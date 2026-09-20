# Session — 2026-07-10 (Step 2: Supabase wired + Story A verified end-to-end)

Continues: 011-2026-07-09-write-path-adr005-safety-constat-decomposition.md.
Purpose: execute step 2 of the Safety sequence — wire Supabase (infra only) and solder
Story A's pending end-to-end HTTP verification. No product code this session.

## What happened

### Supabase project
- Decision: ONE Supabase project now, NOT prod+staging. Rationale (JIT/YAGNI): no prod
  deployed, no users, no schema yet — staging has nothing to stage; deferring costs ~nothing
  (just replay migrations later), while creating it now invites the 7-day inactivity pause.
  Prod/staging split deferred to when the Coolify pipeline goes live.
- Created project `saas-btp` (ref: boxxynsffybaemctpgdt), EU region. DB password in Bitwarden
  (Freelance - Infra). Verified: 1 project = 1 Postgres DB (no in-project environments).

### Project settings
- Data API (PostgREST): to be OFF — writes go through the .NET domain (ADR-ARCH-005), never
  client-direct to Postgres. Note: the toggle may be UI-only; the supported way to "disable"
  is to remove `public` from exposed schemas (Settings -> API). Auth (GoTrue) is a separate
  service and is unaffected by disabling the Data API — confirmed.
- Auto-expose new tables: OFF. Automatic RLS: ON (defense-in-depth; service role bypasses it).

### Auth model confirmed (via Claude Code repo inspection)
- MicroKit.Auth.Supabase validates via JWKS/ES256 (asymmetric) — NO HS256 shared secret.
  Config surface is only ProjectUrl + Audience + Issuer (none are secrets); JwksUri is derived
  as {ProjectUrl}/auth/v1/.well-known/jwks.json. So nothing secret to store for auth.
- Real token minted from the project; `iss` = https://boxxynsffybaemctpgdt.supabase.co/auth/v1
  matches the expected form exactly (no issuer-shape surprise). `aud` = authenticated.

### Config wiring
- appsettings.Development.json (UNTRACKED) holds the real Supabase ProjectUrl/Audience/Issuer.
  appsettings.json keeps REPLACE_ME placeholders as override markers.
- launchSettings.json (TRACKED) added: API boots in Development on http://localhost:5000, so
  ASPNETCORE_ENVIRONMENT no longer has to be passed by hand. Resolves the prior
  no-launchSettings / non-deterministic-port debt.

### HTTP probes
- Structure under apps/api/http/ (NOT tests/ — these are manual smoke probes, not automated
  tests): access/me.http, site/context.http, README.md, and http-client.env.json (UNTRACKED,
  holds host + token). Token lives only in the gitignored env file.

### Test user tenant claim
- Set app_metadata.tenant_id = 00000000-0000-0000-0000-000000000001 on the test user via the
  Supabase Admin API (service_role, stored in Bitwarden), NOT via direct SQL on auth.users
  (Supabase discourages touching the GoTrue-managed schema). service_role delivered to Claude
  Code via an isolated /tmp file-drop, never printed, shredded after use.

### Story A verification — ALL GREEN against real Supabase
- GET /me: no token 401; valid token 200 (tenantId resolved from app_metadata.tenant_id).
- GET /context: no token 401; valid token 200; valid site 200 (currentSite = Chantier Demo A,
  availableSites = A + B from the provisional ConfigurationSiteScopeProvider).
- GET /context + forged X-Site-Id: 403 via the site-in-tenant check (SITE_NOT_IN_TENANT).
  True pass, proven by differential: the same token passes /me and valid-site; only the forged
  header flips to 403 -> the header is never trusted. Story A's core guarantee proven e2e.

## Decisions / learnings
- One Supabase project now; prod/staging split deferred to pipeline go-live.
- Data API off (write authority is the .NET domain, per ADR-ARCH-005).
- Auth is JWKS/ES256; no shared secret to manage.
- Admin API over direct auth.users SQL for user metadata (supported surface, stable contract).

## Next / re-entry

Step 2 DONE. Auth infra fully de-risked in isolation before any Safety domain code — so any
future Safety failure is a domain bug, not infra. Next = Step 3: Safety schema + Constat code
(first product persistence).

Before ANY code, settle two deferred decisions:
1. Id format -> record UUIDv7 (client-generated, strongly-typed VO) in
   context/architecture/conventions/naming.md as the project-wide convention.
2. Migrations ADR — DbUp (SQL-first) vs EF Migrations. First product schema, so decide now.
   Leaning DbUp (more control, plays well with RLS/triggers/PowerSync CDC, avoids multi-
   DbContext EF-migration pain); write it as an ADR before the schema.

Then implement the Constat: Domain -> Application -> Infrastructure -> API, mirroring
Access/Site hexagonal layout, against the frozen model in
context/architecture/safety-domain-model.md.

### Re-entry prompt for the web orchestration chat

"On reprend le SaaS BTP, etape 3 (schema Safety + code du Constat). Lis la derniere session
(012-2026-07-10-supabase-wiring-story-a-verified.md), build-checklist.md, et
context/architecture/safety-domain-model.md. Etat : Supabase cable, Story A verifiee
end-to-end all-green (auth JWKS/ES256, resolution tenant, scoping site, header jamais cru).
Le Constat est complet sur papier (aggregate fige, contrat API, ADR-ARCH-005 write path).
AVANT tout code, tranche deux decisions differees : (1) format d'id UUIDv7 a acter dans
naming.md, (2) ADR migrations DbUp vs EF (premier schema produit). Ensuite implemente le
Constat Domain->Application->Infrastructure->API en miroir d'Access/Site. Cadre-moi une etape
a la fois, doc gh/MicroKit verifiee, jamais --delete-branch quand la head est dev, confirmation
avant toute action destructive."

## Open items / debt

New this session:
- API error format: 403/4xx responses have empty bodies (no ProblemDetails). Wire a structured
  error format (RFC 9457 ProblemDetails) mapping domain Result/errors -> response payload, so
  clients (esp. the offline PowerSync client, ADR-ARCH-005 rejection protocol) can distinguish
  and surface a rejection reason. Trigger: after the Constat story yields 2-3 real domain error
  types (rule of three). Candidate: mini-ADR. Same debt as the ADR-005 rejection protocol,
  two angles — resolve together.
- Test artifact: app_metadata.tenant_id was set by hand on the test user. The REAL mechanism
  (how a genuine user receives tenant_id: signup / GoTrue hook / invitation) is a future Access
  story, not built.
- "One user = one tenant" assumption: tenant_id as a static JWT claim has a ceiling. If
  multi-tenant-per-user becomes real, tenant becomes a switchable currentTenant (mirroring
  currentSite) + a user<->tenants membership; future Access ADR (the tenant pendant of
  ADR-ARCH-004).
- Test user uses a personal email; consider a dedicated test@ account to avoid mixing.

Resolved this session:
- Story A live HTTP verification (was pending Supabase since session 010).
- launchSettings.json / non-deterministic dev port.

Carried:
- Add the UUIDv7 client-id convention to naming.md (due at step 3).
- MicroKit follow-ups from #4 (findings 2-4).
- Microsoft.OpenApi 2.10.0 pin (remove once AspNetCore.OpenApi ships a patched transitive).
- Offline conflict-resolution strategy (future ADR).
- Directory.Packages.props stale header comment.
- Naming: module-Site vs aggregate-Site — revisit at the first real Site aggregate.

# Session — 2026-07-08 (Story A #17 CurrentSite: Site module implemented, merged via PR #23)

Continues: 009-2026-07-08-site-split-currentsite-membership.md.
Purpose: implement Story A (#17) — the CurrentSite execution context — the buildable
half of the Site split decided in 009. Deliver the Site bounded context in apps/api
(config/claims only, no EF, no Membership) with a protected GET /context, green build +
architecture + unit tests, then merge. The gated half (Membership) stays deferred as
Story B (#20). This is the implementation session; 009 recorded the framing.

## What shipped

- Site bounded context in apps/api, three projects (Hexagonal, mirrors Access):
  - SaasBtp.Site.Domain — pure center. Holds the CurrentSite contract only:
    ICurrentSiteContext, ISiteScopeProvider (driven port), SiteId + SiteScope value
    objects. References nothing (no MicroKit, no ASP.NET Core, no EF).
  - SaasBtp.Site.Application — one vertical slice Features/ResolveCurrentSite/
    (query + handler + result + errors). Host-agnostic.
  - SaasBtp.Site.Infrastructure — driving/driven adapters + composition: the
    CurrentSite resolution middleware, ConfigurationSiteScopeProvider (provisional,
    appsettings-seeded), the GET /context endpoint, AddSiteModule / UseSiteModule /
    MapSiteEndpoints.
- By-aggregate layout, but only Context/ created — no Site aggregate. Story A is an
  execution-context slice, not a domain model. The day a real Site aggregate exists it
  gets its own folder; ADR-ARCH-004's anti-Membership guardrail is extended to Site.
- GET /context returns { user, tenant, roles, currentSite?, availableSites[] } built
  from Access's claims/tenant context plus the provisional config-seeded provider.
  availableSites = the current tenant's site catalogue (seeded: Chantier Demo A / B
  under dev tenant 00000000-0000-0000-0000-000000000001).
- Boundaries respected: Site mirrors Access but never references any Access project.
  The Host orchestrates order — UseSiteModule runs after UseAccessModule so the tenant
  context is populated before the site is validated against it. GET /me untouched.

## The three graved design decisions, as implemented

1. Three-state resolution kept off the Domain interface.
   SiteResolution { NotRequested, Resolved, Rejected } is public on the Infrastructure
   impl (CurrentSiteContext.Resolution), deliberately ABSENT from ICurrentSiteContext
   (which stays a pure two-member read projection: CurrentSite + AvailableSites). The
   middleware only RESOLVES — it never returns 403; the endpoint maps Rejected -> 403
   (SITE.CONTEXT.SITE_NOT_IN_TENANT). Requests always proceed, so site-agnostic
   endpoints like GET /me are unaffected by any X-Site-Id value. HTTP rejection is an
   Infrastructure concern, kept out of the Domain by exclusion, not by an access modifier.

2. Site.Infrastructure declares no direct MicroKit packages.
   It composes no auth/tenancy — it consumes the context Access already populated. The
   MicroKit abstractions + Result it calls flow in transitively via the Site.Application
   project reference. Re-declaring them would be redundant; the layer that composes
   nothing references nothing directly (contrast Access.Infrastructure, which does
   compose auth/tenancy and therefore carries the concrete MicroKit packages).

3. Nested /context contract, VO never serialized.
   The response is nested by bounded context — user{userId,email}, tenant{tenantId} —
   with roles at the ROOT (org-level roles from claims, identical to GET /me). Site
   entries carry only { siteId, name }: tenantId is NOT exposed on site entries. The
   handler projects the domain SiteScope VO to dedicated SiteView DTOs — the SiteScope
   (which carries TenantId for the isolation check) is never serialized on the wire.
   camelCase via the ASP.NET Core System.Text.Json default.

## Validation semantics (anti-Membership boundary held)

Story A validates site-belongs-to-tenant, NOT user-belongs-to-site. Two ordered checks:
tenant resolved (from #4's Auth -> Tenancy chain, else 403), then X-Site-Id present and
in the current tenant's catalogue (else Rejected -> 403). There is no third
"is the user authorized on this site?" check — that IS Membership and does not exist in
A. ISiteScopeProvider takes a tenant id + CancellationToken and NO user parameter (the
seam Story B swaps). Accepted temporary consequence, unchanged from 009: within a tenant
any authenticated user may scope any site of that tenant — bounded by tenant isolation
(#4's hard boundary), tightened by Membership (#20).

## Process notes (worth recording for future sessions)

- Commit discipline: the first pass auto-committed the implementation. That commit was
  reset (git reset --soft) so the pre-merge review gate could run BEFORE anything landed,
  not after. Sequence going forward: implement -> soft-reset any premature commit ->
  run the gate -> commit once clean. The gate precedes the commit, not the reverse.
- The pre-merge gate ran as fresh-context PROMPTS, not agent files: a dependency-guardian
  pass (project-reference direction, CPM compliance, no MicroKit leak into Infra, no EF,
  no sibling coupling) and an api-reviewer pass (interface purity, /context contract vs
  #17, naming/style parity with Access, encapsulation, no scope leak). Both passed. The
  btp-* agents these prompts prefigure are NOT extracted yet — extraction is deferred to
  post-Safety (agents emerge from repeated friction, not on spec) and tracked as a JIT
  chore issue so the pattern is captured when it recurs.
- Encapsulation call: InternalsVisibleTo vs public impl. Chose to keep Resolution (and
  the CurrentSiteContext write-path) PUBLIC on the concrete Infrastructure type, with NO
  InternalsVisibleTo anywhere in the repo. Rationale: it matches Access's house style
  (public sealed, boundaries enforced by the architecture tests, not the internal
  keyword); Domain purity is already guaranteed by keeping the state off the interface.
  Introducing InternalsVisibleTo would be a new pattern with no precedent — not worth it.

## Verification results (all green)

- dotnet build -c Release (warnings-as-errors): 0 errors, 0 warnings.
- 46 tests total, all passing:
  - SaasBtp.Site.UnitTests: 19 (handler, middleware three-state resolution, config
    provider).
  - SaasBtp.Access.UnitTests: 8 (unchanged from #4 — no Access regression).
  - SaasBtp.Architecture.Tests: 19 (9 prior + 10 new Site guardrails: anti-EF,
    anti-Membership, no cross-module reference to Access, tenant-only port signature,
    Domain references nothing, etc.).
- Merged via PR #23. Issue #17 closed on the CurrentSite perimeter (execution context
  delivered; Membership was already carved out to #20 in session 009).

## Deferred / debt (carried forward)

- Live end-to-end HTTP verification is pending a real Supabase token. Manual harness
  committed at apps/api/src/Host/SaasBtp.Api/SaasBtp.Api.http (the /context acceptance
  matrix + /me). Today only the anonymous -> 401 case is reproducible; the 200 and 403
  cases need a valid bearer token once Supabase is wired.
- Story B (#20) Membership — durable user<->site<->role model, gated on ADR-ARCH-004,
  backlog off milestone #1. Replaces ConfigurationSiteScopeProvider with a
  membership-backed provider behind ISiteScopeProvider (adds per-user filtering); the
  GET /context contract is stable across that swap.
- core.hooksPath points to an absent .githooks directory, so the kickoff pre-push guard
  is inactive locally. Either create the hooks or unset the path — the guard is a no-op
  until then.
- Directory.Packages.props has a stale header comment (says Auth family = preview.2 while
  the actual pins and the ItemGroup label are preview.3). Cosmetic; correct on next touch.
- Naming question: module-Site vs aggregate-Site. The plural-folder / by-aggregate
  convention is assumed but unconfirmed for a module that currently has no aggregate
  (only Context/). Revisit at the first real Site aggregate rather than guessing now.
- MicroKit follow-ups from #4 (findings 2-4) still pending in the MicroKit repo, branch
  docs/findings-story-4-integration (docs/microkit-followups-from-story-4.md).

## Next / re-entry

Safety is now buildable: it depended only on CurrentSite (a BTP constat is intrinsically
attached to a chantier), and CurrentSite is delivered. Candidate next moves, decide
before any code:
1. Frame + implement the first Safety story (constat) — the next product increment,
   now unblocked.
2. Story B (#20) Membership — settle ADR-ARCH-004, then the first product persistence
   (first DbContext + Supabase schema + MicroKit.Persistence). Hardening, off the Safety
   path; tightens Story A's accepted over-permission.
3. Clear the MicroKit follow-ups from #4 (findings 2-4) before Site/Safety consume the
   libraries more heavily.

### Re-entry prompt for the web orchestration chat

"On reprend le SaaS BTP. Lis la derniere session
(010-2026-07-08-story-a-currentsite-implemented.md) et build-checklist.md. Etat : Story A
(#17, CurrentSite) est faite et mergee sur dev via PR #23 — module Site (Domain/
Application/Infrastructure) + endpoint protege GET /context ({ user, tenant, roles,
currentSite?, availableSites[] }), config/claims only, zero EF, zero Membership, vert
(build Release 0 warning, 46 tests). Validation site-appartient-au-tenant seulement
(pas user-appartient-au-site) : sur-permission temporaire assumee, bornee par
l'isolation tenant, resserree par Membership #20. Safety est desormais debloque (il ne
dependait que de CurrentSite). Verif HTTP live encore en attente d'un vrai token Supabase
(harness SaasBtp.Api.http, seul anon->401 reproductible aujourd'hui). Prochaine decision :
soit la premiere story Safety (constat), soit Story B #20 (Membership, gated
ADR-ARCH-004), soit les findings MicroKit (#4, findings 2-4). Cadre-moi le choix avant
tout code, une etape a la fois, doc gh/MicroKit verifiee, jamais --delete-branch quand la
head est dev."

## Open items / debt (carried, unchanged)

- Remove the Microsoft.OpenApi 2.10.0 pin once Microsoft.AspNetCore.OpenApi ships a
  patched transitive (NU1903).
- Offline conflict-resolution strategy for safety data (future ADR).
- Back-button behavior in the installed PWA on Android: validate in the real client.
- Token refresh after long offline sessions (real-client architecture requirement).
- Multi-photo per constat + gallery picker (product UI).
- Cold tooling: Context7 auth, powersync-ja/agent-skills eval, persistent corepack in .zshrc.

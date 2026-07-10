# Build Checklist - Phase 1 (Increment 1: Safety constat -> Corrective action)

Progress tracker from project bootstrap to the first story ready to code.
Update this file as items complete. Read it first in a new session to know where we are.

Last updated: 2026-07-10.

---

## Done - Session kickoff

- [x] Mono-repo created and pushed (main + dev)
- [x] Empty monorepo skeleton (apps/, packages/, infra/, ...)
- [x] Anchor files (CLAUDE.md, ai-os vision/roadmap/method, decisions-index)
- [x] ADR-ARCH-001 (OpenAPI contract boundary)
- [x] Pre-push hook (blocks force-push on main/dev)

---

## Step 0 - Gates (decided)

- [x] 0.1 Slice 1 = Safety constat -> Corrective action -> dashboard (ADR-PROD-001)
- [x] 0.2 Bounded contexts validated (Access, Site, Safety, Quality,
      CorrectiveActions separate, Workforce, Stock, Reporting, + Media/Notifications)
- [x] 0.3 GitHub Issues = tracker + versioned convention + backlog.md removed (agreed)

---

## Step 1 - Durable specs in the repo

- [x] 1.1 scope-phase-1.md (steering: Phase 1 perimeter + increment sequence + slice 1)
- [x] 1.2 architecture.md (bounded contexts, dependency graph, offline/sync strategy)
- [x] 1.3a naming.md (project-wide naming conventions standard)
- [x] 1.3b specifications/safety/constat.md (volatile feature spec)
- [x] 1.3c ADR-ARCH-003 (increment-1 client platform = offline PWA, spike-gated)
- [x] 1.4 ADR-PROD-001 (market entry = Safety, Ivorian market rationale)
- [x] 1.5 ADR-ARCH-002 (CorrectiveActions as a separate bounded context)
- [x] 1.6 Adjust product-vision.md (remove "backlog.md canonical" mention)
- [x] 1.7 Delete backlog.md
- [x] 1.8 Commit `docs(scope): define Phase 1 + bounded contexts` + push (on dev)

---

## Step 2 - Tracker foundation (GitHub Issues, reusable)

- [x] 2.1 Issues convention in repo (title format, labels, DoR/DoD)
- [x] 2.2 Create labels via gh (ctx:safety, ctx:corrective-actions, type:story, prio:*, offline...)
- [x] 2.3 Issue templates (.github/ISSUE_TEMPLATE/story.yml)
- [x] 2.4 Milestone "Increment 1 - Safety -> Corrective action"
- [x] 2.5 Project board (Kanban) via gh
- [x] 2.6 Commit `chore(repo): issues convention + templates` + push

---

## Step 3 - Slice 1 into stories (Issues)

- [x] 3.1 Decompose slice 1 into vertical stories (spike + access -> offline capture
      -> corrective lifecycle -> notifications -> dashboard)
- [x] 3.2 Create Issues via gh (#3-#10), labeled + milestone #1 + added to board #9
- [x] 3.3 Define attack order (first to code: #3 spike)
- [x] 3.4 Backlog re-split on bounded contexts (sessions 008, 009): Access/Site
      separated (#4 closed on Access), then Site #17 split into Story A (CurrentSite,
      #17) + Story B (Membership, #20, gated ADR-ARCH-004)

---

## Step 4 - AI OS Palier 1 tooling (JIT, after scope)

- [ ] 4.1 Core btp-* agents (product, architect, implementer, reviewer, ux) - one by one
      NOTE: post-code review (dependency-guardian, api-reviewer) ran as fresh-context
      prompts on Story A #17 (first friction). Extraction to btp-* agent files deferred
      to post-Safety (2 archetypes covered) - tracked as a JIT chore issue.
- [ ] 4.2 (optional) Notion hub for steering
- [ ] 4.3 First story implemented via agent flow -> observe where it hurts

---

## Step 5 - First application code (triggers JIT infra)

- [x] 5.0 Technical spike PowerSync Web offline + camera - 3 gates PASS, ADR-ARCH-003
      cleared, findings.md merged (PR #12), see session 005
- [x] 5.1 Scaffold apps/api (.NET 10 modular monolith) on the first bounded context
      - done as the FIRST ACT of Story #4 (Access): Access module + protected GET /me;
        green build/arch/unit; committed and squash-merged to dev; see sessions 007, 008
- [x] 5.1b Story A #17 (Site Context / CurrentSite): Site module + protected GET /context
      (claims/config only, no EF, no Membership); 46 tests green; dependency-guardian +
      api-reviewer PASS; squash-merged to dev (PR #23); #17 closed on CurrentSite perimeter.
- [x] 5.1c Supabase wiring (infra only, no product code): ONE project `saas-btp`
      (ref boxxynsffybaemctpgdt, EU); Data API off (write authority = .NET domain,
      ADR-ARCH-005); auth = JWKS/ES256, no shared secret. appsettings.Development.json
      (untracked) + launchSettings.json (tracked, Development on :5000) + apps/api/http/
      manual probes. See session 012.
- [x] 5.1d Story A end-to-end HTTP verification against real Supabase - ALL GREEN:
      /me 401/200; /context 401/200; valid site 200; forged X-Site-Id 403
      (SITE_NOT_IN_TENANT, proven by differential - header never trusted). Closes the
      pending live-verification debt from session 010.
- [ ] 5.2 CI path-filters (first workflow, scoped to api)
- [ ] 5.3 Pick OpenAPI -> TS generator (at first client generation)

---

## Current position

Story A #17 (Site Context / CurrentSite) DONE and merged to dev (PR #23): Site bounded
context shipping a single protected endpoint GET /context resolving
{ user, tenant, roles, currentSite?, availableSites[] } from JWT claims + a provisional
config-seeded ISiteScopeProvider (ConfigurationSiteScopeProvider) - no EF, no Membership,
no Site aggregate, no persistence. Mirrors the Access module; Site never couples Access;
Host orchestrates pipeline order (UseSiteModule after UseAccessModule). Three-state
resolution (SiteResolution public on the Infra impl, absent from the Domain interface;
middleware resolves, endpoint maps to HTTP). Validation is site-belongs-to-tenant only,
NOT user-belongs-to-site (accepted temporary over-permission, bounded by tenant isolation,
tightened by Membership in #20). Green: Release build 0 warnings; 46 tests (Site 19 +
Access 8 unchanged + Architecture 19, incl. 10 Site guardrails). Pre-merge review PASS
(dependency-guardian + api-reviewer, fresh-context prompts). GET /me unchanged.
#17 closed on its CurrentSite perimeter.

Supabase wired and Story A verified end-to-end all-green (session 012): auth JWKS/ES256,
tenant resolution, site scoping, forged header never trusted. Auth infra de-risked in
isolation before any Safety domain code.

Next = Step 3: Safety schema + Constat code (FIRST product persistence). Gated on two
deferred decisions to settle BEFORE any code:
1. Id format -> record UUIDv7 (client-generated, strongly-typed VO) in
   context/architecture/conventions/naming.md as the project-wide convention.
2. Migrations ADR -> DbUp (SQL-first) vs EF Migrations (first product schema; leaning DbUp
   for RLS/triggers/PowerSync CDC control). Write the ADR before the schema.
Then implement the Constat Domain -> Application -> Infrastructure -> API, mirroring
Access/Site, against context/architecture/safety-domain-model.md (frozen).

Deferred / not on the Safety critical path:
- Story B #20 (Site Membership) - gated ADR-ARCH-004; hardening, not blocking Safety.
- MicroKit follow-ups from #4 (findings 2-4).

JIT, still untriggered: Step 5.2 (CI path-filters), Step 5.3 (OpenAPI -> TS generator),
Step 4.1 (btp-* agents - extraction deferred to post-Safety).

## Open decisions still pending

- Offline conflict-resolution strategy for safety data (future ADR).
- Naming: bounded-context vs aggregate (module "Site" vs a future "Site" aggregate) -
  plural-folder convention (Sites/Site.cs) assumed sufficient; revisit at the story that
  first creates the Site aggregate (Story C / Safety). Not blocking.

## Carried debt (cross-repo / cleanup)

- API error format: 403/4xx responses have empty bodies (no ProblemDetails). Wire RFC 9457
  ProblemDetails mapping domain Result/errors -> payload, so clients (esp. offline PowerSync,
  ADR-ARCH-005 rejection protocol) can surface a rejection reason. Trigger: after the Constat
  story yields 2-3 real domain error types (rule of three). Resolve with the ADR-005
  rejection protocol (same debt, two angles).
- Test-user tenant_id set by hand via Admin API; the REAL mechanism (signup / GoTrue hook /
  invitation delivering tenant_id) is a future Access story, not built. Also: "one user = one
  tenant" is a static-claim assumption with a ceiling (future Access ADR, tenant pendant of
  ADR-ARCH-004). Test user uses a personal email; consider a dedicated test@ account.
- MicroKit follow-ups from Story #4 (findings 2-4 still open) - tracked in
  docs/microkit-followups-from-story-4.md, to be ACTIONED in the MicroKit repo on branch
  docs/findings-story-4-integration.
- Remove the Microsoft.OpenApi 2.10.0 pin once Microsoft.AspNetCore.OpenApi ships a patched
  transitive (NU1903).
- core.hooksPath points to an absent .githooks dir in the saas-btp clone - the kickoff
  pre-push hook (force-push guard on main/dev) is NOT active locally. Reconstitute in a
  separate infra pass.
- Directory.Packages.props stale header comment (says Auth = preview.2; actual pins are
  preview.3). Pre-existing doc drift, cosmetic. Fix in a separate chore pass.

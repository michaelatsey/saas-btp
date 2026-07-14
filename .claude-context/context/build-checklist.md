# Build Checklist - Phase 1 (Increment 1: Safety constat -> Corrective action)

Progress tracker from project bootstrap to the first story ready to code.
Update this file as items complete. Read it first in a new session to know where we are.

Last updated: 2026-07-14 (#48 STEP 2 storage: access invitations schema, 0005, ADR-ARCH-011).

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
- [x] 2.7 Milestone 2 "Increment 0 - Identity, tenancy & request context" (epic #44)

---

## Step 3 - Slice 1 into stories (Issues)

- [x] 3.1 Decompose slice 1 into vertical stories (spike + access -> offline capture
      -> corrective lifecycle -> notifications -> dashboard)
- [x] 3.2 Create Issues via gh (#3-#10), labeled + milestone #1 + added to board #9
- [x] 3.3 Define attack order (first to code: #3 spike)
- [x] 3.4 Backlog re-split on bounded contexts (sessions 008, 009): Access/Site
      separated (#4 closed on Access), then Site #17 split into Story A (CurrentSite,
      #17) + Story B (Membership, #20, gated ADR-ARCH-004)
- [x] 3.5 Epic #44 inserted BEFORE Story 1 branch 2 (session 015): the tenant/membership
      model cannot be retrofitted into immutable evidence records, so it lands first.
      #34 (Story 1 branch 2) is deferred behind #49. Accepted cost: several weeks.

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
      - Access module + protected GET /me; sessions 007, 008
- [x] 5.1b Story A #17 (Site Context / CurrentSite): Site module + protected GET /context
      (claims/config only, no EF, no Membership); PR #23; #17 closed on CurrentSite perimeter
- [x] 5.1c Supabase wiring (infra only): ONE project `saas-btp` (ref boxxynsffybaemctpgdt, EU);
      Data API off (write authority = .NET domain, ADR-ARCH-005); auth = JWKS/ES256, no shared
      secret. See session 012.
- [x] 5.1d Story A end-to-end HTTP verification against real Supabase - ALL GREEN
- [x] 5.1e Persistence gates SETTLED (were blocking, now closed):
      - Id format: UUIDv7, client-generated, strongly-typed VO -> `naming.md`
      - Migrations: DbUp, sole authority for DDL; EF Core is runtime persistence only,
        migrations disabled -> ADR-ARCH-006
- [x] 5.1f Safety schema + Constat aggregate (first product persistence).
      `0001_safety_create_constats.sql`. Domain -> Application -> Infrastructure -> API.
      Anti-drift schema tests (plain Npgsql introspection, Testcontainers).
- [x] 5.1g Access identity model — SQUASH + REBASELINE (#45, epic #44). See below.
- [x] 5.1h Site schema + relationship-based authorization (#47). `0004_site_model.sql` (PORTABLE);
      `ISiteScopeProvider.CanActInSiteScopeAsync(userId, tenantId, siteId)` reads
      `site.site_memberships` + `access.memberships` (raw Npgsql, owner connection); ADR-ARCH-009.
      Story-A config provider + CurrentSiteMiddleware + the `X-Site-Id` header removed.
- [~] 5.1i #48 STEP 2 (storage): access invitations schema. `0005_access_invitations.sql` (PORTABLE,
      append-only): `access.invitations` (onboarding workflow — token_hash never the token; status
      pending|accepted|revoked with `expired` DERIVED; tenant_role NULL = external invitee) +
      `access.invitation_sites` (0..N site edges, natural composite PK, ON DELETE RESTRICT, mirrors
      site.site_memberships so acceptance is a COPY). One PARTIAL UNIQUE
      `ux_invitations__tenant_id_email__pending` = "at most one live invitation per (tenant, e-mail)",
      blocks an invitee-driven role escalation. Email is varchar(255) (COPY CONSTRAINT to
      access.profiles.email, NOT RFC 5321's 320). RLS enabled, NO policy (a policy is #50). ADR-ARCH-011.
      Anti-drift `InvitationsSchemaTests` (positive + negative) written; classification 6/6 green
      (non-Docker); the schema test is Docker-gated + unrun here (as #47). STORAGE ONLY — see #48 DEBT.
      This is only STEP 2; the writers (CreateInvitation / AcceptInvitation) are later steps of #48.
- [ ] 5.2 CI path-filters (first workflow, scoped to api) — still deferred (#38)
- [ ] 5.3 Pick OpenAPI -> TS generator (at first client generation)

---

## Epic #44 - Identity model reversal (Milestone 2)

The `tenant_id`-as-a-static-JWT-claim model had a ceiling and was reversed. `tenant_id`
leaves the JWT and leaves `access.profiles`, replaced by `access.tenants` +
`access.memberships`. Action context (tenantId, siteId) travels in the command PAYLOAD,
never in a header (ADR-ARCH-005). Authentication, business identity and authorization
become three distinct concepts.

- [x] #39 — access.profiles + observer claims. Merged, applied. Superseded in substance
      by #44. Established ADR-ARCH-007 (GoTrue writes app_metadata via a post-INSERT
      UPDATE, so no AFTER INSERT trigger on auth.users can ever see it).
- [x] #45 — access schema: SQUASH and rebaseline. Scripts 0002..0006 deleted, replaced by
      `0002_access_identity_model.sql` (PORTABLE, anti-drift tested) and
      `0003_access_identity_auth.sql` (AUTH-COUPLED, checklist-validated).
      No ALTER, no DROP IF EXISTS, **no trigger on auth.users at all**.
      `0001_safety_create_constats` untouched, byte for byte.
      ADR-ARCH-008 supersedes ADR-ARCH-004.
      Validated: 118/118 CI green + manual checklist all-green against the real Supabase.
- [x] #46 — closed as merged into #45.
- [x] #47 — site schema + real ISiteScopeProvider. `0004_site_model.sql` (PORTABLE, anti-drift
      tested): `site.sites` + `site.site_memberships` — TWO independent authorization edges (the site
      edge joins the org edge from #45). `ISiteScopeProvider` is now a relationship-based check
      `CanActInSiteScopeAsync(userId, tenantId, siteId)` reading BOTH edge tables via raw Npgsql
      (branch a: active window-valid site membership — covers EXTERNAL people; branch b: an owner-level
      tenant membership). The config-backed provider, `CurrentSiteMiddleware` and the `X-Site-Id` header
      are gone for good (ADR-ARCH-005). ADR-ARCH-009 (amends ADR-ARCH-008: two edges, not one; retires
      the ADR-ARCH-004 "no Membership model in Site" guardrail). Build + non-Docker tests green
      (architecture 24, migrator classification 5, Site unit 10, Access 8, Safety 40). Docker-gated tests
      (SiteSchemaTests + the 10 provider cases) are WRITTEN and compile, run where Docker is present — no
      CI yet (#38). Closes #20's deferral.
      DEBT (schema-first, intentional — inherited, not rediscovered):
      (1) the two tables have NO writer; create-site / assign-site-member are #48; populated by tests only.
      (2) `ISiteScopeProvider` has NO production caller; first caller is the constat write path (#49+),
          which validates the payload-carried `siteId`.
      GET /me and GET /context stay 403 (dead tenant claim, #49) — VERIFIED still 403, not a new 500:
      `ResolveCurrentSiteHandler` guards the tenant before reading the (now unpopulated) site context.
- [~] #48 — onboarding by invitation (ADR-ARCH-011). `access.profiles` is written EXCLUSIVELY here.
      Until it lands, nothing guards account creation — public signup MUST stay disabled (a property of
      the model now, not a stopgap).
      - [x] STEP 2 (storage): `0005_access_invitations.sql` — `access.invitations` +
        `access.invitation_sites` (see 5.1i). STORAGE ONLY: neither table has a writer yet; the outbox /
        e-mail-delivery table is a SEPARATE later migration (MicroKit.Messaging). RLS enabled, no policy
        (#50). Anti-drift test written; Docker-gated + unrun here.
      - [ ] Later steps: operator tenant provisioning (createUser + profile + owner membership — an
        OPERATOR op, NOT a public endpoint), `CreateSite`, `CreateInvitation` (issuer authz: owner/admin
        for the org edge, site_manager-or-owner per site; roles server-imposed; revoke-then-create),
        `AcceptInvitation` (single account-creation path: createUser+email_confirm, orphan adoption via
        EXACT Admin-API e-mail match, golden-rule e-mail match, idempotent), `GET /me/workspaces`.
- [ ] #49 — request context (payload-carried tenant/site, validated against memberships;
      ProblemDetails). **Repairs `/me`.**
- [ ] #50 — RLS (spike first: `SET LOCAL app.current_tenant_id` against the session pooler
      in transaction mode, and against PowerSync CDC).
- [ ] #51 — Story 1 branch 2, closes #34.

---

## Current position

**#45 merged.** The `access` schema is rebaselined on the target identity model:

- `access.profiles` — the human, 1:1 with `auth.users`. No tenant, no role.
- `access.tenants` + `access.memberships` — the ONLY authorization bridge.
  Revocation is immediate (`is_active = false`), not at token expiry.
- JWT — identity only: `sub`, `email`, `observer_name`, `observer_function`. No tenant claim.
- **No trigger on `auth.users`.** Onboarding is an explicit .NET command (#48).

**Deliberate break: "1 auth user = 1 profile" no longer holds.** A user can exist in
`auth.users`, sign in, and hold a valid token with NO business identity. A valid token no
longer proves access to the product — only a membership does.

**KNOWN BROKEN, by design:** `GET /me` and every tenant-scoped path fail at runtime.
`AccessModuleExtensions` still resolves the tenant from claims, and the claim is gone.
This is #49. **No fallback is to be added.** Do not file it as a regression.

**#47 merged** (site schema + relationship-based authorization, ADR-ARCH-009).

**#48 STEP 2 (storage) implemented** — `0005_access_invitations.sql` + anti-drift tests, ADR-ARCH-011 —
on branch `feature/access/invitations-schema`, uncommitted (the human runs git). STORAGE ONLY: the two
invitation tables have no writer yet. **Next within #48 = the writers** (operator provisioning,
`CreateInvitation`, `AcceptInvitation`, `GET /me/workspaces`). Then #49 -> #50 -> #51.

The migration set is **append-only from here on**. The squash window closed with #45: it was
only legitimate because `access.profiles` was empty, the app was not deployed, and there was
no data to protect. It will not reopen.

---

## Open decisions still pending

- Offline conflict-resolution strategy for safety data (future ADR).
- **MicroKit.Tenancy extension point.** `AddMicroKitAuthMultitenancy` registers
  `AuthTenantResolutionStrategy`, which resolves from CLAIMS. The new model resolves from the
  PAYLOAD, validated against memberships. Clean extension point, or MicroKit follow-up?
  To instruct at #49, not before.
- **PowerSync publication** must include `access.memberships` (parameter queries can read
  source tables, verified session 015). Not done. Belongs to #50 or the first sync slice.
- Naming: bounded-context vs aggregate (module "Site" vs a future "Site" aggregate) —
  revisit at the story that first creates the Site aggregate (#47). Not blocking.

---

## Carried debt (cross-repo / cleanup)

- **API error format:** 403/4xx responses have empty bodies (no ProblemDetails). Wire RFC 9457
  mapping domain Result/errors -> payload, so clients (esp. offline PowerSync, ADR-ARCH-005
  rejection protocol) can surface a rejection reason. Now due at #49, which needs it.
- MicroKit follow-ups from Story #4 (findings 2-4) — tracked in
  `docs/microkit-followups-from-story-4.md`, to be ACTIONED in the MicroKit repo.
- Remove the Microsoft.OpenApi 2.10.0 pin once Microsoft.AspNetCore.OpenApi ships a patched
  transitive (NU1903).
- `core.hooksPath` points to an absent `.githooks` dir in the saas-btp clone — the kickoff
  pre-push hook (force-push guard on main/dev) is NOT active locally. Reconstitute in a
  separate infra pass.
- `Directory.Packages.props` stale header comment (says Auth = preview.2; actual pins are
  preview.3). Cosmetic doc drift.
- **Supabase legacy API keys are removed end of 2026.** New keys (`sb_publishable_` /
  `sb_secret_`) go on the `apikey` header ONLY — adding `Authorization: Bearer` makes the
  platform parse them as a JWT and reject with "Invalid JWT".

RESOLVED, removed from this list:
- ~~Id format gate~~ -> UUIDv7 (`naming.md`).
- ~~Migrations ADR gate~~ -> DbUp (ADR-ARCH-006).
- ~~"Test-user tenant_id set by hand; the real mechanism is a future Access story"~~ ->
  the mechanism it described is DEAD. There is no tenant claim, no trigger, no static
  tenant. `InviteMemberCommand` (#48) is the real mechanism. The debt is resolved by the
  disappearance of its subject, not by being paid.
- ~~"one user = one tenant is a static-claim assumption with a ceiling"~~ -> that is exactly
  what epic #44 paid. `access.memberships` is the answer.

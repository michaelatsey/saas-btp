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
- [~] #48 — onboarding by invitation (ADR-ARCH-011). **REWRITTEN**: the original scope assumed a
      trigger on `auth.users` (removed in #45) and `inviteUserByEmail` as the entry point (breaks on
      an existing e-mail, therefore on the SECOND tenant — the exact capability epic #44 delivers).
      Both are dead. `access.profiles` is written EXCLUSIVELY here. Public signup stays disabled
      PERMANENTLY — a property of the model now, not a stopgap.
      - [x] STEP 1 — GoTrue spike, against the real project. `createUser` on an existing e-mail →
        HTTP 422 / `error_code: "email_exists"`, same signature for an ORPHAN. The 422 carries no id;
        `GET /admin/users?filter=` resolves it, so `auth.users` is NEVER read in SQL.
      - [x] STEP 2 — storage: `0005_access_invitations.sql`. **Merged (PR #58).** See 5.1i.
      - [ ] STEP 3 — operator tenant provisioning (createUser + profile + owner membership; an
        OPERATOR op, NOT a public endpoint). **BLOCKED on the Access persistence decision
        (ADR-ARCH-012) — see Current position.**
      - [ ] STEP 4 — `CreateSite`. Closes the "no writer" debt from #47.
      - [ ] STEP 5 — `CreateInvitation`. Issuer authz: owner/admin for the org edge; `site_manager`
        (or a cross-site tenant role) per site — **participation is NOT permission to administer**.
        Roles are server-imposed, never read from the request.
      - [ ] STEP 6 — `AcceptInvitation`. The single account-creation path: profile lookup → createUser
        → orphan adoption on 422. Golden rule: authenticated e-mail == invited e-mail. Idempotent.
      - [ ] STEP 7 — `GET /me/workspaces`.

      MANDATORY at STEP 5/6 — inherited, not to be rediscovered:
      - **E-mails normalized (lower + trim) BEFORE writing, and a TEST proves it.**
        `ux_invitations__tenant_id_email__pending` compares RAW strings — Postgres does not fold case.
        Two assertions: `" Paul@Mail.com "` → stored as `paul@mail.com`; and `PAUL@MAIL.COM` on an
        existing `pending` → UNIQUE violation, not a second live token. Without them, the
        anti-escalation guard silently stops guarding.
      - **Re-inviting = REVOKE-then-CREATE in ONE transaction.** The partial unique makes it
        structurally impossible otherwise — and it is the correct behaviour: the previous token must
        die before a new one is minted.
      - **The Admin API e-mail lookup is a PREFIX SEARCH, not an equality.** Exact normalized match;
        `Count == 1` → adopt; `Count > 1` → SECURITY FAILURE, abort. `.First()` would attach a business
        identity to the WRONG PERSON — an impersonation, not a bug.
      - **`AcceptInvitation`: NO compensation logic.** An orphaned `auth.users` row is ADOPTED on
        retry (read-before-write IS the recovery). Deleting it would race with a concurrent retry.
      - **Invitation validity: 7 days, in CONFIGURATION.** Never hard-coded, never a DB `DEFAULT`.
      - **The outbox / e-mail-delivery table is a SEPARATE later migration.** Not in 0005.
- [ ] #55 — existing-identity management: `AssignSiteMember`, `AssignTenantMember`, `ChangeRole`,
      `RevokeMembership`. The invitation is an INDIRECT writer of the site edge, only at acceptance —
      an existing employee joining a second site must NOT be e-mailed "create your account". After #48.
- [ ] #49 — request context (payload-carried tenant/site, validated against memberships;
      ProblemDetails). **Repairs `/me`.**
- [ ] #50 — RLS (spike first: `SET LOCAL app.current_tenant_id` against the session pooler
      in transaction mode, and against PowerSync CDC).
- [ ] #51 — Story 1 branch 2, closes #34.

---

## Current position

**#48 — 2 of 7 steps done.**

- [x] **STEP 1 — GoTrue spike**, against the real project. Closed four Open items of ADR-ARCH-011.
      `createUser` on an existing e-mail → **HTTP 422, `error_code: "email_exists"`** — the SAME
      signature for an ORPHAN (`auth.users` row with no profile) as for a fully-onboarded user, which
      is what makes **deterministic adoption** implementable. The 422 carries **no id**;
      `GET /admin/users?filter=` resolves it, so **`auth.users` is NEVER read in SQL**.
      ⚠️ That `filter` is a **PREFIX SEARCH**, not an equality — exact normalized match required,
      `Count == 1` to adopt, `Count > 1` = abort. `.First()` would be an impersonation.
- [x] **STEP 2 — storage**, `0005_access_invitations.sql`. **Merged (PR #58)**, tests green on Docker.
- [ ] **STEP 3 — operator tenant provisioning ← NEXT. BLOCKED, see below.**
- [ ] STEP 4 — `CreateSite` · STEP 5 — `CreateInvitation` · STEP 6 — `AcceptInvitation` ·
      STEP 7 — `GET /me/workspaces`

**The model (ADR-ARCH-011): the TOKEN is the authorization to exist in the product.** `createUser`
(Admin API) is the ONLY account-creation path. `inviteUserByEmail` is NOT usable — Supabase states
explicitly it is not built for multi-tenant apps, and it fails on an existing e-mail (i.e. it breaks
on the second tenant, the exact capability epic #44 delivers).

**Public signup stays DISABLED permanently.** It is a property of the model, not a stopgap until #48.

**The two authorization edges are INDEPENDENT** (ADR-ARCH-009/010): a subcontractor holds a site
membership with NO organization membership. Externality is a RELATIONAL FACT, not a role — there is
no `sub_contractor` token, ever.

**KNOWN BROKEN, by design:** `GET /me` and `GET /context` still 403 (dead tenant claim). That is #49.
No fallback. Not a regression.

The migration set is **append-only**. The squash window closed with #45 and will not reopen.

---

## 🔴 BLOCKER before #48 STEP 3 — Access has no persistence layer

STEP 3 writes to THREE tables transactionally (`tenants`, `profiles`, `memberships`) AND calls the
GoTrue Admin API. **Access has NO `DbContext`** — `SiteMembershipScopeProvider` (#47) reads
`access.memberships` in plain Npgsql.

**EF Core or plain Npgsql?** This decision governs steps 3, 5, 6 and every future Access command. It
must be taken BEFORE any code is written.

Direction agreed, **NOT ratified**: an `AccessDbContext`, **EF as a MAPPER only**. DbUp stays the sole
DDL owner and EF migrations stay disabled (ADR-ARCH-006) — exactly what `SafetyDbContext` already
does. This is not a new architecture; it is the existing one applied to Access.

**It needs ADR-ARCH-012, and the ADR needs FACTS first.** Two unknowns, to establish by READING:
1. How `SafetyDbContext` is actually wired. Note from #47: **`AddSafetyModule` is not even invoked by
   the Host** — EF has never run in production in this repo.
2. What becomes of `SiteMembershipScopeProvider`. If Access gains a DbContext, there are TWO read
   mechanisms on `access.memberships`, one of which does not share the transaction. Not blocking (it
   is a read), but it must be NAMED, not discovered.

**Unpriced consequence:** MicroKit's outbox REQUIRES a `DbContext` (`EfOutboxStore<TContext>`). If
Access gets one, STEP 5 can consume the outbox **without waiting** for the `NpgsqlOutboxWriter` — the
MicroKit issue becomes useful, not blocking.

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
- 🔴 **THE UNVALIDATED ASSUMPTION (ADR-ARCH-010).** The site is the authorization grain: a
  subcontractor assigned to a site sees EVERYTHING on it, including other lots. **Nobody on this
  project has field knowledge of the Ivorian BTP market.** Put this to the business partners BEFORE a
  single membership row exists in production:
  > *When a subcontractor (plumbing, electrical) works on a site: must they see ALL the safety
  > findings on it, including those outside their lot? Or only what touches their lot?*
  If the answer is "only their lot", **the grain reopens — and it must reopen while
  `site.site_memberships` is still EMPTY.** This is the ONE decision on record whose cost is
  asymmetric in time: near-zero today, very high once production rows exist.
- **Invitation delivery channel.** ADR-ARCH-011 leaves the e-mail provider open. The token and the
  link are independent of the TRANSPORT — for the Ivorian field, WhatsApp may beat e-mail. A PRODUCT
  decision, due before STEP 5.
- **MicroKit.Messaging is not consumable by saas-btp** (its own flagship consumer). Four gaps:
  hard-coded PascalCase/no-schema naming, EF hard-wired for the outbox write, no canonical DDL for a
  DbUp-owned consumer, inbox forced. Issue drafted LOCALLY, not committed. Blocking STEP 5 unless
  Access gets a DbContext.
- **ADR immutability vs amendment.** `decisions-index.md` says an ADR is "immutable once accepted,
  and superseded (never edited)". ADR-ARCH-011 was amended THREE times in one day (spike findings,
  security rule, operator provisioning) — none of them reversing a decision, all of them adding
  facts established afterwards. Either the rule needs refining ("immutable on the decision, amendable
  on facts established later"), or we stop amending. Not urgent; do not decide it under pressure.

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

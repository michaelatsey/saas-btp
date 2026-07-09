# Session — 2026-07-08 (Site story split: #17 CurrentSite (Story A) + #20 Membership (Story B))

Continues: 008-2026-07-08-access-merged-site-split.md.
Purpose: record the decision to split the combined Site story #17 into two bounded,
differently-paced stories — CurrentSite (execution context) and Membership (durable
business model) — and the tracker operations that enacted it. No code this session;
backlog + specs only.

## The decision

Session 008 moved site scope out of Access into a single Site story #17
("membership + currentSite + resolution + switch"). Reviewing it showed it still
conflated two concepts of different nature and cadence:

- CurrentSite = execution context (which site am I working on now). User session
  state, switchable, NOT an identity/JWT claim. Depends on #4 only.
- Membership = durable user<->site<->role relation. Subject of ADR-ARCH-004
  (ownership open: org-level grant vs site-level assignment).

Keeping them in one story re-blocked the buildable half (CurrentSite) behind the
gated half (Membership, gated ADR-ARCH-004), and thus blocked Safety on a modeling
decision it does not need — Safety only needs a currentSiteId to attach a constat.
Same artificial-dependency mistake #4 taught us to avoid.

Decision: split #17.
- Story A (#17, recadrée) — Site Context / CurrentSite. Depends on #4 only. Unblocks Safety.
- Story B (#20) — Site Membership. Gated on ADR-ARCH-004. Backlog, not in milestone #1.
- Story C (Site Administration: CRUD, invitations, archival) — NOT created. No
  consumer in increment 1 = YAGNI. Emerges from a real need later.

## Story A (#17) — scope frozen

- Ports/impl: ISiteScopeProvider (port) + ConfigurationSiteScopeProvider (provisional,
  appsettings-seeded, mirrors #4's ConfigurationTenantStore). The seam Story B swaps
  (MembershipSiteScopeProvider) without changing the API.
- CurrentSite resolution middleware reads X-Site-Id (client-provided, never trusted;
  validated server-side every request).
- GET /context returns { user, tenant, roles, currentSite?, availableSites[] }.
  GET /me unchanged (identity only). No rename.
- Validation semantics (anti-Membership boundary, graved in the issue): validates
  site-belongs-to-tenant, NOT user-belongs-to-site. Two checks (tenant resolved ->
  site in tenant catalogue); no third "user authorized on site" check — that IS
  Membership. availableSites = tenant catalogue, NOT a membership list, and NOT an
  authorization source (feature modules must not use it for access decisions).
- roles in /context = org-level roles from claims (same as /me), provided by the
  auth/tenancy layer. Site-level roles do not exist in Story A.
- No POST /context/site (switch = client changes header). No single-site fallback
  (currentSite null when no X-Site-Id). No DbContext/EF, no Membership (ADR-ARCH-004
  guardrail extended to the Site module).
- Accepted temporary consequence: within a tenant, any authenticated user may scope
  any site of that tenant — bounded by tenant isolation (#4's hard boundary), tightened
  by Membership (Story B).

## Story B (#20) — gated placeholder

- Owns the durable membership model, SHAPED BY ADR-ARCH-004 (do not pre-assume a single
  Membership aggregate; the ADR may yield a two-level split: org-level grant in
  Access/Tenancy + site-level assignment/role binding in Site).
- First product persistence (first DbContext + Supabase schema + MicroKit.Persistence
  entry) — larger than #4 and Story A, both persistence-free.
- Replaces ConfigurationSiteScopeProvider with MembershipSiteScopeProvider behind
  ISiteScopeProvider — GET /context contract unchanged. Adds per-user site filtering
  (the check Story A omits).
- Security hardening, not UX: until it ships, Story A's accepted over-permission stands
  (fine for a controlled increment, not for complex multi-user production).
- prio:normal, no milestone (backlog). Blocked until ADR-ARCH-004 is resolved.

## ADR posture

- ADR-ARCH-004 intact and immutable. No ADR-ARCH-005 created now. The
  CurrentSite<->Membership independence is stated in Story A's body, not a new ADR. An
  ADR would only emerge as Story B's gate if the ownership decision must be frozen.

## Attack order (updated)

#4 Access (done) -> #17 Story A (CurrentSite) -> Safety (increment 1) ; ADR-ARCH-004
+ #20 Story B (Membership) come later (hardening), off the Safety path.

## Tracker state

- #17 recadrée -> Story A "Site Context: resolve current site (CurrentSite)"
  (ctx:site, type:story, prio:high, milestone #1). Body replaced.
- #20 created -> "Site Membership: durable user-site-role model (gated
  ADR-ARCH-004)" (ctx:site, type:story, prio:normal, no milestone). Added to board #9.
- #5-#10 (Safety/corrective/notifications) unchanged.

## Next / re-entry

Decide before any code:
1. Frame + implement Story A (#17): scaffold the Site module in apps/api
   (ISiteScopeProvider + ConfigurationSiteScopeProvider + CurrentSite middleware +
   GET /context), config/claims only, no EF, no Membership. Unblocks Safety.
2. OR clear the MicroKit follow-ups from #4 first (findings 2-4, MicroKit repo, branch
   docs/findings-story-4-integration).

### Re-entry prompt for the web orchestration chat

"On reprend le SaaS BTP. Lis la derniere session
(009-2026-07-08-site-split-currentsite-membership.md) et build-checklist.md. Etat : la
story Site combinee #17 a ete scindee — #17 = Story A (CurrentSite, execution context,
depend de #4 seul, debloque Safety) et #20 = Story B (Membership, gated
ADR-ARCH-004, backlog hors milestone #1). Story A est cadree et figee dans l'issue.
Prochaine decision : soit cadrer/implementer Story A (#17), soit traiter d'abord les
findings MicroKit. Cadre-moi le choix avant tout code, une etape a la fois, doc
gh/MicroKit verifiee, jamais --delete-branch quand la head est dev."

## Open items / debt (carried, unchanged)

- MicroKit follow-ups from #4 (findings 2-4) — docs/microkit-followups-from-story-4.md.
- Remove the Microsoft.OpenApi 2.10.0 pin once Microsoft.AspNetCore.OpenApi ships a
  patched transitive (NU1903).
- Offline conflict-resolution strategy for safety data (future ADR).
- Back-button behavior in the installed PWA on Android: validate in the real client.
- Token refresh after long offline sessions (real-client architecture requirement).
- Multi-photo per constat + gallery picker (product UI).
- Cold tooling: Context7 auth, powersync-ja/agent-skills eval, persistent corepack in .zshrc.

# Session — 2026-07-08 (Access #4 merged; bounded-context re-split -> Site #17)

Continues: 007-2026-07-08-access-scaffold-me.md.
Purpose: record what happened after 007's scaffold was written — the manual
commit/merge of #4, and the backlog re-split that moved site scope out of Access
into a dedicated Site story. 007 is left as-is (historical record of the
implementation session); this session is the accurate re-entry point.

## What happened since 007

### #4 (Access) committed and merged
- Single commit feat(access): scaffold apps/api + Access module + ResolveCurrentContext
  slice, on branch feature/access/scaffold-resolve-current-context.
- PR opened base dev, squash-merged. dev now carries apps/api (Access, GET /me,
  claims-only), all gates green (build/arch/unit). Live Supabase /me still owner-run.

### MicroKit.Auth preview.3 released (the fix #4 surfaced)
- The CS8852 finding (SupabaseAuthOptions init-only vs Action<T> overload) was fixed
  in the MicroKit repo: init -> set, mirroring JwtOptions (ADR-AUTH-007), plus a
  compile-time regression test covering all five settable props. api-reviewer PASS,
  no .csproj change (dependency-guardian N/A). Released auth-v1.0.0-preview.3 to
  nuget.org, back-merged main->dev, CPM bumped (Auth.Abstractions + Auth.Permissions
  -> preview.3, the only internally-consumed Auth packages). SaaS BTP consumes
  preview.3.

### Backlog re-split — the real decision of this session
- Reviewing #4's original acceptance criteria (sign in + tenant scope + a current
  siteId + reject invalid tenant/site scope + attached to >=1 site) against what
  shipped showed 3 of 5 criteria are about siteId/site — none delivered.
- This was diagnosed as a story-decomposition error, not a technical blocker: #4
  mixed two bounded contexts (Access + Site) in one story. ADR-ARCH-004 surfaced it.
- Key distinction made (do not conflate):
  - Membership = which sites a user belongs to, with which role (durable relation).
    Subject of ADR-ARCH-004 (ownership open: org-level grant vs site-level assignment).
  - CurrentSite = which site the user works on now — a user session state, chosen or
    restored, switchable. NOT an identity property, NOT a JWT claim. This retroactively
    validates keeping siteId out of the token: the current site cannot be a claim.
- Decision: do NOT create a "#4b = finish #4". Realign the backlog on bounded contexts:
  - #4 (Access) closed on its real perimeter: auth, identity, tenant, claims, current
    user. Delivered.
  - #17 (Site) created to own membership, current-site resolution, switch-site.
  - ADR-ARCH-004 to be settled at #17's threshold, informed by its concrete cases
    (multi-site conducteur, Contractor external member, per-site role), before
    membership persistence.
  - Safety stories depend on Site (a BTP constat is intrinsically attached to a
    chantier) — Site precedes Safety in the attack order.

### Tracker state
- Issues: #3 (spike) closed + Done; #4 (Access) closed on Access perimeter + Done;
  #17 (Site) open + active on board #9. #5-#10 (Safety/corrective/notifications)
  unchanged in backlog.
- MicroKit follow-ups from #4 (findings 2-4) still pending, to be actioned in the
  MicroKit repo on branch docs/findings-story-4-integration
  (list: docs/microkit-followups-from-story-4.md). Finding 1 (CS8852) done in preview.3.

## Next / re-entry

Two candidate next moves; decide before any code:
1. Frame Story #17 (Site) — the next real product work. Sequence: scope #17 (its
   concrete cases feed ADR-ARCH-004) -> settle ADR-ARCH-004 -> implement Site
   (membership + currentSite + resolution + switch) -> then Safety. #17 will likely
   be the first product persistence (first DbContext + Supabase schema + MicroKit.
   Persistence entry) — larger than #4, size accordingly.
2. Clear the MicroKit follow-ups from #4 first (session in the MicroKit repo, branch
   docs/findings-story-4-integration): ship an ASP.NET Core Supabase auth scheme
   (finding 2), TenantId TypeConverter/IParsable + README (finding 3), document the
   mandatory tenant store (finding 4). These improve the libraries before Site
   consumes them more heavily.

### Re-entry prompt for the web orchestration chat

"On reprend le SaaS BTP. Lis la derniere session
(008-2026-07-08-access-merged-site-split.md) et build-checklist.md. Etat : Story #4
(Access) fermee et mergee sur dev (auth + identity + tenant + claims-only GET /me).
Le backlog a ete re-decoupe : le site-scope est sorti d'Access vers la story Site #17
(membership + currentSite + switch), ADR-ARCH-004 a trancher au seuil de #17. Safety
depend de Site. Les follow-ups MicroKit issus de #4 (findings 2-4) restent en attente
(repo MicroKit, branche docs/findings-story-4-integration, liste
docs/microkit-followups-from-story-4.md). Prochaine decision : soit cadrer #17 (Site),
soit traiter d'abord les findings MicroKit. Cadre-moi le choix avant tout code, une
etape a la fois, doc gh/MicroKit verifiee, jamais --delete-branch quand la head est dev."

## Open items / debt (carried from 007, unchanged)

- Remove the Microsoft.OpenApi 2.10.0 pin once Microsoft.AspNetCore.OpenApi ships a
  patched transitive (likely recurs in MicroKit too — replicate the pin there when it
  first pulls OpenApi).
- MicroKit follow-ups from #4 (findings 2-4) — docs/microkit-followups-from-story-4.md.
- Offline conflict-resolution strategy for safety data (future ADR).
- Back-button behavior in the installed PWA on Android: validate in the real client.
- Token refresh after long offline sessions (real-client architecture requirement).
- Multi-photo per constat + gallery picker (product UI).
- Cold tooling: Context7 auth, powersync-ja/agent-skills eval, persistent corepack in .zshrc.

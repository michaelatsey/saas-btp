# Session — 2026-07-06 (Handoff: next step = Story #4 Access)

Continues: 005-2026-07-06-spike-powersync.md.
Purpose: record the post-spike sequencing decision and how to resume cleanly next
session without losing context. No code written this session — planning/handoff only.

## Where we are

- Spike #3 done, ADR-ARCH-003 cleared (PWA retained, Expo Phase 2). PR #12 + #13 merged
  to dev. Teardown complete. Disposable harness gitignored (only findings.md tracked).
- Build-checklist: Step 5.0 complete. Steps 5.1/5.2/5.3 and Step 4 still open.

## Sequencing decision (taken this session)

- "Step 5.1 scaffold apps/api" is NOT a standalone step done in a vacuum. It is the
  first act of Story #4 (Access), on the Access bounded context. Product-First: no empty
  modular monolith built "to see" — scaffold only the minimal skeleton needed to carry
  the first real behavior (auth + tenant/site scoping).
- Attack order (from session 004) confirmed: Story #4 (Access, foundational) is the
  first domain slice. Architecture.md: Access + Site are foundational; every context
  assumes an authenticated, tenant-scoped user.
- btp-* agents do NOT exist yet. They emerge JIT from the friction of Story #4 done by
  hand first. An agent is justified only by fresh-context JUDGMENT (e.g. arch/scope
  guard), not by encoding a procedure (that is a SKILL/rule). Do not build agents on spec.
- CI path-filters (5.2) and OpenAPI->TS generator (5.3) stay JIT: triggered by the first
  real need inside #4 (first api-scoped workflow; first client generation), not before.

## Known tension to weigh at the START of Story #4 (not a blocker)

- Story #4 is the FIRST external consumption of my own MicroKit.Auth + MicroKit.Tenancy
  (from NuGet) inside a real product. Expect integration friction (versions, DI, Supabase
  auth wiring). This is healthy — proving MicroKit on a real product is a goal — but it
  means #4 is also the first integration test of the ecosystem, so do not under-size it.

## Next session — exact re-entry

### In the web AI orchestration chat, say:

"On reprend le SaaS BTP. Lis la derniere session (006-2026-07-06-next-step-access-handoff.md)
et build-checklist.md. On demarre Story #4 (Access) : scaffold minimal apps/api (.NET 10
modular monolith) portant le module Access sur MicroKit.Auth + MicroKit.Tenancy, auth
Supabase. Cadre-moi le prompt Plan Mode avant tout code. Une etape a la fois, doc gh/
MicroKit verifiee, jamais --delete-branch quand la head est dev."

### In Claude Code (WSL2), the first move is:

- Activate Plan Mode BEFORE any prompt.
- Read: CLAUDE-SAAS-BTP.md, architecture.md (Access/Site sections), the MicroKit.Auth +
  MicroKit.Tenancy READMEs/NuGet versions, issue #4 on GitHub.
- Ask for a PLAN only: minimal apps/api skeleton (one module: Access) on MicroKit.Auth +
  Tenancy, Supabase auth wiring, nothing else (no Safety/CorrectiveActions/other modules).
  Flag anything drifting beyond Access as out of scope.

## Open items / debt (carried)

- Offline conflict-resolution strategy for safety data (future ADR).
- Back-button behavior in installed PWA on Android: validate in the real client.
- Token refresh after long offline sessions (real-client arch requirement).
- Multi-photo per constat + gallery picker (product UI).
- Cold tooling: Context7 auth, powersync-ja/agent-skills eval, persistent corepack in .zshrc.

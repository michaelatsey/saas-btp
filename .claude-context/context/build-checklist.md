# Build Checklist - Phase 1 (Increment 1: Safety constat -> Corrective action)

Progress tracker from project bootstrap to the first story ready to code.
Update this file as items complete. Read it first in a new session to know where we are.

Last updated: 2026-07-03.

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

- [ ] 3.1 Decompose slice 1 into vertical stories (offline capture -> corrective
      lifecycle -> sync -> dashboard)
- [ ] 3.2 Create Issues via gh, labeled + assigned to milestone
- [ ] 3.3 Define attack order (first story to code)

---

## Step 4 - AI OS Palier 1 tooling (JIT, after scope)

- [ ] 4.1 Core btp-* agents (product, architect, implementer, reviewer, ux) - one by one
- [ ] 4.2 (optional) Notion hub for steering
- [ ] 4.3 First story implemented via agent flow -> observe where it hurts

---

## Step 5 - First application code (triggers JIT infra)

- [ ] 5.0 Technical spike: PowerSync Web offline + camera on target Android (gates ADR-ARCH-003)
- [ ] 5.1 Scaffold apps/api (.NET 10 modular monolith) on the first bounded context
- [ ] 5.2 CI path-filters (first workflow, scoped to api)
- [ ] 5.3 Pick OpenAPI -> TS generator (at first client generation)

---

## Current position

Step 2 complete (tracker foundation: convention, labels, templates, milestone, board).
Next: Step 3 - decompose slice 1 into vertical stories as Issues.

## Open decisions still pending

- Offline conflict-resolution strategy for safety data (future ADR).

# Session — 2026-07-03 (Step 2: GitHub Issues foundation)

Continues: 002-2026-06-28-scope.md.
Goal: Step 2 — build the tracker foundation (issues convention, labels, templates,
milestone, project board), then commit and open a PR to dev.

---

## Starting context

- Step 1 (scope + bounded contexts) merged to dev via PR #1 (squash, commit b5ac2cf).
- Branch for this work: feature/product/issues-convention (from dev).

---

## Completed

### 2.1 — Issues convention (repo file)

- Created context/product/issues-convention.md.
- Title format: [ctx:<context>] <type>: <imperative description>.
- Labels taxonomy: ctx:* (six Increment 1 contexts), type:* (story/spike/chore/bug),
  prio:* (critical/high/normal/low), plus offline.
- DoR / DoD defined. DoD point 4 tied to decisions-index.md ADR governance
  (domain prefixes, per-domain numbering, template, supersede-never-edit).
- Section on Milestones = Increments. Section on Relationship to ADRs (issues track
  work, ADRs track decisions).
- Consistency review (via Claude Code) surfaced two points, both resolved:
  - ctx:* list initially omitted Access/Site (foundational). Resolved: the six
    Increment 1 contexts all get a label now; wording tightened. Matches
    architecture.md section 7.
  - Photo rule corrected: critical-severity constat requires a photo enforced at the
    capture (UX/application) layer — photos remain optional in the domain model per
    constat.md. Not a hard model invariant.
- File is now fully ASCII (milestone title + two prose em-dashes converted).

### 2.2 — Labels (GitHub state, not a repo file)

15 labels created via gh label create, then default GitHub labels deleted (9 removed:
bug, documentation, duplicate, good first issue, help wanted, invalid, question,
wontfix, enhancement). Final set = exactly the 15 below, colors all distinct:

- ctx:access (0052CC), ctx:site (1D76DB), ctx:safety (5319E7),
  ctx:corrective-actions (006B75), ctx:media (0E8A16), ctx:notifications (BFD4F2)
- type:story (A2EEEF), type:spike (C2E0C6), type:chore (C5DEF5), type:bug (EE0701)
- prio:critical (B60205), prio:high (D93F0B), prio:normal (FBCA04), prio:low (FEF2C0)
- offline (FF8C00)

### 2.3 — Issue templates (repo files)

Created .github/ISSUE_TEMPLATE/:
- story.yml  (auto-label type:story; required ctx dropdown, 6 options)
- spike.yml  (auto-label type:spike; required ctx dropdown, 6 + "none (cross-cutting / infra)")
- bug.yml    (auto-label type:bug;   required ctx dropdown, 6 + none)
- chore.yml  (auto-label type:chore; required ctx dropdown, 6 + none)
- config.yml (blank_issues_enabled: false — every issue routes through a form)

Design: ctx:* selection is captured as body text (GitHub can't map a dropdown to a
label); applying the ctx:* label stays a manual post-creation step. Only type:* is
auto-applied. Decision to build all four forms now (not just story): three static YAML
files carry no architectural decision and near-zero maintenance cost, so JIT does not
apply; uniform discipline (web + mobile) wins.

### 2.4 — Milestone (GitHub state)

Created via gh api POST /repos/michaelatsey/saas-btp/milestones.
- number: 1
- title: "Increment 1 - Safety -> Corrective action" (ASCII, matches convention section 5)
- description: Safety constat -> Corrective action -> dashboard (ADR-PROD-001),
  offline PWA (ADR-ARCH-003).
- state: open, no due date.

### 2.5 — Project board (GitHub state)

- Added project scope to gh token (gh auth refresh -s project).
- Created Project v2 "SaaS BTP" via gh project create.
  - number: 9
  - id: PVT_kwHOBAW7d84BcY2F
  - url: https://github.com/users/michaelatsey/projects/9
- Linked to repo: gh project link 9 --owner michaelatsey --repo saas-btp.
- Board view + Status field configured in web (CLI can't edit single-select options
  cleanly). Status options = gates, in order:
  - Backlog (gray) — created, not yet passed DoR
  - Ready (blue) — passed DoR, ready to code
  - In Progress (yellow) — active on a feature branch
  - In Review (purple) — under DoD verification (tests, ADR, invariants, PR to dev)
  - Done (green) — DoD met, merged to dev
- Default value: Backlog. Default repository set to saas-btp. README + short
  description set on the project.

---

## Environment notes (not blocking, deferred)

- WSL2 interop is disabled on this session: gh ... --web fails
  (/proc/sys/fs/binfmt_misc/WSLInterop missing). Worked around by opening URLs
  manually in the Windows browser. Fix later (check /etc/wsl.conf [interop] and
  Windows .wslconfig), out of roadmap scope.

---

## Current position

- Step 2 complete: convention + 15 labels + 4 issue forms + config + milestone #1 +
  board #9.
- Branch feature/product/issues-convention: files staged, not yet committed.
- Next: two commits (chore for the repo files, docs(session) for this file), push,
  PR to dev.

---

## Next session — Step 3

Decompose Increment 1 slice (Safety constat -> Corrective action -> dashboard) into
vertical stories as GitHub Issues:
- 3.1 Decompose into vertical stories (offline capture -> corrective lifecycle -> sync
  -> dashboard).
- 3.2 Create Issues via gh (or web forms), labeled + assigned to milestone #1 + added
  to board #9.
- 3.3 Define attack order (first story to code).

Then Step 4 (AI OS Palier 1: core btp-* agents, JIT) and Step 5 (first app code +
PowerSync spike that gates ADR-ARCH-003).

---

## Open items

- Offline conflict-resolution strategy for safety data (future ADR).
- PowerSync-web offline + camera spike (gates ADR-ARCH-003) — before Step 5.
- WSL2 interop disabled — fix out of band.

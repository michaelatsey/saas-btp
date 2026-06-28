# AI OS — Roadmap (the 6 paliers)

> The path from "5 manual agents" to the full AI Operating System.
> Each palier has an exit criterion. Do not advance until it is met.
> Current position is marked with `>>>`.

---

## >>> Palier 1 — Hub + core manual agents

Status: in progress

- Notion as the product hub (backlog, user stories, ADRs reference).
- Core agents, invoked manually, in separate Claude Code sessions:
  `btp-product`, `btp-architect`, `btp-implementer`, `btp-api-reviewer`, `btp-ux` (light).
- Deliver ONE vertical BTP feature end to end, by hand.
- Observe where it hurts.

Exit criterion: one Phase 1 feature shipped; pain points noted in a session log.

---

## Palier 2 — Specialization

Status: not started

- Split an overloaded agent into dedicated roles ONLY when the overload is felt
  (e.g. `btp-ux` -> `ux-research` + `ui` when "understand the user" and "draw the
  screen" visibly collide in one agent).
- Each split recorded as an ADR-ORG with the triggering pain.

Exit criterion: at least one justified split, documented.

---

## Palier 3 — Figma in the loop

Status: not started

- Bring Figma into the chain for one feature: spec -> wireframe -> maquette.
- Manual trigger. Learn the spec->design handoff in isolation.

Exit criterion: one feature designed via the spec->Figma path, founder-validated.

---

## Palier 4 — Structured Git / CI for the SaaS

Status: not started

- Apply the proven MicroKit Git + CI discipline to the SaaS repo
  (branch flow, conventional commits, CI gates, Coolify deploy).

Exit criterion: a feature merged and deployed through the structured pipeline.

---

## Palier 5 — n8n automation of rodé handoffs

Status: not started

- Automate with n8n ONE transition already performed by hand 5+ times that has
  become tedious. Never before.

Exit criterion: one manual handoff replaced by a reliable, debuggable n8n flow.

---

## Palier 6 — The AI OS runs

Status: not started

- A new feature flows through a near-complete pipeline with the founder mostly
  validating, not executing.
- The method is documented well enough to teach.

Exit criterion: both conditions of the AI OS "definition of done" hold
(see ai-os-vision.md section 5).

---

## Progress log

| Date | Palier | Event |
|------|--------|-------|
| 2026-06-28 | 1 | Project structure stood up; cap and roadmap recorded |

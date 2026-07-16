# Process-First — Agent Blueprint (draft, run-01)

Source: derived from session 021 (021-2026-07-15-bp-001-process-first-first-run.md),
the first full run. This file is a cumulative journal: run-02 and run-03 append their
evidence and corrections here before anything is built.

Status: DRAFT after ONE full run (BP-001). NOT to be built yet — rule of three.
Purpose: capture, while fresh, the target agent/subagent/rule/skill layout and the
working flow, so a future session can rebuild it without losing this run's experience.
Each item is intentionally MINIMAL: role + trigger + one-line responsibility. Content is
fleshed out only when the rule-of-three (2-3 runs) confirms the pattern is real, not an
accident of run-01.

---

## Why an agent at all (the honest case)

Run-01 showed the method WORKS but is HARD to hold: the operator had to block the
assistant repeatedly from two drift patterns (existing schema dictating the model;
future hypotheticals imposing present complexity). An agent's value is exactly that —
carrying the discipline the assistant keeps reaching to break. But an agent extracted
from one run would grave that run's accidents as rules. Hence: blueprint now, build
after run-02/03.

---

## Target layout

### 1. Primary agent

**`process-first-architect`**
- Trigger: any new feature / module / process to design.
- Responsibility: drive the 11-step method (0-9 model → 10 impl design → 11 ADRs),
  enforce the gates, never let infrastructure or existing schema enter before step 10.
- Owns the STOP signals (see Rules). Does NOT write code — hands a validated design to
  the implementer.

### 2. Sub-agents (fresh-context judgment, not contaminated by the author)

- **`domain-boundary-reviewer`** — checks that each task/entity carries ONE
  responsibility, that no future hypothetical imposed present complexity, that "minimal
  ≠ poor" holds (descriptive vs lifecycle properties).
- **`decision-classifier`** — for every decision surfaced, forces Category A (conceptual,
  write now) vs B (technical, defer to step 10/11). Prevents both premature ADRs and
  buried decisions.
- **`existing-model-confronter`** — the ONLY agent allowed to read existing ADRs/schema,
  and only AFTER the model is validated. Enforces "model decides, existing confirms or
  reveals a gap" — never "existing dictates". Must READ an ADR before any amendment.
- **`invariant-auditor`** — places each INV/RULE on the state model, verifies no state
  violates it, checks an invariant is TESTED not merely stated ("an untested invariant
  is a comment").
- **`adr-scribe`** — writes ADRs at the right level (decision that outlives the process,
  not a description of it), with alternatives and divergences named.

### 3. Rules (always-on, the STOP signals)

- **R1 — No infra before step 10.** Any stack/table/provider name in steps 0-9 = the
  model is not yet conceptual. STOP.
- **R2 — Existing model never dictates.** If a decision is being made because "the table
  already looks like this", STOP and re-derive from the domain.
- **R3 — Future informs boundaries, not present form.** If complexity is added "because a
  future process will need it", STOP — defer to that process.
- **R4 — Apply the owner's test.** For any field/entity/edge: "does THIS process actually
  produce/exercise this?" If no → it belongs to another process.
- **R5 — One problem at a time; one PR per axis** (WHAT vs authorization vs HOW). Never
  merge a domain revision and a technical decision in one PR.
- **R6 — Amend an ADR only after READING it.** Never from memory.
- **R7 — Category A written at step 6; Category B deferred to step 11.** No speculative
  ADR; no buried decision.
- **R8 — GPT cross-reviews are arbitrated, not adopted.** Accept/reject with explicit
  rationale.
- **R9 — Git discipline** (inherited): gh CLI from WSL2, one step at a time, confirmation
  before destructive actions, `--body-file` in a unique /tmp file removed at the end,
  never `--delete-branch` when head is `dev`.

### 4. Skills (SKILL.md — mechanical procedures, not judgment)

- **`adr-writing`** — ADR file format, numbering per domain, index update, amend-vs-
  supersede convention. (Mechanical: how to WRITE one, not whether to.)
- **`session-trace`** — session file format, numbering, "what shipped / decisions / do
  not lose / next-session prompt" structure.
- **`pr-scripting`** — the branch + commit + `--body-file` PR script pattern, with the
  git guardrails baked in.
- **`spec-writing`** — the BP-XXX spec structure (frame, boundary, tasks, contracts,
  rules, state model, Mermaid, decisions, traceability).

---

## The working flow (agencement)

    [process-first-architect] drives:

    Step 0-5   model              → domain-boundary-reviewer + invariant-auditor gate
    Step 6     decision discovery → decision-classifier splits A/B
                                   → adr-scribe writes Category A now
    Step 7-8   states + diagram   → invariant-auditor verifies placement
    Step 9     VALIDATION GATE    → human. Nothing technical before this passes.
    ----------------------------- infra allowed only past this line -----------------
    Step 10    impl design        → existing-model-confronter enters HERE (not before)
                                   → decisions B resolved
    Step 11    adr-scribe writes Category B ADRs
    ----------------------------- code only past this line --------------------------
    Implementer prompt (English), then Claude Code executes a VALIDATED design.

    Rules R1-R9 are always-on across every step.
    One PR per axis (R5): domain PR, authorization PR, impl PR — never merged.

---

## Run-01 evidence (why each item exists)

- domain-boundary-reviewer: run-01 nearly fused Organization/Workspace, nearly gave
  Ownership a role, nearly added a workspace edge — all caught by "one responsibility".
- existing-model-confronter (post-validation only): run-01 twice let `access.memberships`
  / `tenant_id` pull the target model; corrected each time.
- R3 (future ≠ present): ownership history, workspace-as-perimeter "for future
  capabilities" — both correctly deferred; workspace-as-perimeter was ultimately
  justified by a STRUCTURAL 1:N domain fact, NOT by future capabilities.
- R6 (read before amend): ADR-ARCH-010/011 twice suspected of blocking, twice cleared by
  reading them.
- decision-classifier: D4/D5 correctly kept conceptual (in the spec), not made technical
  ADRs; persistence correctly deferred to step 11.

## Possible run-01 accidents (NOT yet invariants)

Honesty clause: after ONE run, some of the above may be specific to BP-001, not general.
Flagged so they are TESTED against run-02/03, not assumed:

- The sub-agent SET and GRAIN may be wrong. Maybe domain-boundary-reviewer and
  invariant-auditor are one agent; maybe existing-model-confronter is not needed as a
  separate agent at all. Do NOT treat five sub-agents as settled.
- BP-001 was authorization-heavy and identity-heavy. A feature that is data-heavy or
  workflow-heavy may need different reviewers, or reveal these ones as over-fitted.
- R2/R3 (existing-dictates / future-imposes) fired a LOT in run-01 because BP-001 sat on
  top of a rich existing model. A greenfield feature may barely trigger them — which
  would mean they are situational guards, not always-on rules.
- The A/B decision split was clean here because Category B was small (persistence only).
  A feature with heavy technical decisions may blur the line.

## What run-02/03 must confirm before building

- Are these sub-agents real (recurring need) or run-01-specific?
- Does the A/B classifier hold on a feature with heavier Category B?
- Does "existing-model-confronter enters only at step 10" survive a feature that touches
  more existing schema?
- Build the agent Process-First itself (actor, intent, boundary) when confirmed.
- Sub-agent GRAIN: how many, which merged? (e.g. is domain-boundary-reviewer +
  invariant-auditor one agent?) Decide from evidence across runs, never from one.

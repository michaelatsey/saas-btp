# ADR-ARCH-002 — CorrectiveActions as a separate bounded context

Status: accepted
Date: 2026-06-28

## Context

Findings arise in multiple domain contexts: Safety (constats) and Quality
(non-conformities), and later others. Each finding can trigger a corrective action
with its own lifecycle (assignment, deadline, evidence, verification, closure).
Question: does the corrective-action lifecycle live inside each source context
(duplicated in Safety and Quality), or as its own context?

Projets.docx frames corrective actions as the strategic, transversal asset of the
product — the real differentiator over "just another safety logger".

## Decision

CorrectiveActions is its own bounded context. It owns the corrective-action entity
and its state machine (open -> in progress -> verification -> closed). It does not
depend on Safety or Quality at compile time; it reacts to their integration events
(e.g. SafetyConstatRaised) via the outbox/inbox pattern (MicroKit.Messaging).

## Consequences

- Easier: one corrective-action lifecycle, reused by Safety, Quality and future
  sources, with no duplication; the differentiator is modelled once, cleanly.
- Inversion: source contexts raise events; CorrectiveActions subscribes. No source
  context references CorrectiveActions directly, and vice versa — zero circular deps.
- Increment 2 (Quality) reuses the exact same CorrectiveActions contract; only a new
  event source is added.
- Harder: requires the messaging plumbing (integration events) from increment 1, not
  just direct calls — accepted, as it is the same MicroKit.Messaging already in the stack.

## Alternatives considered

- Corrective actions embedded in Safety (and later duplicated in Quality): rejected —
  duplicates the lifecycle, dilutes the differentiator, and couples the model to each
  source context.
- A generic "workflow engine" for all lifecycles: rejected — premature platform
  thinking (CLAUDE-SAAS-BTP.md 4); CorrectiveActions is one opinionated lifecycle, not
  a configurable engine.

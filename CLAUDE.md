# SaaS BTP — Project Root Brain

## Vision

A vertical B2B SaaS for construction operations: field execution, QHSE audits,
non-conformities, corrective actions and reporting. The field user works on a site with
poor or no network, on a phone, often with gloves on — offline-first is a property of
the domain, not a feature.

---

## Working rules

- **State lives in GitHub Issues.** Never in a repository file, never in memory. What is
  done, in progress or next is read from the issues and their proof comments.
- **Cite what you read.** Before asserting a path, a build or test impact, or a runtime
  behaviour, name the file you established it from. An unread claim is a defect.
- **Never route around a guard.** If a test or an architecture rule blocks you, do not
  rename, exclude or relax it to get green: either fix the code, or delete the guard
  explicitly and record why in an ADR.
- **Check the container before registering a service.** Search for an existing
  registration first; never add a second one that wins by ordering.
- **Read the provider's current documentation before wiring an external
  service.** Keys, headers and endpoints change without notice, and both the
  repository and any model's knowledge of them are dated. Never configure a
  provider from memory.
- **Workflow.** Issue -> branch -> pull request -> squash-merge into `main` with one
  closing keyword per issue (ADR-ORG-002, ADR-ORG-003). Titles, labels, body structure,
  DoR and DoD: `.claude-context/context/product/issues-convention.md`.

---

## Navigation — Where to find context

| Need | Load |
|------|------|
| Product rules, what NOT to build | `.claude-context/context/product/product-vision.md` |
| Phase 1 scope and increments | `.claude-context/context/product/scope-phase-1.md` |
| A business process (spec, design package, implementation design) | `docs/business-processes/BP-XXX/` |
| Domain / feature specs (non-BP) | `.claude-context/context/product/specifications/` |
| How to design any new feature | `.claude-context/context/architecture/conventions/process-first-method.md` |
| Issues convention, and where durable artifacts live | `.claude-context/context/product/issues-convention.md` |
| Modular monolith decomposition | `.claude-context/context/architecture/architecture.md` |
| Naming conventions (all layers) | `.claude-context/context/architecture/conventions/naming.md` |
| Analysis vocabulary (BP, ACT, TSK...) | `.claude-context/context/architecture/conventions/glossary.md` |
| Any decision, and why | `.claude-context/context/decisions/decisions-index.md` |

---

## Documentary model

A Business Process is the autonomous documentary unit: anyone, human or AI, must be able
to pick up a BP under `docs/business-processes/BP-XXX/` and derive an implementation
without the conversation history. Specification (WHY) -> Design Package (WHAT, business
model, closed) -> Implementation Design (software HOW, stable contract) -> Implementation
Plan, which is derived per tool and never consigned.

`issues-convention.md` section 12 is authoritative for this layout and for the derivation
rules. This file carries only the orientation above.

---

## Conventions

- Branches: `main` is protected and is the only long-lived branch; work happens on
  `<type>/<scope>/<description>`, merged by pull request.
- Commits follow Conventional Commits 1.0.0: `<type>(<scope>): <description>`, the
  description in the imperative, no trailing period. `feat` and `fix` carry their
  specification meaning; `build`, `chore`, `ci`, `docs`, `perf`, `refactor`, `style`,
  `test` and `revert` are also used. The scope names the area of the codebase the commit
  touches (`api`, `issues`, `security`, `repo`), independently of the branch scope. A
  breaking change is marked with `!` before the colon, a `BREAKING CHANGE:` footer, or
  both.
- ADRs: `ADR-PROD-*` / `ADR-ARCH-*` / `ADR-ORG-*` (see decisions-index.md). Conceptual
  ADRs are written during design validation; technical ADRs once infrastructure
  constraints are known. An accepted ADR is superseded, never edited.
- Feature design: every new feature starts with the Process-First Design Method
  (`.claude-context/context/architecture/conventions/process-first-method.md`).
- Language: all repository files in English. UI labels localized, French first.
- Versioned files: plain text, no decorative emojis.
- Secrets are never written to a settings file, a repository file, or a conversation.

---

## Stack

A .NET 10 modular monolith (Hexagonal, DDD, CQRS) on the MicroKit ecosystem, with an
offline-first client and Supabase for PostgreSQL, Auth and Storage. The choices behind
it, and the ones still open, live in `decisions-index.md` — this file does not restate
them.

The stack is an implementation constraint, not a source of domain decisions. Never let a
stack name drive a modelling choice.

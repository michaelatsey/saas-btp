# SaaS BTP — Project Root Brain

## Vision

A vertical B2B SaaS for construction operations (field execution, QHSE audits,
non-conformities, corrective actions, reporting), offline-first, built and operated
by a single founder augmented by an AI organization.

This repository serves TWO pillars that must never be confused:

| Pillar | What it is | Where |
|--------|-----------|-------|
| **Product** | The construction SaaS itself | `.claude-context/context/product/` + `architecture/` |
| **AI OS** | The augmented-company operating system used to build it | `.claude-context/context/ai-os/` |

> The Product is the deliverable. The AI OS is both the means AND a career asset:
> the documented method of building software as a one-person AI-augmented company,
> to be sold later as expertise to companies that want to augment themselves.

---

## Navigation — Where to find context

| Need | Load |
|------|------|
| Product rules, what NOT to build | `.claude-context/context/product/product-vision.md` (refs `CLAUDE.md`) |
| Current Phase 1 scope + increments | `.claude-context/context/product/scope-phase-1.md` |
| Detailed feature specs (fields, rules) | `.claude-context/context/product/specifications/` |
| How to design any new feature | `.claude-context/context/architecture/conventions/process-first-method.md` |
| User stories / backlog | GitHub Issues (NOT a repo file) — see `.claude-context/context/product/issues-convention.md` |
| Build progress (what's done / next) | `.claude-context/context/build-checklist.md` |
| AI OS target & principles (north star) | `context/ai-os/ai-os-vision.md` |
| Where we are on the AI OS journey | `.claude-context/context/ai-os/ai-os-roadmap.md` |
| The documented method (career asset) | `.claude-context/context/ai-os/ai-os-method.md` |
| Modular monolith decomposition | `.claude-context/context/architecture/architecture.md` |
| Naming conventions (all layers) | `.claude-context/context/architecture/conventions/naming.md` |
| Any decision (why) | `.claude-context/context/decisions/decisions-index.md` |
| What happened last | highest-seq file in `.claude-context/sessions/` |
| Agent definitions | `.claude/agents/` |

Always read the most recent `.claude-context/sessions/` file before starting work.

---

## Conventions (inherited from the freelance ecosystem)

- Branches: `main` protected | `dev` integration | `feature/scope/desc` | `fix/scope/desc`
- Conventional Commits: `feat(audit):` `fix(sync):` `docs(ai-os):` `chore(ci):`
- ADRs: prefixed `ADR-PROD-*` / `ADR-ARCH-*` / `ADR-ORG-*` (see decisions-index.md).
  ADR discovery follows the Process-First method: conceptual ADRs are written during
  design validation; technical ADRs after infrastructure constraints are known.
- Feature design: every new feature starts with the Process-First Design Method
  (`.claude-context/context/architecture/conventions/process-first-method.md`). Business-process
  validation precedes any technical design.
- User stories / backlog: GitHub Issues (milestones = increments, labels = context/type/prio).
  Never tracked in repo markdown. Repo holds durable specs and decisions only.
- Language: all repo files in English. Sessions in English. UI labels localized (French first).
- GitHub-versioned files: plain text, no decorative emojis (status icons allowed).
- Sessions: committed, plain text (Option B — traceability is part of the asset).
  Private drafts go in `.claude-context/sessions/local/` (gitignored, emojis allowed).
- Sessions filename: `<seq>-<date>-<subject>.md` (seq = 3-digit, zero-padded).
  Latest session = highest seq. Read it first before any work.
- GitHub operations: always `gh` CLI from WSL2, never the web UI unless necessary.
- Coolify: this project = its own Coolify project (`saas-btp`), production + staging envs.
- Credentials: saved in Bitwarden immediately on account creation.

---

## Stack (validated)

Backend: .NET 10 modular monolith — Hexagonal · DDD · CQRS · MicroKit ecosystem
Web: Next.js 16+ · TypeScript · Shadcn/ui · TailwindCSS · offline-first via PowerSync Web (ADR-ARCH-003)
Data: Supabase (PostgreSQL + Auth + Storage)
Mobile (Phase 2): React Native (Expo) · offline-first via PowerSync
Contract boundary: OpenAPI (ADR-ARCH-001)
Infra: Hetzner VPS · Coolify · Cloudflare

The stack is an implementation constraint, not a source of domain decisions.
Never let a stack name drive a modelling choice.

---

## Current state

- Phase 1 scope and modular-monolith bounded contexts: DEFINED (Step 1 done).
- Increment 1: Safety constat -> Corrective action -> dashboard (ADR-PROD-001),
  shipped as an offline PWA (ADR-ARCH-003, spike-gated).
- AI OS palier: Palier 1 (hub + core manual agents) — in progress.
- Next step: GitHub Issues foundation (convention + labels + milestone + board),
  then decompose increment 1 into stories. See `.claude-context/context/build-checklist.md`.

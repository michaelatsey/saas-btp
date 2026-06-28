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
| Product rules, scope, what NOT to build | `context/product/product-vision.md` (refs `CLAUDE-SAAS-BTP.md`) |
| Current Phase 1 scope | `context/product/scope-phase-1.md` |
| Backlog / user stories | `context/product/backlog.md` |
| AI OS target & principles (north star) | `context/ai-os/ai-os-vision.md` |
| Where we are on the AI OS journey | `context/ai-os/ai-os-roadmap.md` |
| The documented method (career asset) | `context/ai-os/ai-os-method.md` |
| Modular monolith decomposition | `context/architecture/architecture.md` |
| Any decision (why) | `context/decisions/decisions-index.md` |
| What happened last | most recent file in `sessions/` |
| Agent definitions | `.claude/agents/` |

Always read the most recent `sessions/` file before starting work.

---

## Conventions (inherited from the freelance ecosystem)

- Branches: `main` protected | `dev` integration | `feature/scope/desc` | `fix/scope/desc`
- Conventional Commits: `feat(audit):` `fix(sync):` `docs(ai-os):` `chore(ci):`
- ADRs: prefixed `ADR-PROD-*` / `ADR-ARCH-*` / `ADR-ORG-*` (see decisions-index.md)
- GitHub-versioned files: plain text, no decorative emojis (status icons allowed)
- Sessions and local notes: emojis allowed, never committed if marked local
- GitHub operations: always `gh` CLI from WSL2, never the web UI unless necessary
- Coolify: this project = its own Coolify project (`saas-btp`), production + staging envs
- Credentials: saved in Bitwarden immediately on account creation

---

## Stack (validated)

Backend: .NET 10 modular monolith — Hexagonal · DDD · CQRS · MicroKit ecosystem
Web: Next.js 16+ · TypeScript · Shadcn/ui · TailwindCSS
Data: Supabase (PostgreSQL + Auth + Storage)
Mobile (Phase 2): React Native (Expo) · offline-first via PowerSync
Infra: Hetzner VPS · Coolify · Cloudflare

---

## Current state

- Phase: project bootstrap — structure stood up, scope Phase 1 not yet defined
- AI OS palier: **Palier 1** (hub + core manual agents) — in progress
- Next step: define Phase 1 vertical slice + modular monolith bounded contexts

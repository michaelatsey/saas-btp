# Product Vision

> The authoritative product rules live in `CLAUDE-SAAS-BTP.md`
> (product philosophy, phases, UX/offline/RBAC strategy, decision filter).
> This file is the entry point; it does not duplicate that content.

## One-liner

Vertical B2B SaaS for construction operations: field execution, QHSE audits,
non-conformities, corrective actions, daily/weekly reporting, stock, offline-first.

## Source of truth for features

- `Innov_Boost.docx` — original spec (basic, incomplete features).
- `Projets.docx` — deepened analysis; THIS is the reference for product features.

## Non-negotiable product stance

- Product first, platform later. No generic workflow/form/rule engines until real
  usage patterns emerge (see CLAUDE-SAAS-BTP.md sections 4, 12, 13).
- Offline-first is a strategic differentiator, not an option.
- Opinionated, simple, field-usable. Never ERP/BPM/config-software feel.

## Where things live

- Phase 1 scope and increment sequence: `scope-phase-1.md`
- Detailed feature specs (fields, rules): `specifications/`
- User stories / backlog: GitHub Issues (NOT a repo file) — see issues-convention.md
- Architecture: `../architecture/architecture.md`
- Decisions (why): `../decisions/decisions-index.md`

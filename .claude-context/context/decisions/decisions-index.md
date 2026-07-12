# Decisions Index (ADRs)

> Every non-trivial decision is recorded as an ADR. An ADR is short, immutable once
> accepted, and superseded (never edited) when reversed.

## Domains

| Prefix | Domain | Folder |
|--------|--------|--------|
| `ADR-PROD` | Product / business scope | `decisions/product/` |
| `ADR-ARCH` | Technical architecture | `decisions/architecture/` |
| `ADR-ORG`  | AI OS / organization | `decisions/ai-os/` |

## Numbering

`ADR-<PREFIX>-NNN-short-kebab-title.md` — NNN is a zero-padded sequence per domain
(e.g. `ADR-ORG-001-progressive-not-bigbang.md`).

## Template

```
# ADR-XXX-NNN — Title

Status: proposed | accepted | superseded by ADR-...
Date: YYYY-MM-DD

## Context
What forces this decision. The problem, the constraints.

## Decision
What we chose, stated plainly.

## Consequences
What becomes easier, what becomes harder, what is now forbidden.

## Alternatives considered
Options rejected and why.
```

## Index

| ADR | Title | Status |
|-----|-------|--------|
| ADR-PROD-001 | Market entry through Safety (not Quality) | accepted |
| ADR-ARCH-001 | TypeScript clients generated from OpenAPI, not from C# | accepted |
| ADR-ARCH-002 | CorrectiveActions as a separate bounded context | accepted |
| ADR-ARCH-003 | Increment 1 client = offline PWA (spike-gated), Expo in Phase 2 | accepted |
| ADR-ARCH-004 | Ownership of Membership (deferred; Access owns none in #4) | accepted |
| ADR-ARCH-005 | Write path: PowerSync uploadData() to the .NET API (amended 2026-07-12: auth-boundary identity-provisioning exception) | accepted |
| ADR-ARCH-006 | DbUp owns the database schema (DDL); EF Core is runtime ORM only | accepted |

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

### Removed architecture ADRs

ADR-ARCH-001 to ADR-ARCH-016 were removed with the API reset; their text stays reachable
under the tag `archive/pre-reset`. New architecture ADRs continue at ADR-ARCH-017: a number
is never reused, so a citation of a removed ADR can never resolve to a new one.

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
| ADR-PROD-002 | Founding creation (BP-001) does not verify the legal link between the requester and the declared organization; organization is taken as declared | accepted |
| ADR-ARCH-017 | Solution layout for the .NET side of the monorepo | superseded by ADR-ARCH-020 |
| ADR-ARCH-018 | HTTP API surface: Minimal APIs, major version in the URL, committed OpenAPI | accepted |
| ADR-ARCH-019 | Schema migrations with DbUp, run before the application | accepted |
| ADR-ARCH-020 | Solution layout for the .NET side of the monorepo: hosts, modules and folder casing | accepted |
| ADR-ARCH-021 | Test project conventions: xUnit v3, native assertions, XML documentation | accepted |
| ADR-ORG-001 | The repository stays public: Claude.ai needs read access; secret scanning and push protection become a priority; README and licence statement to correct | accepted |
| ADR-ORG-002 | GitHub flow: short-lived branches → PR → `main` (default), squash, branch deleted after merge; `dev` retired after a final promotion | accepted |
| ADR-ORG-003 | Claude Code may commit, push and open PRs, each gated by an `ask` permission rule; merge stays human; GitHub through `gh` only; supersedes "Claude never runs git" (provisional, revisit after pilot #75) | accepted |



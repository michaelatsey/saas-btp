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
| ADR-ARCH-004 | Ownership of Membership (deferred; Access owns none in #4) | superseded by ADR-ARCH-008 |
| ADR-ARCH-005 | Write path: PowerSync uploadData() to the .NET API (amended 2026-07-12: auth-boundary identity-provisioning exception) | accepted |
| ADR-ARCH-006 | DbUp owns the database schema (DDL); EF Core is runtime ORM only | accepted |
| ADR-ARCH-007 | GoTrue writes app_metadata by an UPDATE posterior to the auth.users INSERT (trigger design rule) | accepted |
| ADR-ARCH-008 | Identity model: profiles = human projection; tenants + memberships = authorization bridge; JWT identity-only (amended by ADR-ARCH-009: two independent authorization edges) | accepted |
| ADR-ARCH-009 | Authorization model: claims-based authentication; relationship-based authorization over two independent edges (organization + site); role→scope matrix in C# (amended by ADR-ARCH-010: the site is the grain) | accepted |
| ADR-ARCH-010 | The site is the authorization grain; lot, zone, phase and trade are business dimensions, never authorization perimeters | accepted |
| ADR-ARCH-011 | Onboarding by invitation: the token is the authorization to exist; createUser (Admin API) is the single account-creation path; public signup permanently disabled (amended 2026-07-14: operator tenant provisioning, no self-service; GoTrue open items closed by the #48 spike — orphan adoption on 422, exact-match Admin API lookup; the normalization invariant must be tested) (amended by ADR-ARCH-012: BP-001 is the founding self-service entry path; operator provisioning is no longer the founding path) | accepted |
| ADR-ARCH-012 | Person, account, organizations and initial ownership model; Ownership and Membership are distinct relations; Initial Owner set at founding creation (revealed by BP-001; notes a divergence with ADR-ARCH-008 on the founding entry path, not resolved here) | accepted |
| ADR-ARCH-013 | Authorization scope levels (Organization / Workspace / Site); a scope level is not an access edge; BP-001 produces the first access edge at the Workspace level (revealed by BP-001; amends ADR-ARCH-008/009 on the membership↔authorization conflation — concrete patch deferred; confirms ADR-ARCH-010) | accepted |
| ADR-ARCH-014 | Organization and Workspace are two aggregate roots; a transaction boundary is not an aggregate boundary (revealed by BP-001; answers the question left open by its Implementation Design §3.1 — does not touch the business model, the DDL, or the three autonomous relations) | accepted |
| ADR-ARCH-015 | Access persistence: EF Core as runtime ORM over the DbUp-owned access schema (ADR-ARCH-006 discipline); the founding transaction realized as ONE EF change-set persisted by ONE SaveChanges behind a single application port — createUser stays outside; a pipeline TransactionBehavior for the founding command is explicitly rejected (closes BP-001 ID §6.2.1/§6.2.2) | accepted |
| ADR-ARCH-016 | The invariant-bearing aggregate absorbs its relations: Ownership and Organization Membership inside Organization, Workspace Access inside Workspace; the three axes stay non-derivable SEMANTICALLY (revealed by BP-001; departs from its Implementation Design §3.1, which answered a semantic risk with a structural constraint; confirms ADR-ARCH-013 and ADR-ARCH-014; does not touch the business model or the DDL) | accepted |
| ADR-PROD-002 | Founding creation (BP-001) does not verify the legal link between the requester and the declared organization; organization is taken as declared | accepted |



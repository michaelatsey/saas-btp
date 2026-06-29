# Naming Conventions (project-wide standard)

Status: accepted (2026-06-28). Reusable across all projects.
Principle: each layer keeps its idiomatic convention; boundaries translate
automatically. A field is named ONCE (in C#) and flows everywhere.

---

## Per-layer conventions

| Layer | Convention | Example |
|-------|-----------|---------|
| C# code / domain model | PascalCase | `ObserverName`, `Severity` |
| JSON / API wire (contract boundary, ADR-ARCH-001) | camelCase | `observerName` |
| TypeScript (generated from OpenAPI) | camelCase | `observerName` |
| PostgreSQL columns / tables | snake_case | `observer_name`, `safety_constat` |
| Enum values on the wire | camelCase string tokens | `dangerousSituation` |
| UI labels | localized (FR first), i18n layer | "Nom de l'observateur" |

---

## How translation happens (named once)

- C# model is the single authored source (PascalCase).
- System.Text.Json serializes to camelCase on the wire (ASP.NET Core default).
- EF Core maps to snake_case columns via a naming convention (snake_case provider).
- OpenAPI document is produced from the C# model -> TypeScript generated in camelCase.
- UI labels live in i18n resources, never as field identifiers.

Never hand-rename a field per layer. If a name appears twice by hand, it is a smell.

---

## Field naming rules

- Identifiers: `id`; foreign references: `<entity>Id` (e.g. `siteId`, `assigneeId`).
- Booleans: `is` / `has` prefix (e.g. `isClosed`, `hasPhotos`).
- Timestamps: ISO 8601, stored UTC; names like `createdAt`, `updatedAt`,
  `observedAt`, `dueAt`, `closedAt`.
- Audit fields on every persisted entity: `createdAt`, `updatedAt`, `createdBy`,
  `deletedAt` (soft delete — CLAUDE-SAAS-BTP.md 22).
- Collections: plural (`photos`, `comments`).

---

## Enum value style

- camelCase string tokens on the wire (not integers, not SCREAMING_CASE).
- Stable: never reuse or repurpose a token; add new ones, deprecate old ones.

---

## Specifications

Spec files (`specifications/**`) document fields in camelCase (the contract
convention) with a French UI-label column. They do NOT use C# PascalCase or DB
snake_case — those are derived, not authored in specs.

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
  `deletedAt` (soft delete — CLAUDE-SAAS-BTP.md 22). This applies to mutable
  entities; per the `sql.md` §Audit exception, insert-only aggregates (e.g. the
  Safety `Constat`) carry `created_at` only — `updated_at`/`deletedAt`/`created_by`
  are added when the aggregate actually gains mutation/soft-delete behaviour.
- Collections: plural (`photos`, `comments`).

---

## Enum value style

- camelCase string tokens on the wire (not integers, not SCREAMING_CASE).
- Stable: never reuse or repurpose a token; add new ones, deprecate old ones.
- Exception — persisted closed-set tokens: when a closed-set Value Object's token IS the stored
  value and the DB CHECK constraint (e.g. the Safety Constat `type` / `severity`:
  `dangerous_situation`), that snake_case token stays identical end-to-end, wire included. The
  property-naming (camelCase) conventions above govern property names and true enum values; they do
  NOT apply to these persisted tokens — a second, camelCase wire form would need a mapping table for
  no benefit.

---

## Specifications

Spec files (`specifications/**`) document fields in camelCase (the contract
convention) with a French UI-label column. They do NOT use C# PascalCase or DB
snake_case — those are derived, not authored in specs.

---

## Aggregate identifiers

- Format: UUIDv7 (RFC 9562).
- Storage: PostgreSQL `uuid`.
- Domain representation: a strongly-typed Value Object
  (`readonly record struct XId(Guid Value)`). A raw `Guid`/`string` is never used
  as an identifier in Domain.
- Generation: at aggregate creation time, by the creator of the aggregate.
- Offline-created aggregates (field capture): generated client-side. The id doubles as
  the idempotency key — a replayed create carries the same id, the server returns the
  existing resource, never 409.
- Rationale: time-ordered → B-tree locality on append-heavy tables + free chronological
  sort; native in .NET 10 (`Guid.CreateVersion7`) and Postgres; generatable in JS for the
  offline client.
- To validate at persistence wiring: .NET v7 has no intra-ms monotonic counter and an
  endianness quirk — verify the Npgsql `Guid`→`uuid` mapping preserves order.

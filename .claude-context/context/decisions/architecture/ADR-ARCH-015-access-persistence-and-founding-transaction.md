# ADR-ARCH-015 — Access persistence: EF Core as runtime ORM over the DbUp schema; the founding transaction is one change-set behind one port

Status: accepted
Date: 2026-07-27
Revealed by: BP-001 (Créer son espace entreprise) — Implementation Design §6.2, which names both
decisions as ADR candidates and forbids generalization before an ADR is accepted.
Refs: ADR-ARCH-005 (write path; the .NET domain is the write authority), ADR-ARCH-006 (DbUp owns the
DDL; EF Core is runtime ORM only), ADR-ARCH-014 (two aggregate roots; transaction boundary ≠
aggregate boundary); BP-001 Design Package §4; BP-001 Implementation Design §1–§3.

## Scope of this decision

Two questions the BP-001 Implementation Design left to ADRs, and nothing else:

1. the persistence strategy of the Access bounded context (§6.2.1);
2. the technical realization of the founding transaction boundary (§6.2.2).

The analysis was already done — ADR-ARCH-006 fixed the project-wide split (DbUp owns DDL, EF Core is
runtime-only), and the Safety context exercises it in production code. This ADR formalizes that the
same strategy binds Access, and settles the one point ADR-ARCH-006 could not: HOW the founding
atomicity is realized. It changes no business model, no DDL, no API.

## Decision

### 1. Persistence strategy

The Access bounded context persists through **EF Core via MicroKit.Persistence, as a runtime ORM
over the DbUp-owned `access` schema** — exactly the ADR-ARCH-006 discipline Safety already applies:

- DbUp is the single DDL authority; the `AccessDbContext` maps an existing schema and never creates
  one. No migrations, no `EnsureCreated()`.
- Strongly-typed ids and closed-set value objects map through EXPLICIT converters
  (`OrganizationStatus.FromToken`, id `Value` conversions) — never an implicit `ToString`.
- Values are written as supplied by the domain (ids and UTC timestamps pre-generated); the database
  produces nothing.
- The EF model declares the same referential dependencies the schema carries (workspace →
  organization; relations → their endpoints), so EF's insert ordering is topologically correct and
  mirrors the Design Package's conceptual dependency order without hand-sequencing writes.

### 2. Founding transaction realization

The atomic unit "identity projection + five business facts" is realized as **one EF change-set,
persisted by one `SaveChangesAsync`, behind a single application port** (`IFoundingRepository
.PersistAsync(FoundingUnit)`):

- EF Core executes a multi-entity `SaveChanges` inside a single database transaction. One call
  therefore IS the founding transaction — commit and rollback are structural, not orchestrated.
- The single port method makes committing a subset **impossible at the seam**: no caller can persist
  three of the five facts, because no narrower operation exists.
- `createUser` (the identity boundary) is invoked before the port and outside it, so no database
  transaction is ever open across the GoTrue HTTP call.
- The handler remains the transaction owner in the Implementation Design's sense: it decides when
  the unit is complete and submits it whole. What it delegates is the mechanics, not the boundary.

### Explicitly rejected: a pipeline TransactionBehavior for the founding command

A whole-handler transaction behavior would open the database transaction BEFORE the handler runs and
therefore wrap the GoTrue `createUser` HTTP call inside it — precisely what the Design Package
forbids (createUser is outside the business transaction), and a long-lock anti-pattern besides. If a
TransactionBehavior later joins the MicroKit pipeline for other commands, the founding command must
stay out of its scope.

## Consequences

- Access persistence is now ADR-bound, as ID §3.6 requires: any faithful implementation writes
  through EF Core into the DbUp schema, and the founding writes go through the single port.
- The anti-drift guard (ADR-ARCH-006) applies: the `AccessDbContext` mapping is verified against the
  DbUp-migrated schema by an integration test (Testcontainers), never against an EF-generated schema.
- Uniqueness and FK invariants keep persistence as their single guarantor (ID §2); the EF model's
  declared relationships are ordering metadata, not a second guard.
- A repeated founding submission for the same identity fails loudly at the profile's primary key —
  the whole second change-set rolls back. This is a CONSEQUENCE of atomicity, not a resolution of
  RULE-4 (the duplicate-intent rule), whose guarantor remains an open gap.
- Harder: the founding port is deliberately narrow; future read use-cases will add their own ports
  rather than widening this one.

## Alternatives considered

- **Raw Npgsql commands** (as Site's scope provider uses for its read). Rejected for the write path:
  it would duplicate the mapping the EF configurations already express, diverge from the Safety
  precedent, and hand-roll transaction management EF provides structurally.
- **Six writer ports + a unit-of-work port.** Rejected: a chatty seam that lets a caller commit a
  subset — the exact interpretation the Implementation Design § 3.2 forbids. The Design Package's
  "writers, one per element" are realized as the six entity additions inside the single change-set.
- **Pipeline TransactionBehavior.** Rejected above, on the createUser-outside-the-transaction
  constraint.
- **An explicit `BeginTransaction`/`Commit` block in the handler.** Functionally equivalent for a
  single SaveChanges; rejected as ceremony that re-introduces the possibility of multiple saves
  inside one handler-managed transaction — the door the single-port design closes.

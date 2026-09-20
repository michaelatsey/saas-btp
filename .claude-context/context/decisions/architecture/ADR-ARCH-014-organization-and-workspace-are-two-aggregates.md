# ADR-ARCH-014 — Organization and Workspace are two aggregate roots; a transaction boundary is not an aggregate boundary

Status: accepted
Date: 2026-07-27
Revealed by: BP-001 (Créer son espace entreprise) — the founding process, at the point where its
Domain is materialised.
Refs: BP-001 Implementation Design §3.1 (which leaves this question open), §2 (invariant → single
guarantor), §3.2 (the single transaction boundary); BP-001 Design Package §2, §4; ADR-ARCH-012
(Ownership / Membership vocabulary); ADR-ARCH-013 (Workspace as an authorization scope level).

## Scope of this decision

This ADR decides the **domain-side aggregate boundary** between Organization and Workspace, and
nothing else. It does not change the business model, the persisted model, the DDL, or the
transaction boundary — all of which are already closed upstream. It answers the one question the
Implementation Design deliberately left open, quoted verbatim:

> Left free: how each element is technically mapped (class, record, tuple), and **whether
> Organization+Workspace form a single domain-side transactional aggregate**.

## Context

BP-001 produces, atomically, one identity projection plus five business facts. Two of those facts —
Organization and Workspace — are persisted as autonomous units bound by an Organization 1:N Workspace
containment. The Implementation Design fixes their persistence, their integrity constraints, and the
single transaction that must cover them; it deliberately does not fix whether the Domain models them
as one aggregate (Organization as root, Workspace inside it) or as two aggregate roots.

The question is load-bearing because it is the first thing the Domain materialisation would otherwise
settle by accident: the first constructor written presumes one reading or the other, and an aggregate
boundary is expensive to move later.

Two facts constrain the answer, and both come from documents already closed:

1. **Atomicity is not at stake.** The Implementation Design §2 assigns all-or-nothing over the
   projection + five facts to the **transaction boundary**, and the process rules to
   application/domain orchestration. No invariant it lists requires a single aggregate root to be
   satisfied. A one-aggregate model would therefore buy no atomicity that the transaction does not
   already guarantee.
2. **The Organization 1:N Workspace relation is unbounded and long-lived.** The Design Package
   records it as a domain fact of the BTP market — a company operates through several autonomous
   operational spaces (agencies, regions, activities, entities) — and states that creating an
   additional workspace is a *different* business process from BP-001.

## Decision

**Organization and Workspace are TWO aggregate roots**, each with its own identity, its own lifecycle,
and its own repository port. Neither is an entity inside the other.

They are separate domain concepts:

- **Organization** — the company identity and the ownership boundary.
- **Workspace** — an operational space in which activities are run.

They are related, but the relation is not transactional ownership. A Workspace references its
Organization by identity; the Organization does not contain the Workspace as internal state.

**A transaction boundary is not an aggregate boundary.** The founding process coordinates the
creation of both inside one application use-case transaction — as the Implementation Design requires
— and that coordination does **not** make them one aggregate. The two concepts answer to different
consistency needs; the transaction answers to the process's all-or-nothing terminal state. Conflating
the two would let a process's atomicity requirement dictate the shape of the domain model, which is
the wrong direction of derivation.

### What this decision does not touch

- **The Organization → Workspace link stays containment expressed by reference** (a Workspace carries
  its Organization's identity), exactly as the Design Package §4 cardinalities and the existing
  founding DDL already fix. This ADR does **not** promote that link into a fourth autonomous relation;
  doing so would be a change to a closed business model, not an implementation decision.
- **The three autonomous relations remain autonomous persisted relations** — Ownership, Organization
  Membership, and Workspace Access — never internal components of Organization or Workspace
  (Implementation Design §3.1). This decision reinforces that separation; it does not revisit it.
- The single transaction boundary, the referential dependency order, the uniqueness constraints, and
  the DDL are unchanged. Both readings of the aggregate question mapped onto the same schema.

## Consequences

- The Domain gains two aggregate folders, each self-contained, consistent with the module's
  by-aggregate Domain organisation. Each root owns its own invariants; neither reaches into the
  other's state.
- **A Workspace can evolve independently of its Organization.** Future workflows may create, archive,
  or otherwise manage workspaces without loading or mutating Organization state — which the
  one-aggregate reading would have forced.
- The aggregate boundary stays small. An Organization holding many workspaces never becomes an
  oversized aggregate, and the unbounded 1:N never turns into an unbounded object graph behind a
  single root.
- Workspace Access — an autonomous relation at the Workspace grain (ADR-ARCH-013) — references a
  Workspace root by identity: an ordinary inter-aggregate reference, not a pointer into another
  aggregate's interior.
- **Accepted cost, stated plainly:** no constructor can now make it impossible to build an
  Organization without a Workspace. INV-1 ("a Declared organization immediately owns its first active
  workspace") is guaranteed by the founding orchestration and the transaction, not by a type. This is
  not a weakening: it is where the Implementation Design §2 already placed that guarantee. It does
  mean the founding handler is the single place that must not be bypassed, and that a future writer
  creating an Organization outside BP-001 would need its own guarantee of the same invariant.
- Nothing in the code presumed either reading at the time of this decision: the Domain project was
  empty (assembly marker only) when it was taken.

## Alternatives considered

- **One aggregate — Organization as the root, Workspace as an internal entity.** Rejected. Its single
  real gain was making INV-1 enforceable in a constructor, and that gain is not free: it would put the
  unbounded 1:N behind one root, force every future workspace operation to load and mutate the whole
  Organization, and make Workspace Access reference an identity internal to another aggregate. It
  would also let the process's transactional requirement determine the domain shape, when the
  Implementation Design already assigns atomicity elsewhere.
- **Deciding it implicitly, in the first line of Domain code.** Rejected on method: the Implementation
  Design names this as an open question, so it is settled deliberately and recorded, not established
  by whichever constructor is written first.

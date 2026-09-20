# ADR-ARCH-016 — The invariant-bearing aggregate absorbs its relations; the three axes stay non-derivable semantically

Status: accepted
Date: 2026-08-16
Revealed by: BP-001 (Créer son espace entreprise) — reviewing the materialised Access Domain, which
had five aggregate roots where the model has two consistency boundaries.
Refs: BP-001 Implementation Design §3.1 (the clause this decision departs from), §2 (invariant →
single guarantor), §3.4 (the uniqueness invariants), §3.5 (asymmetric revocability); BP-001 Design
Package §0 (the three axes), §2 (explicit rules); ADR-ARCH-013 (axes never derived from one another);
ADR-ARCH-014 (Organization and Workspace are two aggregate roots).

## Scope of this decision

This ADR decides **which objects each aggregate boundary contains**, and nothing else. It does not
change the business model, the persisted model, the DDL, the transaction boundary, or the number of
aggregate roots — ADR-ARCH-014 already fixed that at two, and this decision confirms it.

It explicitly **departs from one clause** of the Implementation Design and records why.

## Context

The Access Domain was first materialised with five aggregate roots: `Organization`, `Workspace`,
`Ownership`, `OrganizationMembership`, `WorkspaceAccess`. Three of those roots were relations.

An aggregate is a consistency boundary that protects invariants. Measured against that definition,
the three relations failed it: none carried an invariant of its own, none had a lifecycle, none had
anything to enclose. They were roots because a scaffolding step made every persisted element a root,
not because the model asked for it.

The consequence was not merely cosmetic. Every invariant the Design Package states about these
relations spans an organization or a workspace **and** its relations:

- "exactly one initial owner" (INV-2, INV-3) — spans the organization and its ownership;
- "belonging is binary — one membership per person" (ID §3.4) — spans the organization and its
  memberships;
- "one access per person per workspace" (ID §3.4) — spans the workspace and its access edges.

With the relations outside, no aggregate could see both sides of any of these rules. Each was
therefore delegated to a database unique index, and the domain model guaranteed close to nothing —
an anemic model whose business rules were written down in the schema rather than in the code that
claims to hold them. Evans is direct on the point: an invariant that spans several objects defines
the boundary that contains them.

### The clause this departs from

Implementation Design §3.1 states:

> The three relations (Ownership, Membership, Workspace Access) are persisted as **autonomous
> persisted relations**, NOT as internal components of Organization or Workspace.

That clause protects a real business rule — the three axes (ownership, membership, authorization) are
never derived from one another (Design Package §0, ADR-ARCH-013). The risk it guards against is
**semantic**: a model in which access is an attribute of the workspace invites code that reads
membership or ownership to decide where somebody may act.

But it answers that semantic risk with a **structural** constraint, and the structure it imposes is
the one that costs the model its invariant encapsulation. The two concerns are separable, and this
decision separates them.

## Decision

**An invariant that spans an aggregate and a relation puts that relation INSIDE the aggregate.**

Applied to Access, that gives exactly two aggregates, each of which now guarantees its own rules:

- **`Organization`** contains its `Ownership` and its `OrganizationMembership` collection. It
  guarantees exactly one initial owner, and binary membership.
- **`Workspace`** contains its `WorkspaceAccess` collection. It guarantees one access per person, and
  it owns the revocability semantics of the edge.

**The independence of the three axes is preserved SEMANTICALLY, not structurally.** The rule is
unchanged and remains binding: **no code path may read one axis to infer another.** `owns → access`
and `member_of → access` stay forbidden derivations (ADR-ARCH-013). Absorbing a relation changes
where it is stored and who guards it; it never changes what it means. Concretely: the organization
holds no access edge at all, so the authorization axis is not even reachable from the boundary that
holds ownership and membership — the separation is stronger under this model than under a flat set of
sibling roots, not weaker.

**Identity is granted only where identity is meaningful.** `WorkspaceAccess` keeps a strongly-typed
identity of its own, because the business requires the edge to be revocable *without destroying
history* (ID §3.5) and a fact that must outlive its own revocation cannot be defined by its current
values. `Ownership` and `OrganizationMembership` carry none: no process revokes them, so they are
facts defined entirely by their values.

**Invariants are established by construction, not by sequence.** Each aggregate is created by a
factory named for the business act — `Organization.Found(...)`, `Workspace.Found(...)` — which
establishes the founder's ownership, membership and access inside construction. There is no
`Create()` then `AssignInitialOwner()` sequence, therefore no instant at which a valid but ownerless
organization exists, and no invariant that depends on a caller remembering an order.

### What this decision does not touch

- **The business model.** The three axes, their meanings, and their non-derivation are unchanged.
- **The number of aggregate roots.** Still two (ADR-ARCH-014); this decides their contents.
- **The DDL.** The DbUp-owned `access` schema is untouched — same tables, same uniqueness
  constraints, same referential integrity.
- **The transaction boundary.** One transaction still covers the projection plus the five business
  facts (ID §3.2). A transaction boundary is still not an aggregate boundary.
- **The identity projection.** `access.profiles` remains infrastructure. It is not, and never
  becomes, a domain concept — see the consequence on `PersonId` below.

## Consequences

- **The domain now states its own rules.** "Exactly one owner", "belonging is binary", "one access
  per person" are enforced by the aggregate that owns them. The database constraints remain, and
  their role becomes what it should always have been: defense in depth against a writer outside the
  model, not the sole place the rule is written down.
- **Two repository contracts, not five.** `IOrganizationRepository` and `IWorkspaceRepository`. A
  repository is the collection of an aggregate; the relations are reached and persisted through the
  root that contains them. The existing architecture test "one repository per aggregate root" now
  means something.
- **No `Profile` type and no `ProfileId` in the Domain.** The profile is an infrastructure projection
  of `auth.users` — GoTrue owns the identity, the projection only mirrors it — so it is not a domain
  concept. The domain names the far end of its relations `PersonId`, a strongly-typed identity it
  *receives* from the auth boundary and deliberately cannot mint (no `New()` factory). The
  projection's port and carrier type stay in the Application layer.
- **Persistence must be re-derived, and the DDL constrains how.** The relations are no longer
  independently-mapped entities; they must be mapped as collections owned by their root, over the
  same tables and the same columns. Two frictions are known and deliberately left open here:
  `access.ownerships` and `access.organization_memberships` carry surrogate `id` columns that their
  facts no longer have, and the roots' new read surface must be mapped or ignored explicitly. Neither
  is a business question; both belong to the persistence pass, under ADR-ARCH-015 and without
  touching the DDL.
- **Loading an organization loads its memberships.** That is the cost of the boundary, and it is
  bounded by the business: an organization's collective is not an unbounded growth axis the way its
  workspaces are — which is precisely why the workspaces stayed outside (ADR-ARCH-014) and the
  memberships came inside.
- **A future capability inherits the guard for free.** Nothing in BP-001 mutates an existing
  aggregate, so no public `AddMember` / `GrantAccess` / `RevokeAccess` exists yet — publishing one
  now would be speculative, and revocation in particular would decide a FORM that ID §3.5 defers. The
  single private path each aggregate uses already checks its uniqueness rule, so the process that
  later admits a member or grants an access inherits the guarantee by going through it.

## Alternatives considered

- **Keep the five roots and enforce the invariants in the application handler.** Rejected. It moves
  the rule out of the model into whichever use case remembers it, and every future writer must
  remember it again. It is the same delegation as the database constraint, one layer up.
- **Keep the five roots and let the database remain the sole guarantor.** Rejected — this is the
  status quo the correction exists to end. It is defensible only if one accepts that the domain model
  states no rules, which contradicts why the model exists.
- **Absorb the relations AND drop the axis separation as a semantic rule** (e.g. let a workspace
  answer "may this person act here" by consulting the organization's membership). Rejected
  outright: that is the exact derivation ADR-ARCH-013 forbids, and it is the risk ID §3.1 was written
  to prevent. The structural constraint is dropped; the rule it protected is not.
- **Amend Implementation Design §3.1 instead of departing from it by ADR.** Rejected on method. The
  Implementation Design is a consigned, closed contract; a decision that diverges from it is recorded
  as a decision, with its reasoning, not edited into the contract after the fact. The divergence is
  now traceable from both directions.

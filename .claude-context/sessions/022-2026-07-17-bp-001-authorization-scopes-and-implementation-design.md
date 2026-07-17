# 022 — BP-001: authorization scope model and Implementation Design

Date: 2026-07-17
Context: SaaS BTP. Resumed BP-001 (Créer son espace entreprise) at Act 2 (authorization),
continuing from session 021.

## Where we resumed

Session 021 left BP-001 blocked at ID-3.4 (Founder Access edge form): the edge form depended on an
unresolved question — is the Workspace an authorization perimeter? Act 2 had to settle the
authorization scope model before ID-3.4 could proceed.

## What was decided (durable)

### Authorization scope levels — ADR-ARCH-013 (accepted, merged in PR #69)

- Three authorization scope LEVELS recognized: Organization, Workspace, Site.
- A scope level is not an access edge. Admitting a level is structural (costs nothing, forecloses
  nothing); producing an access edge is a Process-First act (a process must produce the relation,
  which has a price: provider + sync rule + RLS + agreement test).
- BP-001 produces the first active access edge, at the Workspace grain, plus the founder's `owns` and
  `member_of` relations to the Organization (organization context, not authorization).
- Organization and Site access edges are instantiated only by the processes that require them.
- Confirms ADR-ARCH-010 (the Site grain is inherited from ADR-010, not re-modelled). Amends
  ADR-ARCH-008/009 on the membership<->authorization conflation — concrete patch DEFERRED.

### Three separate axes (Ownership / Membership / Access)

- Ownership = property; Membership = belonging (organization context); Access = authorization.
- None derives from, is computed from, or replaces another. `member_of -> access` and
  `owns -> access` are forbidden as model rules.
- BP-001 produces owns + member_of + Workspace access — three relationship facts, only one an access
  edge. Organization Membership was derived this session (it did not exist in the 021 model),
  justified by the founding process's terminal state, not by a real-world "a company has members"
  argument.

### BP-001 Design Package (business model, closed — merged in PR #69)

- Autonomous reference: business process, domain model (MCD), invariants, lifecycle,
  transaction/consistency, Non Goals. `access.profiles` is the identity projection (infrastructure),
  not a business fact; the terminal state = identity projection + five business facts (not six).

### BP-001 Implementation Design (software contract, durable — consigned this session)

- Sections: Software Architecture Model (roles, not concrete forms); Domain -> Software mapping
  (invariant -> single guarantor); Persistence Design (autonomous persisted relations vs projection
  vs unit; integrity and uniqueness constraints; generation rules); API / Interaction Contract
  (founding capability, not CRUD); Security / Authorization / Sync Boundary (BP-001 consumes only the
  Workspace grain); Technical Constraints & ADR candidates.
- No execution plan, no order, no prompt: those are derived downstream and not consigned.

### Documentary convention (established this session)

- A Business Process is the autonomous documentary unit: `docs/business-processes/BP-XXX/` holds
  specification, design package, implementation design.
- Chain: Specification -> Design Package -> Implementation Design -> Implementation Plan (DERIVED, not
  consigned) -> Code.
- `.claude-context/` no longer holds produced product artifacts (agent context, conventions,
  architecture rules, decisions, sessions only). ADRs stay at `.claude-context/context/decisions/`.
- CLAUDE.md updated (PR #69); issues-convention.md aligned (this session, section 8).

## Deferred (named, not decided)

- Concrete amendment patch to ADR-ARCH-008/009 (membership<->authorization); amend-vs-replace settled
  when the patch is written.
- Category-B ADRs: Access bounded-context persistence strategy; technical realization of the founding
  transaction boundary; fate of legacy `access.memberships` (migration / rename / replacement).
- Founder Access edge concrete form, DDL, RLS policy content, PowerSync publication — implementation.
- Cascade / roles / permissions / delegation; Organization and Site access edges — future processes.

## Process-First lessons (seed the future agent blueprint)

1. ADR history is evidence, not authority. During conceptual design, existing ADRs are read only
   after the process model is stable. An ADR may constrain implementation, never the discovery of the
   business model.
2. A concept produced by a process is not an authorization perimeter. Business concept and
   authorization scope are independent axes; a fact can be produced by a BP's terminal state yet sit
   outside the authorization path.
3. A scope level admitted by architecture is not an access edge produced by a process. Admitting a
   level costs nothing; building an edge has a price and requires a producing process.

## Consigned

- PR #69 (merged): BP-001 specification (moved into `docs/business-processes/BP-001/`), Design
  Package, ADR-ARCH-013, decisions-index, CLAUDE.md documentary model.
- This session's PR: BP-001 Implementation Design; this session trace; issues-convention.md update.

## Next

- Derive a BP-001 Implementation Plan (separate, non-consigned): issue breakdown, test strategy, then
  implementation. Not a continuation of the design; the design does not reopen.
- When BP-001 is implemented: build the Process-First agent (+ sub-agents + rules); the three lessons
  above seed its blueprint.

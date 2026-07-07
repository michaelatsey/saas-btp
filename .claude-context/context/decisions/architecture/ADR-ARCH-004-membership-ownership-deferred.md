# ADR-ARCH-004 — Ownership of Membership (deferred; Access owns none in #4)

Status: accepted
Date: 2026-07-07

## Context

Story #4 (Access) scaffolds the first bounded context and proves the
MicroKit.Auth + MicroKit.Tenancy chain in a real host via a claims-only
`GET /me`. This endpoint resolves the current user's identity, tenant, and
site/role context.

The relation "a user belongs to a site with a role" (membership) is documented
under the Site context in architecture.md, not Access. Access is authentication
+ tenancy + RBAC; Site owns "sites, periods, membership". They are two distinct
foundational contexts.

Because `GET /me` returns site/role context, scaffolding it "inside Access"
would silently annex a domain that architecture.md assigns elsewhere. The
decision of which context owns membership is a bounded-context boundary, not a
table-placement detail, and it is expensive to reverse once frozen in the
skeleton.

The question is also not binary. VISION-PRODUIT §17 mandates RBAC at
organization, project (site), module, and action scope. Membership likely
decomposes into more than one concept:

- Org-level grant ("works for client company X") — coarse-grained, already the
  responsibility of MicroKit.Tenancy.
- Site-level assignment + role ("chef de chantier on site Y, auditeur on Z") —
  fine-grained, operational, per-site; a QHSE director spans several sites.
- External member (Contractor role) — restricted-rights membership of a
  different nature.

Whether these are one concept or a two-level split (org-level grant in
Access/Tenancy + site-level assignment in Site) cannot be answered responsibly
before real member/role-management stories exist. Deciding now, from the
scaffold, risks emptying Tenancy of its meaning (if org-membership is pushed
into Site) or turning Access into a full IAM (if everything is pulled into
Access).

## Decision

For Story #4, and until the ownership question below is resolved:

- The Access module owns NO membership domain model. No `Membership` entity,
  aggregate, repository, domain service, or persistence schema is introduced in
  Access.
- `GET /me` resolves the current context from JWT claims and identity only.
  `siteId`, `role`, and `tenantId` are treated as context data (claims or
  read-only projections), never as domain aggregates.
- The business ownership of the User <-> Site <-> Role relation remains an OPEN
  architectural question (see Deferred below), to be resolved by a future ADR
  before any member/invitation/role-management feature is implemented.

Scaffold guardrail (applies to all code generation, including AI tooling):
until the ownership ADR is accepted, no generator introduces a `Membership`
entity/aggregate/repository/service/schema in Access. Context data used by
`GET /me` are claims or temporary projections and do not prejudge the final
Access/Site boundary.

## Deferred (open question — future ADR)

Is membership a single concept, or a two-level split — an org-level grant
(Access/Tenancy) plus a site-level assignment + role (Site), including the
external-member (Contractor) case? To be decided with the data from the first
real member/role-management stories, before implementing member management,
invitations, or role assignment as product data.

## Consequences

- Easier: #4 stays correctly sized (claims-only, no persistence), and the
  Access/Site boundary is not frozen prematurely by a skeleton.
- Easier: MicroKit.Tenancy keeps its role as the org-level scoping authority;
  the two-level RBAC intent from VISION-PRODUIT §17 stays reachable.
- Harder / constrained: any story needing to assign a role or manage members as
  product data (not just a JWT claim) is blocked on resolving the deferred
  question first. This is intentional.
- Forbidden until superseded: creating a `Membership` model in Access.

## Alternatives considered

- Access owns membership (IAM approach). Rejected as a scaffold default: makes
  Access responsible for users, roles, permissions, and site memberships,
  contradicting architecture.md (membership under Site) and overloading the
  authentication context.
- Site owns membership (single concept). Plausible for the site-level
  assignment, but collapsing org-level membership into Site would drain
  MicroKit.Tenancy of its purpose. Rejected as a premature single-owner
  decision; the org vs site split is exactly what must stay open.
- Let the #4 scaffold decide implicitly. Rejected: silently converts a spike
  assumption into a bounded-context decision.

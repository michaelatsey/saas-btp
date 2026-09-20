# ADR-ARCH-013 — Authorization scope levels: Organization, Workspace, Site (a scope level is not an access edge)

Status: accepted
Date: 2026-07-16
Revealed by: BP-001 (Créer son espace entreprise) — Act 2 (authorization).
Amends: ADR-ARCH-009, ADR-ARCH-008 — recorded as the OUTCOME of the confrontation in Impact, never used as its premise.
Confirms: ADR-ARCH-010 (the site is the finest grain).
Refs: ADR-ARCH-012 (Ownership / Membership vocabulary).

## Scope of this decision

This ADR decides the authorization SCOPE LEVELS of the product and nothing else. Out of scope,
deferred to their own decisions: roles and permissions, inheritance/cascade between levels,
delegation, and the concrete FORM of any access edge (table shape, provider, DDL — implementation
design). It is written from the BP-001 business model first; existing ADRs are confronted only
afterwards, in Impact. An ADR is evidence and a constraint on implementation, never the authority
that decides the business model.

## Context

BP-001 creates an organization and its first operational workspace, owned and usable by its founder.
Designing the founder's ability to act in that workspace forced a foundational question the product
had not yet answered: at which level(s) does the product authorize?

A construction company operates through several autonomous operational spaces — agencies, activities,
entities — whose teams do not necessarily see the same data (Construction / Maintenance / external
Quality Control). This is a domain fact (an organization holds 1:N workspaces), not a future
abstraction. Under it, the organization level cannot, by construction, express "acts in Abidjan, not
Dakar": it is all-or-nothing across workspaces. The place where operational action happens is the
workspace, and below it the construction site.

The model below is derived from that process and that domain. It is stated so that it can be read
and understood without reference to any prior ADR.

## Decision

The product recognizes **Organization** and **Workspace** as the authorization scope levels revealed
by current processes:

    Organization
        │
    Workspace

A SCOPE LEVEL is not an ACCESS EDGE. The two are set by different disciplines and must never be
collapsed:

- A scope level is a level at which the system is ALLOWED to authorize. Admitting a level is a
  structural decision. It creates no relation, costs nothing, and forecloses nothing.
- An access edge is a concrete relation "this person may act at this scope", produced by a business
  process. Instantiating an edge has a price (a C# provider, a PowerSync sync rule, an RLS policy,
  and a test proving the C# and SQL expressions agree) and is justified only when a process produces
  its relation.

Authorization ("where may this person act") is one of THREE distinct person↔organization axes, none
reducible to another:

- **Ownership** — "to whom does this organization belong". A property relation. Not authorization.
- **Membership** — "who belongs to this organization". A belonging relation — the organization's
  human collective. Not authorization.
- **Access** — "at which scope may this person act". The authorization axis, expressible at a
  recognized scope level.

BP-001, as the founding process, produces exactly these durable facts at its terminal state:

    Founder ── owns ──────── Organization      (ownership — context)
    Founder ── member_of ─── Organization      (membership — context)
    Founder ── access ────── Workspace         (authorization — the first active access edge)

The Organization scope level is recognized, but BP-001 produces no access edge at it.
**The Organization access edge is instantiated when a business process requires managing access at
that scope** — not as a function of any roadmap known today. The Organization level is where
organization-wide processes will authorize (member management, billing, subscription, global
settings, integrations, organization deletion, and others not yet enumerated). The edge is not built
before a process produces its relation.

Recognizing the Organization level now is deliberate and is NOT a violation of the "processes reveal
concepts" rule: it creates no concept and no relation. It reserves an address the architecture
already knows the product needs, so that organization-wide authorization, when a process produces it,
lands INSIDE the model instead of arriving as an out-of-model exception — a global permission, an
owner/admin special case, or a workaround bolted onto Membership.

### Consolidated model

    1. Business relationships (Organization context — NOT in the authorization graph)
           Person ── owns ──────── Organization
           Person ── member_of ─── Organization

    2. Authorization
       Scope levels revealed by current processes (recognized structurally):
           Organization
               │
           Workspace
       Access edges (produced by processes):
           Person ── access ── Organization   (recognized scope · edge not yet produced)
           Person ── access ── Workspace       (edge PRODUCED by BP-001)

    Produced by BP-001:  owns + member_of + Workspace access.

## Consequences

- The product's authorization address space, as revealed by current processes, comprises
  Organization and Workspace, independently of which edges are currently instantiated. A future
  organization-wide authorization need has a declared home rather than arriving as an exception.
- Ownership, Membership and Access are three separate relations. A person may own an organization
  without belonging to it (an investor), belong to it without owning it (an employee), or act in a
  workspace without belonging to the organization (an external subcontractor). None implies another,
  and none is ever silently derived from another.
- Operational authorization begins BELOW the organization. Under the 1:N organization↔workspace fact
  the organization level cannot express per-workspace action, so the first operational edge sits at
  the Workspace level.
- BP-001 produces three durable RELATIONSHIP FACTS of different natures — `owns` and `member_of`
  (organization context, NOT authorization) and Workspace `access` (the one access edge). Its
  founder is owner, member, and workspace-authorized at the terminal state: three facts, not one,
  and only ONE of them is an access edge. `owns` and `member_of` are never part of the authorization
  graph.
- A workspace edge refines externality: "external to the organization" becomes "external to the
  workspace", which can finally distinguish "internal to the company, wrong agency" that the
  organization grain could not express. (Recorded as a consequence; the edge form is implementation
  design, not decided here.)

### Infrastructure priced now — the Workspace edge only

BP-001 produces exactly one active access edge, so exactly one edge triggers infrastructure. Per the
project's per-edge cost (each authorization edge is expressed three times, and the three must be
proven to agree or the failure is silent):

- **C# provider** — a workspace-scope port ("may this user act in this workspace's scope"), reading
  the workspace-membership source directly. One port, one source read.
- **PowerSync sync rules** — the workspace-membership source enters the publication; a parameter
  query selects the workspaces a user may sync. One published table, one query — the SQL half of the
  duplication.
- **RLS (#50)** — the workspace-membership table has RLS enabled with NO policy from day one, which
  returns zero rows SILENTLY to any non-bypassing role (the #45 lesson). A policy becomes mandatory
  the day PowerSync reads it. Recorded now, decided at implementation design (ID-3.5), not here.
- **Agreement test (mandatory)** — the C# expression (provider) and the SQL expression (sync rule)
  must be proven to agree on the same dataset via Testcontainers, or they diverge silently (server
  authorizes, device syncs nothing, or the reverse). The obligation is recorded here; the test lands
  with the PowerSync slice, not with this ADR.

The Organization scope level triggers NO infrastructure now. It is in the model; it is not a table,
provider, sync rule, or policy until a process produces its edge.

## Impact — confronting the existing ADRs (analysis, not premise)

The model above is derived from the product. Read against it, the historical Access ADRs reveal that
some earlier decisions conflated ORGANIZATIONAL MEMBERSHIP with OPERATIONAL AUTHORIZATION — a
conflation only visible once a Workspace level and a separate Membership axis existed in the model.
The historical ADRs are evidence to reconcile, not the source of this decision.

- **ADR-ARCH-010 — confirmed, and its Site grain is inherited, not restated here.** The site is the
  finest authorization grain by ADR-010's prior decision; this ADR does not re-model Site nor fold it
  into BP-001's revealed model. It fixes only that the Workspace sits ABOVE the site. ADR-010 fixes no
  perimeter above the site and forbids none. No amendment. (No current process yet produces a site
  access edge — the site edge has no writer until a chantier process, per ADR-009 — so Site is a
  recognized grain awaiting its producing process, exactly as its own edge remained unproduced.)
- **ADR-ARCH-009 — amended.** It placed the operational authorization edge at the organization level
  ("two independent edges: organization, site"), because no Workspace level existed in the model at
  the time. The derived model shows the operational edge belongs at the Workspace level: what
  ADR-009 called the "organization edge" was operational authorization expressed at the wrong grain.
  The operational authorization edges are Workspace and Site; the Organization becomes a recognized
  scope whose edge is not yet produced. The concrete amendment text is produced as a separate patch
  to ADR-009. Consequence to inherit, not rediscover: ADR-009's "owner sees all sites" seam (branch
  b) drew its source from the organization edge; when the organization ceases to be the operational
  edge, that seam loses its source. Its rehoming is a cascade decision, explicitly deferred — until
  then the seam's legacy source may persist transitionally.
- **ADR-ARCH-008 — amended on one point.** It framed `access.memberships` as the organization-level
  authorization bridge, folding `member_of` (belonging) and `access` (authorization) into one
  relation. The model separates them: `member_of` is organization CONTEXT, not authorization;
  organization-scope ACCESS is a distinct edge, not yet produced. The identity/observer path
  (`access.profiles` → `observer_name` / `observer_function` → the constat write-path guard) does
  NOT traverse membership and is unaffected.

Whether the ADR-008/009 corrections are expressed as amendments or as replacements is settled when
each patch is written, not here. This ADR records WHERE the divergences are.

## Deferred — out of this decision, flagged so they are inherited

- The FORM of the Founder Access edge (implementation design, ID-3.4) — do not presuppose it here.
- Inheritance / cascade between scope levels (does ownership or organization access reach down to
  workspaces and sites?), including the rehoming of ADR-009's cross-scope "owner" seam.
- Roles and permissions at any scope; delegation.
- Table DDL, RLS policy content (#50), and PowerSync publication membership (implementation design,
  ID-3.5).
- Additional scope levels are not decided by this ADR; each is revealed and validated by the process
  that requires it. The **Site** grain is not such a future unknown — it is an accepted prior decision
  (ADR-010), inherited here and not reopened; what remains open is only the process that will produce
  its access edge.
- Re-parenting of `site.sites` under the workspace and the workspace-relative form of
  `origin_tenant_id` — schema consequences on empty tables, settled with the site schema (#47).

## Open — an unvalidated domain assumption

This model rests on workspaces being genuinely operationally isolated in the target market (a member
of one agency is not automatically a member of another). That fact was gravé for the domain
(organization 1:N workspace), but — like ADR-ARCH-010's own open assumption — no one on this project
has field knowledge of the Ivorian BTP market. It shares that ADR's DNA and should be confirmed with
the business partners alongside ADR-010's question. Logged, not a blocker, not reopened.

## Alternatives considered

- **"Organization is not an authorization scope."** Rejected: it confuses "no edge is produced yet"
  with "the level is not admitted". It would leave organization-wide authorization (billing, member
  management, deletion) with no home in the model, so those processes would arrive as out-of-model
  exceptions — the exact hacks this ADR prevents.
- **Build the Organization access edge now.** Rejected: no current process produces an
  organization-scope access relation. A writer-less, consumer-less edge is dead schema debt — the
  trap the site edge already illustrates (a table with no writer and no production caller until a
  later story).
- **Three parallel operational edges, with Organization as an active operational grain.** Rejected:
  under the 1:N fact the organization grain cannot express per-workspace operational action; the
  operational edge belongs at the Workspace level. Organization is a scope for organization-wide
  processes, not an operational grain.
- **Collapse Membership into Access (the owner is "a member with an owner role").** Rejected: it
  re-merges the two axes the model keeps distinct — belonging answers "who is part of the
  organization", access answers "where may they act". Making membership a privileged access role is
  the same conflation the model breaks apart.

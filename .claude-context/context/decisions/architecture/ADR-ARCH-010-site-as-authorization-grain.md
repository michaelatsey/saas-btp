# ADR-ARCH-010 — The site is the authorization grain; lot, zone, phase and trade are data dimensions

Status: accepted
Date: 2026-07-14
Refs: ADR-ARCH-009 (relationship-based authorization over two independent edges), ADR-ARCH-005
(action context travels in the command payload), migration 0004_site_model (site.sites +
site.site_memberships), issues #47 (this schema), #48 (onboarding), #50 (RLS).

## Context

A construction site is subdivided in several INDEPENDENT ways at once — by lot (gros oeuvre,
plomberie, electricite), by zone (batiment A, R+2, parking), by phase (terrassement, structure,
second oeuvre) — and every person on it also has a trade (corps de metier). None of these
subdivisions nests inside the others: they are orthogonal.

The question this ADR settles: is a subdivision an AUTHORIZATION PERIMETER ("Paul may only see the
plumbing lot") or a DATA DIMENSION ("this constat concerns the plumbing lot, and everyone on the
site sees it")?

The question is load-bearing because the answer decides the GRAIN of the site edge — and the grain
is the one thing this project has committed to freezing (ADR-ARCH-009). Changing it later costs the
edge table, `ISiteScopeProvider`, the PowerSync sync rules, and the RLS policies (#50) — three
places that must agree, one of which fails SILENTLY.

## Decision

**The site is the authorization grain.** `site.site_memberships` defines whether a user participates
in a site's scope. It is the finest perimeter the system authorizes on.

**Lot, zone, phase and trade are BUSINESS DIMENSIONS.** They drive filtering, reporting and domain
workflows. They live as columns or tables on the records they describe (`safety.constats`, a future
`site.lots`, ...). They are NOT authorization dimensions in the current model, and they never appear
on the authorization path.

**Site authorization defines the PARTICIPATION BOUNDARY, not a uniform permission.** Being in a
site's scope does not imply identical rights over every operation within it. Command-level
permissions are resolved separately, in C#, from the role held on the edge (ADR-ARCH-009).
`site_membership` must never be read as "full CRUD on everything in the site".

### Trade (corps de metier) in particular is not on the edge

The temptation is to put it on `site.site_memberships` ("Paul is a plumber"). Rejected: a trade that
does not authorize has no business on the authorization table, and a trade that DOES authorize means
the grain was reopened without an ADR.

The concept already exists where it belongs — `access.profiles.job_function` (descriptive, stamped
into the JWT as `observer_function`) and on the constat itself (which trade the finding concerns).
"Paul is a plumber" and "this finding concerns plumbing" are different facts; a QHSE officer may
perfectly well raise a finding about the plumbing lot.

If trade-based authorization ever becomes a real requirement ("only an electrician may raise an
electrical non-conformity"), it is a PERMISSION — resolved in C# from the role and the command, per
ADR-ARCH-009 — not a column on the edge. Should it nonetheless require a change to the authorization
grain, that change needs its own ADR. It is never satisfied by adding a column to
`site.site_memberships`.

## Guardrail — how this decision dies if nobody defends it

The failure mode is not a decision, it is a COLUMN. Someone adds:

    site.site_memberships (..., lot_id uuid NULL, zone_id uuid NULL, phase_id uuid NULL, trade varchar NULL)

and the grain is reopened by accident, in a migration nobody reviewed as an architectural change,
with nullable columns whose semantics live only in C#.

**Therefore: no sub-perimeter column may ever be added to `site.site_memberships`.** Extending the
authorization grain below the site requires its OWN ADR, which must explicitly price the four
consumers it breaks (edge table, provider, PowerSync sync rules, RLS policies) and state how the C#
and SQL expressions of the new perimeter are proven to agree.

## Consequences

- The grain frozen in #47 stands. `ISiteScopeProvider.CanActInSiteScopeAsync(userId, tenantId,
  siteId)` remains correct, and — deliberately — its NAME does not enclose the grain: it asks "may
  this user act in the scope of this site", not "is this user a site member". A finer perimeter would
  change its implementation, not its contract.
- A subcontractor assigned to a site is authorized AT SITE LEVEL. The product does not restrict
  visibility by lot, zone or phase. This is a statement about the system's current CAPABILITY, not a
  claim about what the construction industry requires. Introducing such a restriction is an explicit
  change to the authorization model (see Guardrail).
- Invitations (#48) carry SITES, never lots. This holds regardless of how the open question below is
  answered, so #48 is not blocked by it.
- Data dimensions can be added freely and cheaply: a `lot_id` on `safety.constats` is an append-only
  migration with zero authorization impact.

## Open — this decision rests on an UNVALIDATED assumption

Neither the author nor any AI consulted on this project has field knowledge of the Ivorian BTP
market, which is the entry market. The site-level model is inferred from the common pattern of
project-level authorization with business dimensions used for filtering and reporting — not from the
customers this product targets.

**The question to put to the business partners, before any real membership row exists in production:**

> On a site, when a subcontractor (plumbing, electrical) works there: must they be able to see ALL
> the safety findings and non-conformities on that site, including those that do not concern their
> lot? Or only what touches their lot?

If the answer is "only their lot", this ADR is superseded and the grain reopens — and it must be
reopened WHILE `site.site_memberships` IS STILL EMPTY. The cost is low today (no data to protect,
exactly as `access.profiles` was empty at #45) and high once production rows exist.

This is the single decision on record whose cost is asymmetric in time. It is written here so that it
is asked, not assumed.

## Alternatives considered

- **Sub-perimeter columns on the edge** (`lot_id`/`zone_id`/`phase_id`, NULL = all). Rejected as the
  default: it triples the C#/SQL duplication ADR-ARCH-009 already flags as silently divergent, for a
  need no customer has stated. It is also the shape a future ADR would have to justify explicitly —
  not slide in.
- **A policy engine (ABAC / OPA / Cedar) evaluating arbitrary perimeter expressions.** Rejected:
  PowerSync cannot query it, so the offline device could never be told what to sync. It would also be
  the "generic engine" the product strategy explicitly rejects (Projets.docx).
- **`trade` on `site.site_memberships`.** Rejected above.

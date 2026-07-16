# Session — 2026-07-15 (BP-001: first full Process-First run, through Implementation Design)

Continues: 020 (Process-First Design Method charter + glossary). First feature ever run end-to-end
through the method.

Purpose on entry: resume #48 step 3 and decide Access persistence (EF vs Npgsql). What actually
happened: we did NOT touch #48. We ran a NEW process, BP-001 (Créer son espace entreprise), through
the Process-First method steps 0-9, then into Implementation Design (step 10) — which is NOT finished:
it is suspended mid-ID-3 by a domain revision (Organization 1:N Workspace) that must land before the
authorization decision.

---

## What shipped (PRs merged)

- **#65** — BP-001 conceptual model (spec) + **ADR-ARCH-012** (person/account/organizations/ownership)
  + **ADR-PROD-002** (no legal verification of the declared org) + **ADR-ARCH-011 amended**
  (BP-001 is the founding entry path, not operator provisioning) + decisions-index updated.
- **#66** — BP-001 domain revision: **Organization 1:N Workspace**; BP-001 creates the first
  workspace; INV-1 and D5 amended; workspace identity is unique within its organization (not global).
- Earlier this arc: #60 (Process-First charter), #62 (glossary).

---

## The reframing that started everything

#48 was NOT the product's first process. I had mis-taken an invitation EXAMPLE (given to illustrate
"join an org") as the founding entry path, and that mistake dragged the whole design toward
invitation/operator-provisioning. The owner corrected it:

- **BP-001 — Créer son espace entreprise** is the FIRST functional business process. A director,
  external to the platform, creates their first organization and becomes its Initial Owner.
- Invitation / operator-provisioning are LATER processes (membership, not founding).
- A founder is not an invited member: there is no one above a founder to invite them.

This reconciled with ADR-ARCH-011: its security invariant (no unguarded generic signup) STANDS; its
"no self-service in Phase 1" was a scope assumption ADR-ARCH-011 itself said a future ADR would lift —
that ADR is ADR-ARCH-012. Founding creation is a GUARDED business process, not an anonymous signup.

---

## BP-001 — the validated business model (steps 0-9, gravé in #65, revised in #66)

- **Actor**: a BTP company director, external to the platform. **Self-service, no platform-side human
  in the boundary.**
- **Trigger**: the director requests creation of their company space.
- **Boundary end**: the organization exists with its first workspace, owned by the founder, ready to
  use. OUT of boundary: commercial cycle, login (a future BP), first site, inviting collaborators,
  additional workspace creation.
- **Tasks**: TSK-1 identify the requester → TSK-2 collect organization context → TSK-3 create
  organization + first workspace + ownership + founder access. (TSK-4 was removed: the state model
  proved no business transformation between "created" and "operational".)
- **Rules/invariants**: see the spec. Key ones below in DO NOT LOSE.

---

## Decisions taken this session (with their category)

### Conceptual (Category A — written now)

- **ADR-ARCH-012** — Model: Physical person → account → N organizations → each org owns workspace(s).
  **Ownership** (property relation) ≠ **Membership** (access relation) ≠ Identity; never conflated.
  **Initial Owner** = the founder. Ownership and Membership are revoked independently (owning ≠ being
  able to act). Cardinality person↔account left OPEN (not decided). Vocabulary is business-only;
  Organization↔tenant correspondence is a HOW. Notes a divergence with ADR-ARCH-008 (founding creation
  is autonomous, not folded into InviteMemberCommand) — identified, not resolved.
- **ADR-PROD-002** — BP-001 does NOT verify the legal link between requester and declared
  organization. Ownership is established on a DECLARED basis; legal proof is another process.

### Technical (Category B)

- **Access persistence = Npgsql (plain SQL), NOT EF Core.** Decided at the BOUNDED CONTEXT level, not
  for BP-001. Criterion (durable, gravé to become an ADR at step 11): *the persistence technology is
  chosen by HOW the domain is manipulated* — a context that truly leverages an ORM (change tracking,
  identity map, graph rehydration) → EF (that is Safety/Constat); a context of explicit transactional
  commands (identity, authorization, provisioning, workflows) → plain SQL (that is Access). NOT decided
  by table count or "aggregate presence". DbUp stays the DDL owner (ADR-ARCH-006). The old session-019
  "AccessDbContext, EF as mapper" direction is REVERSED. ADR to be written at step 11 (next free
  ADR-ARCH number).
- **D4** (identity/creation coherence) — RESOLVED, and it is CONCEPTUAL, stays in the spec, NOT a
  technical ADR: identity (`createUser`, HTTP) is created OUTSIDE the transaction; then a business
  transaction. Order imposed by INV-4 (no orphan workspace allowed; an orphan IDENTITY is harmless and
  adopted). Switch point = commit of the business transaction. The identity-establishment GUARD (proof
  of control before createUser — mechanism deferred: email/OAuth/passkey/SSO) lives INSIDE TSK-1.
- **D5** (concurrent instances) — RESOLVED, conceptual, in the spec: uniqueness concerns the VERIFIED
  organization (deferred to a verification process), NOT the number of workspaces. Detection of an
  existing org = user-journey aid, never atomic refusal.

---

## Implementation Design (step 10) — WHERE IT IS SUSPENDED

Order used: ID-1 responsibilities → ID-2 logical model → ID-3 persistence (table by table).

**Validated logical model (ID-1/ID-2):**

    Profile (existing, access.profiles, referenced — NEVER recreated)
       ├─ Ownership ─────→ Organization (Status = Declared)
       │                        │ owns (1:N)
       │                        ▼
       └─ Founder Access ──→ Workspace (the FIRST, created by BP-001)

- Ownership → Organization (you own the company, not the space). Minimal object, no role, no history.
- Workspace is a SEPARATE entity, not an attribute of Organization (different responsibility).
- Founder Access → Workspace. **Its edge FORM is NOT decided** (blocks on Act 2, below).
- Profile: for BP-001, the founder arrives WITH NO ACCOUNT. Unlike invitation (session 019),
  BP-001 CREATES the identity (`createUser`) AND the `access.profiles` row before attaching
  Ownership/Access. "Profile existing, referenced, never recreated" is TRUE for invitation, FALSE
  for founding: BP-001 is a profile CREATOR. This adds a write to TSK-3's sequence.

**Persistence tables decided (ID-3.1–3.3), conceptual level (no final DDL):**

    access.organizations
      id uuid (UUIDv7, domain-gen) · display_name text NOT NULL · description text NULL
      · status text CHECK (status='declared') · created_at_utc timestamptz (domain-stamped)
      NO: owner_id, created_by, updated_at, tenant_id

    access.workspaces
      id uuid · organization_id uuid FK · display_name text NOT NULL · description text NULL
      · slug (navigation identity) · created_at_utc timestamptz
      slug UNIQUE within its organization (NOT global) — 1:N model
      NO: status, updated_at, owner-ref

    access.ownerships
      id uuid · organization_id uuid FK UNIQUE · profile_id uuid FK · created_at_utc timestamptz
      NO: role, workspace_id, is_initial, history columns

**Access conventions gravées:** ids are `uuid`, UUIDv7, domain-generated, no SQL DEFAULT. Timestamps
are `*_utc`, `timestamptz`, domain-stamped, no `DEFAULT now()`. (`_utc` suffix is a deliberate,
documented divergence from Safety's `created_at` — makes the UTC invariant visible in the schema.)

**ID-3.4 (Founder Access) — BLOCKED. This is the resume point.**

---

## THE BLOCKER — Act 2 (authorization), not yet started

ID-3.4 must persist "the founder can act in the created space", which forces the question the whole
session circled: **is the Workspace an AUTHORIZATION PERIMETER?**

That question surfaced a deeper DOMAIN fact, now gravé in #66: **an Organization has 1:N Workspaces**
(BTP reality: a company runs several autonomous operational spaces — agencies, activities, entities —
whose teams do not necessarily see each other's data; e.g. Construction / Maintenance / external
Quality Control). The clinching argument was the owner's: "does every org member automatically see all
workspaces?" — in 1:N, the organization edge gives all-or-nothing and CANNOT express "member of
Abidjan, not Dakar". So if workspaces are operationally independent, the workspace must be able to
become an authorization perimeter.

**Current authorization model (facts read this session from ADR-ARCH-008/009/010):**
- TWO independent edges: `access.memberships` (user→tenant = organization edge) and
  `site.site_memberships` (user→site edge). Neither implies the other (ADR-ARCH-009).
- `tenant` in the repo = the Organization (business). No "workspace" perimeter exists anywhere.
- ADR-ARCH-010: the site is the FINEST authorization grain; lot/zone/phase/trade are DATA dimensions.
  It does NOT say "site is the only perimeter" and does NOT forbid perimeters ABOVE the site. **No
  amendment of ADR-ARCH-010 is needed** (verified by reading it).

**The three-act split agreed (do not merge):**

    Act 1 — Domain (1:N)          ✅ DONE, merged #66
    Act 2 — Authorization         ← NEXT SESSION, the hard part
    Act 3 — Back to ID-3.4+       ← after Act 2

**Act 2 will require (this is the real work of next session):**
- ADR-ARCH-013 is a FULL ADR, not a refinement of ADR-ARCH-010. The workspace exists NOWHERE in the
  current authorization model (two edges: tenant, site). Making it a perimeter is a STRUCTURAL
  addition (amends "two edges" → "three"), same rank as ADR-ARCH-012 — not a minor refinement.
- **Amend ADR-ARCH-009** — "two independent edges" → THREE (organization, workspace, site).
- **This is heavy**: ADR-ARCH-009 mandates each edge be expressed THREE times — C#
  (`ISiteScopeProvider`-style), SQL (PowerSync sync rules), RLS (#50) — with a Testcontainers test
  proving they agree, or the failure is SILENT. A third edge = a third C#/SQL/RLS set to keep coherent.
  This must be PRICED explicitly in the ADR, not slid in.
- Only after Act 2: resume ID-3.4 (Founder Access = a workspace edge), then ID-3.5 (transverse
  constraints: FKs, unique, RLS, UUIDv7/domain-stamp conventions), then step 11 (write the Npgsql-Access
  technical ADR + any Act-2 ADRs), THEN the Claude Code prompt.

---

## Method learnings (for building the Process-First agent — see below)

- **The method's whole point proved out**: we started from a local need (Founder Access) and it forced
  a product-model decision (1:N, ownership vs access) that dwarfs the process. Caught because we refused
  to persist before modelling.
- **I repeatedly reached for two anti-patterns; the owner blocked each**:
  1. letting the EXISTING schema (`access.memberships`, `tenant_id`) dictate the target model — corrected
     to "model decides, existing confirms or reveals a gap";
  2. letting a FUTURE hypothetical impose present complexity (ownership history, workspace-as-perimeter
     "because capabilities will come") — corrected to "the future informs boundaries, not present form".
- **The owner's own test settled the hardest call**: "does BP-001 produce this data / this distinction?"
  Workspace-as-perimeter was justified NOT by future capabilities (rejected) but by the STRUCTURAL 1:N
  fact (an org has independent spaces → org edge can't express per-space access). That is a domain
  argument, and domain arguments win.
- **Category A vs B discipline held**: conceptual ADRs written at step 6 (012, PROD-002); technical
  ones (persistence) deferred to step 11. D4/D5 correctly classified as conceptual (stay in the spec),
  NOT technical ADRs.
- **"Minimal ≠ poor"**: descriptive properties (display_name, description nullable) belong to the
  concept NOW; lifecycle/provenance properties (status, updated_at, created_by, owner_id) wait for the
  process that maintains them. The line: does it need a workflow to stay true? → defer. Is it just what
  the concept IS? → keep.
- **Amending an ADR requires READING it first.** Twice the owner suspected ADR-ARCH-010/011 blocked us;
  reading them showed they did not. Never amend from memory.
- Several GPT cross-reviews were arbitrated, not adopted (e.g. OrganizationCreationJourney as a durable
  process trace → correctly ruled out of BP-001 and logged as an open product question).

---

## DO NOT LOSE

- **Public signup stays DISABLED permanently** (ADR-ARCH-011, unchanged).
- **BP-001 creates: identity (`createUser`, out of tx) + access.profiles + Organization(Declared)
  + FIRST Workspace + Ownership(→Org) + Founder Access(→that first Workspace)**, the SQL writes in
  one business transaction after createUser. Founder Access is attached to the FIRST workspace
  specifically (1:N: later workspaces get their own access attribution, a future process).
- **INV-4**: no active workspace without an owner (imposes identity-first ordering; orphan identity is
  tolerated + adopted, NEVER an orphan workspace).
- **Workspace slug is unique WITHIN its organization**, not global. Form of navigation resolution
  (slug alone / org+workspace / public UUID) is NOT decided — a later concern.
- **Founder Access edge form is UNDECIDED** — depends on Act 2. Do NOT default it to `access.memberships`
  (that would presuppose organization-grain) NOR invent a table before Act 2 decides.
- **Act 2 is the resume point**, and it touches the authorization CORE. Do it COLD, not in a marathon.
  Price the three consumers (C#, PowerSync, RLS).
- **Access persistence = Npgsql** (decision made; ADR to write at step 11).
- **Open product questions logged** (not ADRs, future Process-First passes):
  - Process Creation Trace (funnel/support/audit observability of creation intents).
  - Organization reference identifier (for strong uniqueness) — the verification process.
  - "Create additional workspace" — a future BP distinct from BP-001 (candidate: BP-00X), with its
    own access attribution for the new workspace.
- **ADR-ARCH-010's own unvalidated assumption still stands** (from session 019, unrelated to this work
  but still owed to the business partners): subcontractor sees whole site or only their lot? Ask while
  `site.site_memberships` is empty.
- **RLS on the new Access tables (organizations, workspaces, ownerships) is an ID-3.5 concern, NOT
  Act 2.** Same trap as ADR-ARCH-009's "RLS on / no policy = silent zero rows": the day a
  non-privileged consumer (PowerSync) reads them, a policy is mandatory. Note it now, decide it at
  ID-3.5. Do NOT load Act 2 with it.

---

## Next session — the prompt to give me

Paste this to resume cleanly:

> Reprise SaaS BTP, mes règles habituelles s'appliquent. Lis la session 021
> (021-2026-07-15-bp-001-process-first-first-run.md) et la spec BP-001
> (product/specifications/BP-001-creer-espace-entreprise.md), ADR-ARCH-008, ADR-ARCH-009, ADR-ARCH-010,
> ADR-ARCH-012.
> On reprend à l'ACTE 2 de BP-001, à froid : le Workspace devient-il un périmètre d'autorisation ?
> On traite ça Process-First / niveau architecture : décision des NIVEAUX de périmètre
> (Organization / Workspace / Site) uniquement — pas les rôles/permissions/héritage/délégation.
> Livrables attendus : ADR-ARCH-013 (workspace comme périmètre) + amendement ADR-ARCH-009 (deux arêtes
> → trois), avec pricing explicite des trois consommateurs (C# provider, PowerSync sync rules, RLS #50)
> et le test d'accord C#/SQL exigé par ADR-ARCH-009. Ne rien graver avant que le modèle d'autorisation
> soit stable. Après l'Acte 2 seulement : retour à ID-3.4 (Founder Access), puis ID-3.5, puis l'ADR
> technique de persistance Access (Npgsql), puis le prompt Claude Code.
> Rappelle-moi aussi qu'on veut construire l'agent Process-First (+ sous-agents + règles) une fois BP-001
> livré.

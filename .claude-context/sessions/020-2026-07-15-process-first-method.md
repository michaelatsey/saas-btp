# Session — 2026-07-15 (Process-First Design Method — project charter)

Continues: 019 (#48 steps 1-2, onboarding model). No code shipped this session.

Purpose on entry: resume #48 step 3, decide EF vs Npgsql for Access (ADR-ARCH-012).
What actually happened: the persistence question kept pulling toward reopening SETTLED
decisions (the grain, the two edges). We stopped, and instead installed the method that
prevents that class of drift. A convention was written; #48 step 3 did NOT start.

---

## What shipped

- **`process-first-method.md`** — new project convention at
  `context/architecture/conventions/process-first-method.md`. The 11-step charter:
  model the business process before any infrastructure name appears.
- **`CLAUDE.md`** — four inserts wiring the charter into governance (navigation line,
  a Feature-design convention, an enriched ADR convention, a Stack guardrail line).

Both are conventions, not features. No DDL, no command, no migration.

---

## Why this session exists — the FACTS report on ADR-ARCH-012

A read-only facts investigation was run before writing ADR-ARCH-012. It killed the
"agreed, not ratified" direction from 019:

- **The outbox argument is false.** `EfOutboxStore<TContext>` does NOT exist in the
  packages this solution resolves (`Persistence.Abstractions preview.2`; `IOutboxStore`
  only ships in stable `1.0.0`). "Access gets a DbContext so step 5 consumes the outbox"
  had nothing to consume.
- **"Exactly what SafetyDbContext does" is not a precedent.** Safety is a plain
  `DbContext`, wired only via `UsePostgreSQL` (scoped `AddDbContext`, nothing else), no
  UoW, no repository, no transaction, NOT composed by the Host, and `SafetyDb` is absent
  from every appsettings. It has only ever run inside a Testcontainers mapping test.
- **No explicit transaction exists anywhere in the repo yet.** Step 3 writes the first.
  So the persistence choice is a choice of TRANSACTION OWNER, not of mapper.
- **Step 6 (`AcceptInvitation`) writes TWO schemas in ONE transaction** — `access` ×3
  plus `site.site_memberships`. Site has no DbContext and its architecture test forbids
  one. An `AccessDbContext` cannot map the `site` write; raw Npgsql owns both schemas
  with one connection, one transaction. This is the decisive fact.

ADR-ARCH-012 was therefore NOT written. It waits for step 3's ideal flow to state what
it needs (per the new method), not before.

---

## The method — why it was installed now

Two moves during the session were the trigger:

1. I twice pulled toward reopening the grain / merging the two authorization edges to
   make the persistence choice easier. Both are SETTLED (ADR-ARCH-009/010/011). Using an
   infrastructure constraint to reopen a domain decision is exactly the drift the method
   forbids.
2. We discovered a possible **P0 missing in front of step 3**: the CEO sign-up. "Operator
   provisioning" assumes a subscription already happened off-product; nobody had modelled
   the actual first gesture (a director wants to subscribe). This is what infra-first
   design hides — and what Process-First step 0 (actor + trigger) surfaces.

The owner named the method (Process-First Architecture) and set the working order:
list flows → define the IDEAL enchaînement infra-abstracted → confront to infra → one
problem at a time.

### The 11 steps (as gravé)

0. Actor + Trigger · 1. Process boundary · 2. Business actors · 3. Tasks ·
4. Task contract (Trigger·Owner·**Preconditions**·Inputs·Rules·Outputs·Errors·Events) ·
5. Business rules · 6. ADR discovery (Category A conceptual = decided+written now;
Category B technical = identified now, decided at 10, written at 11) · 7. State model ·
8. Conceptual Mermaid (no class diagram, no entity model, no API) · 9. Validation gate ·
10. Implementation design · 11. ADR finalization.

Two GPT cross-reviews were arbitrated, not adopted wholesale:
- Accepted: `Preconditions` in the task contract; "stable across reasonable technology
  choices" over "true regardless of stack"; the infra-feasibility guardrail; the Mermaid
  clarifications.
- Rejected with cause: adding security/observability/deployment to step 10 (turns a
  process-design method into a delivery checklist — that is `build-checklist.md`'s job);
  a new `context/methods/` folder (one occupant — rule of three); a Current-state line
  duplicating the Conventions rule.

---

## Learnings

- **An infrastructure constraint is never a licence to reopen a domain decision.** The
  method exists because I reached for that licence twice in one session. Focus is a rule.
- **A facts report can invalidate a direction that felt settled.** The EF direction had
  three supporting intuitions (outbox, "like Safety", mapper convenience); the facts
  removed all three. Read the code before ratifying the plan.
- **The missing process is invisible from inside the découpage.** Steps 1-7 of #48 never
  contained the CEO's first gesture, because the découpage started one step too late.
  Step 0 of the method is precisely the guard against that.
- **A convention not referenced in `CLAUDE.md` becomes optional.** Writing the charter was
  half the work; wiring it into navigation + conventions + the ADR rule is what makes it
  binding.

---

## Where things stand — #48, still 2 of 7 steps

No progress on the epic this session (by design — we built the tool, not the feature).

- [x] 1. GoTrue spike
- [x] 2. Schema (`0005`) — merged, PR #58
- [ ] **3. Operator provisioning** ← NEXT, now to be modelled Process-First from step 0
- [ ] 4. `CreateSite`
- [ ] 5. `CreateInvitation`
- [ ] 6. `AcceptInvitation`
- [ ] 7. `GET /me/workspaces`

ADR-ARCH-012 (Access persistence) remains OPEN — a Category B decision, to be taken when
step 3's ideal flow reaches implementation (method step 10), not before.

---

## DO NOT LOSE (carried from 019, still live)

- **The CEO sign-up (possible P0) is unmodelled.** Before step 3 code: run step 0 of the
  method on it. Open question the owner has not yet answered: does the operator enter
  IMMEDIATELY (self-service pay → tenant created), or is there a qualification step
  between us first? This decides whether P0 exists in front of provisioning.
- **The unvalidated grain assumption (ADR-ARCH-010).** Still owed to the business partners
  while `site.site_memberships` is EMPTY: does a subcontractor see the whole site or only
  their lot? If "only their lot", the grain reopens.
- **Public signup stays DISABLED permanently** (property of the model, ADR-ARCH-011).
- **Step 6 crosses two schemas in one transaction** — the fact that will drive ADR-ARCH-012
  toward raw Npgsql + a named `ISiteMembershipWriter` port (owned by Access, implemented in
  Site — inverted dependency, keeps the Access architecture test green). Not yet decided.
- **`CreateInvitation` (step 5) MUST carry the two normalization tests.** Re-inviting =
  REVOKE-then-CREATE in one transaction. `AcceptInvitation`: no compensation. Admin API
  lookup is a PREFIX search — exact match, `Count == 1`, else abort.
- **The MicroKit.Messaging issue is drafted LOCALLY, not committed.**
- Carried: PowerSync publication must include `access.memberships`; Microsoft.OpenApi 2.10.0
  pin; offline conflict-resolution ADR; `core.hooksPath`; CI (#38) deferred; 0005 leaves
  both invitation tables without a writer and RLS-enabled-no-policy (#50).

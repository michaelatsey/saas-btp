# Architecture

Status: initial decomposition (2026-06-28). Living document — evolves as increments
ship. Steering: docs/product/scope-phase-1.md. Decisions: ../decisions/decisions-index.md.

> Backend: .NET 10 modular monolith — Hexagonal, DDD, CQRS, built on the MicroKit
> ecosystem. Not microservices (CLAUDE.md 20). Contract boundary: OpenAPI
> (ADR-ARCH-018). Naming: conventions/naming.md.

---

## 1. Bounded contexts

### Platform
- **Access** — the people, organizations and workspaces of the product, and the ownership,
  membership and workspace access that tie them (`docs/domain/access.md`, ADR-ARCH-031).
  The workspace is the tenant. Authentication itself is the identity provider's; Access holds
  the profile that projects a person. Foundational: every other context assumes a signed-in
  person acting in a workspace.

### Domain
- **Site** (Chantier) — a construction site and what is scoped to it. A site belongs to
  exactly one workspace (ADR-ARCH-031). Foundational alongside Access.
- **Safety** — safety audits, sensibilisations, constats (incident / accident /
  dangerous situation). Source of safety findings.
- **Quality** — quality audits, non-conformities. Source of quality findings.
- **CorrectiveActions** — the issue -> action -> validation -> closure lifecycle.
  Transversal and SEPARATE (removed ADR-ARCH-002, tag `archive/pre-reset`). Consumes findings
  from Safety and Quality;
  owns the corrective-action entity and its state machine.
- **Workforce** — daily labor tracking.
- **Stock** — site stock (deliveries, withdrawals, thresholds).
- **Reporting** — daily reports, weekly objectives, blocking points, and operational
  dashboards (read-models projected from other contexts).

### Cross-cutting supports
- **Media** — photos, signatures, attachments. Stored in Supabase Storage, referenced
  by id from other contexts (no binary coupling).
- **Notifications** — reacts to integration events (reminders, critical-severity
  alerts). Nothing depends on Notifications; it only consumes.

### Not bounded contexts (architectural concerns, not domains)
- Offline sync (PowerSync) — device <-> database synchronization.
- Outbox/Inbox messaging (MicroKit.Messaging) — inter-context communication.

---

## 2. Inter-context communication

- Contexts do NOT reference each other directly. They communicate via integration
  events through the outbox/inbox pattern (MicroKit.Messaging), at-least-once with
  idempotent handlers; an outbox is introduced with the first event a consumer needs
  (ADR-ARCH-032).
- Authorization across contexts: every policy on workspace data calls its context's own
  `<context>.may_act_in(workspace_id)`, which delegates to `access.may_act_in`, published by
  Access; no context reads another's tables (ADR-ARCH-032, ADR-ARCH-033).
- Extraction: every context shares one database today, and any context can be moved out as a
  service with its own storage. No foreign key crosses schemas; a reference to another
  context's record is its identifier; a context's `may_act_in` is the one function to replace
  when it moves out (ADR-ARCH-033).
- Example (increment 1): Safety raises `SafetyConstatRaised` -> CorrectiveActions
  reacts to create/track a corrective action; CorrectiveActions raises
  `CorrectiveActionDue` -> Notifications schedules a reminder.
- Shared identity: workspace data carries `workspaceId` (Access, the tenant); site data also
  carries `siteId` (Site).
- Media and Notifications are referenced/triggered by id and events, never by direct
  domain coupling.

> Two distinct sync layers — do not conflate:
> - PowerSync = client device <-> Postgres (offline-first field data).
> - MicroKit.Messaging = context <-> context inside the backend (outbox/inbox).

---

## 3. Dependency rules

- Access and Site are foundational; domain contexts depend on them for scoping, not
  the reverse.
- CorrectiveActions depends on no domain context at compile time — it reacts to
  events from Safety and Quality (inversion via integration events).
- Notifications and Media are sinks: consumed by others, depend on no domain context.
- No circular dependencies. Any new inter-context dependency is recorded as an ADR.

---

## 4. Modular monolith mapping

- One bounded context = one module in the .NET modular monolith.
- Each module: three projects, `SaasBtp.<Context>.Domain`, `.Features` (vertical slices, an
  endpoint calling its handler, no mediator) and `.Adapters` (port implementations and the
  module's single entry point), with its tests in the module's `tests/` folder; beyond that, a
  module's internal design is its own (ADR-ARCH-034).
- MicroKit consumption, subject to the MicroKit decision of #177:
  - Domain, Result — everywhere.
  - Persistence (EF Core, PostgreSql) — repositories, outbox/inbox storage.
  - Messaging — inter-context integration events.
  - Auth, Tenancy — the Access context, subject to the workspace as tenant (ADR-ARCH-031).
- Audit fields and soft delete follow `sql.md` (Audit / lifecycle) and `naming.md`; this file
  sets no rule of its own on them.

---

## 5. Offline-first / sync strategy

- Local-first writes on the field client; automatic sync on reconnect; optimistic UI.
- PowerSync synchronizes the field client with Postgres (Supabase).
- Conflict resolution: NOT blindly last-write-wins for safety data — strategy to be
  decided (open). Candidate approaches recorded when ADR is written.
- Photos: captured offline, uploaded independently on sync (Media); never block the
  parent record's sync.
- Platform of the increment-1 field client (offline web PWA vs Phase 2 Expo app):
  OPEN — see the removed ADR-ARCH-003 (tag `archive/pre-reset`). This gates whether increment 1
  ships on web or mobile.

---

## 6. Deployment topology

- Coolify project `saas-btp`, with production and staging environments.
- `apps/api` (.NET) and `apps/web` (Next.js) deploy as separate pipelines.
- `apps/mobile` (Expo) — Phase 2.
- Data: Supabase (Postgres + Auth + Storage). Infra config under `infra/`.

---

## 7. Increment 1 — contexts in play

`Access`, `Site`, `Safety` (constat), `CorrectiveActions` (lifecycle), `Media`
(photos), `Notifications` (reminders/critical alerts). Quality, Workforce, Stock,
Reporting are NOT built in increment 1.

---

## 8. Open decisions

| Decision | ADR |
|----------|-----|
| Market entry = Safety first | ADR-PROD-001 (decided) |
| CorrectiveActions as a separate context | removed ADR-ARCH-002 (tag `archive/pre-reset`), not re-decided |
| Increment-1 field client platform (PWA vs Expo) | removed ADR-ARCH-003 (tag `archive/pre-reset`), open |
| Offline conflict-resolution strategy for safety data | future ADR (open) |

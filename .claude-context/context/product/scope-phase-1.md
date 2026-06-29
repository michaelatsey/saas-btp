# Scope — Phase 1 (Vertical MVP)

Status: defined (2026-06-28). This is a STEERING document (strategy, increments,
goals, metrics, risks, validation). It does NOT hold detailed feature specs — those
live in `specifications/`. It does NOT hold architecture debates — those live in
`decisions/`. Source of truth for features: Projets.docx. Product rules:
CLAUDE-SAAS-BTP.md. Market rationale: ADR-PROD-001.

---

## 1. Principle

Phase 1 is NOT built all at once. The full Phase 1 perimeter is the TARGET; it is
delivered as a sequence of vertical increments. Each increment is end-to-end
(capture -> lifecycle -> offline sync -> dashboard) and ships real field value.

This protects the two biggest unknowns from being deferred: offline-first
reliability (the technical bet) and the corrective-action lifecycle (the product
differentiator per Projets.docx).

Only the CURRENT increment is detailed below. Later increments stay one line in the
sequence table until we attack them — no upfront work on deferred increments.

---

## 2. Phase 1 target perimeter (the destination)

Safety (audits, sensibilisations, constats), Quality (audits, non-conformities),
Corrective Actions (transversal), Reporting (daily/weekly/blocking points),
Workforce, Stock, Offline synchronization (cross-cutting from increment 1).

Explicitly OUT of Phase 1 (CLAUDE-SAAS-BTP.md 5): workflow designer, BPMN editor,
generic automation, advanced form builder, rule engine, no-code system.

---

## 3. Increment sequence

| # | Increment | Why this order |
|---|-----------|----------------|
| 1 | **Safety constat -> Corrective action -> dashboard** | Market entry is safety (ADR-PROD-001); constat carries a full lifecycle; proves offline + corrective-action cycle at once |
| 2 | Quality non-conformity -> Corrective action | Reuses the proven CorrectiveActions contract; only the source context changes |
| 3 | Safety audits + sensibilisations | Structured capture on a proven Safety context |
| 4 | Daily / weekly reporting + dashboards | Aggregates existing data; read-model heavy |
| 5 | Workforce + Stock | Independent, lower-risk domains |

Order is revisable on field-partner signal; the technical skeleton is identical
across increments, so resequencing is cheap.

---

## 4. Increment 1 — Safety constat to corrective action

### 4.1 Business goal

Let a site team record a safety finding and drive it to resolution, even with no
network, replacing the Excel/WhatsApp/paper loop with a traceable digital one.

### 4.2 Personas

Primary — Field QHSE / Chef de chantier
- Device: Android phone or tablet, outdoor, gloves, poor/no network.
- Needs: very fast capture, minimal typing, photo-first, works fully offline.

Secondary — Responsable QHSE / Directeur
- Device: desktop, office, good network.
- Needs: dashboard, validation of corrective actions, traceability, reporting.

### 4.3 Business value

- Replace paper/Excel/WhatsApp findings with a single traceable system.
- Stop corrective actions from being forgotten (assignment + deadline + reminders).
- Improve regulatory compliance and traceability (CI: mandatory site insurance,
  SPS coordination, penal liability — ADR-PROD-001).
- Reduce reporting time.

### 4.4 User journey (basis for Figma, Palier 3)

```
Open app -> choose site -> capture photo -> fill minimal constat form
        -> assign corrective action (assignee + deadline) -> done (works offline)
Later (desktop): review -> validate -> close -> dashboard reflects it
```

### 4.5 Acceptance criteria (high level)

- A constat is created and usable fully offline; survives app restart.
- It links a corrective action with assignee, deadline, status.
- The action can be progressed, verified (conforme / non conforme) and closed.
- All of it syncs reliably on reconnect; conflicts handled, no data loss.
- The safety dashboard reflects synced data, scoped per site.

Detailed field-level rules: `specifications/safety/constat.md` and
`specifications/corrective-actions/corrective-action.md` (created JIT).

### 4.6 Business constraints (domain invariants)

These are stable invariants that feed the domain model directly:

- A constat belongs to exactly one site (no cross-site constat).
- A constat of type `accident` requires a corrective action before it can become
  `resolved`.
- A constat is `resolved` only when all its corrective actions are closed.
- A `critical`-severity constat requires at least one photo (evidence).
- Records are soft-deleted only; never hard-deleted (auditability).

Corrective-action invariants (one owner, evidence before closure, a closed action is
immutable) live in the corrective-action spec (created JIT) to avoid pre-inventing.

### 4.7 UX constraints

- Mobile-first, photo-first, minimal typing, large touch targets (gloves).
- Capture must feel instant; no blocking spinners on save (optimistic UI).
- Offline state must be visible and reassuring (queued, will sync).

### 4.8 Technical constraints / offline strategy

- Local-first writes; automatic sync on reconnect; conflict resolution; optimistic UI.
- Photos captured offline, uploaded on sync (Media context).
- Platform decision (offline web PWA vs Phase 2 Expo app): see ADR-ARCH-003.

### 4.9 Dependencies

Increment 1 depends on:
- Access (authentication, tenancy, RBAC)
- Site (scoping root)
- Offline sync infrastructure (PowerSync)
- Media storage (Supabase Storage)
- Notification scheduling (reminders, critical-severity alerts)

### 4.10 Bounded contexts touched

`Access`, `Site`, `Safety`, `CorrectiveActions`, `Media`, `Notifications`.
Full decomposition: `../architecture/architecture.md`.

### 4.11 Out of increment 1 (non-goals)

Audit templates, advanced search, PDF export, analytics beyond the minimal
dashboard, role administration UI, bulk actions, quality/NC, sensibilisations,
configurable forms, electronic signature. Deferred to later increments.

### 4.12 Definition of done

- Constat created fully offline, survives restart.
- Generates/links a corrective action (assignee, deadline, status).
- Action progressed, verified, closed.
- Reliable sync after reconnect (no loss, conflicts handled).
- Safety dashboard reflects synced data, per site.

### 4.13 Success metrics (targets to validate, not yet proven)

| Metric | Target |
|--------|--------|
| Time to capture a constat | < 60 s |
| Sync latency on reconnect | < 5 s |
| Data loss on offline/conflict | 0 |
| App usable fully offline | 100% |
| Photos eventually synced | >= 95% |

### 4.14 Validation plan

How we will know increment 1 succeeded:
- Pilot: 1 real site, ~10 users, ~2 weeks.
- Method: observe field usage, interview users, collect the success metrics above.
- Decision gate: go / adjust / pivot based on observed metrics and feedback.

### 4.15 Risks

Product risks (mitigation = field testing, user research)
- Field users may reject a tablet/phone-based flow vs paper habit.
- Per-site scoping may not match multi-site users' mental model.

Technical risks (mitigation = integration tests, spikes)
- Offline sync correctness (the core bet).
- Photo / large media upload on poor network.
- Conflict resolution semantics for safety data.
- Low-end Android: battery, storage, performance.

### 4.16 Assumptions (to revisit after first field feedback)

- Field users accept and prefer offline-first.
- The corrective-action lifecycle is the real differentiator.
- Photos are central to a credible constat.
- Per-site scoping is sufficient for increment 1.
- Safety is the fastest market entry (ADR-PROD-001).

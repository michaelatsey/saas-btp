# Specification — Safety Constat

Status: draft (2026-06-28). VOLATILE spec — fields, validations and rules evolve
with the product. Steering doc: ../../scope-phase-1.md.
Field source: Innov_Boost.docx (Securite > Constats). Context: Safety.
Naming: camelCase per conventions/naming.md (UI labels in French).

> A constat is a field safety finding (incident, accident, or dangerous situation).
> It is the entry point of the corrective-action lifecycle in increment 1.

---

## 1. Fields

| Field (camelCase) | UI label (FR) | Type | Required | Notes |
|-------------------|---------------|------|----------|-------|
| id | — | identifier | auto | system-generated |
| type | Type de constat | enum | yes | incident / accident / dangerousSituation |
| severity | Gravite | enum | yes | minor / major / critical (drives dashboard + escalation) |
| observedAt | Date et heure | timestamp | yes | defaults to now; merges date + heure |
| location | Lieu | text | yes | location on site |
| siteId | Chantier | reference | yes | the Site this constat belongs to (scoping) |
| observerName | Observateur | text | yes | who made the finding |
| observerRole | Fonction | text | no | role of the observer |
| trade | Corps de metier | text | no | trade concerned |
| category | Categorie | enum | no | workerSafety / materialEquipment / ... |
| shortDescription | Description breve | text | yes | short summary |
| detailedDescription | Description detaillee | text | no | full circumstances |
| assigneeId | Responsable concerne | reference | no | person/team to handle it |
| immediateAction | Action immediate | text | no | immediate action taken (free text) |
| status | Statut | enum | yes | pending / inProgress / resolved (default pending) |
| notes | Observations | text | no | extra notes |
| photos | Photos | media[] | no | references to Media; captured offline, synced later |

Audit fields (all entities, per naming.md): createdAt, updatedAt, createdBy, deletedAt.

> `severity` is added vs Innov_Boost (which lists it on NC, not constat) because the
> safety dashboard groups by severity and escalation depends on it. Kept (validated).

---

## 2. Validations

- Required (block save, even offline): type, severity, observedAt, location, siteId,
  observerName, shortDescription, status.
- observedAt: not in the future.
- status: starts at pending; moves forward via the corrective-action flow or explicit
  user action; backward moves require a reason/comment.
- photos: optional in the model, but UX strongly prompts at least one (rule 3.2).

---

## 3. Business rules

> Validated invariants (2026-06-28). These feed the domain model. Mirrored as
> domain invariants in ../../scope-phase-1.md (4.6).

- 3.1 A constat MAY generate a corrective action. For type = accident, a corrective
  action is REQUIRED before status can become resolved.
- 3.2 For severity = critical, the UI requires at least one photo and surfaces the
  constat for priority review (notification to Responsable QHSE).
- 3.3 status = resolved is only allowed when all linked corrective actions are closed.
- 3.4 A constat is always scoped to exactly one siteId (no cross-site constat).
- 3.5 Soft delete only (auditability — CLAUDE-SAAS-BTP.md 22). Never hard delete.

---

## 4. Statuses

```
pending -> inProgress -> resolved
```

- pending: recorded, nothing started.
- inProgress: corrective action(s) in progress.
- resolved: all corrective actions closed and verified.

---

## 5. Edge cases

- Created offline then device dies before sync: persist locally, sync later.
- Same constat edited offline on two devices: conflict resolution per sync strategy
  (architecture.md). Last-write-wins is NOT assumed for safety data — to be decided.
- Photo upload fails on poor network: constat syncs; photo retries independently
  (Media context); constat is not blocked.
- Accident type with no corrective action: cannot become resolved (rule 3.1).

---

## 6. Error messages (offline-friendly, non-blocking where possible)

- Missing required field: inline, on the field, at save attempt.
- Save while offline: confirm "saved on device, will sync" — never an error.
- Sync failure: silent retry; surface only if persistent; never lose data.

---

## 7. Links

- Corrective action lifecycle: ../corrective-actions/corrective-action.md (JIT).
- Media (photos/signatures): Media context.
- Notifications (critical review, reminders): Notifications context.

# GitHub Issues Convention

Scope: SaaS BTP repository. Defines how work items (stories, spikes, chores, bugs)
are titled, labeled, tracked, and considered ready/done.

GitHub Issues is the canonical tracker for living work items. The repository holds
durable specs and decisions only (specifications/, decisions/). Stories are never
tracked in repo markdown.

---

## 1. Title format

```
[ctx:<context>] <type>: <imperative description>
```

Rules:
- One primary context per issue. If work spans two contexts, it is usually two issues.
- Description in the imperative, lower-case, no trailing period.
- Keep it short enough to scan in a board column.

Examples:
```
[ctx:safety] story: create constat offline
[ctx:corrective-actions] story: assign corrective action with deadline
[ctx:safety] spike: powersync-web offline + camera on target Android
[ctx:notifications] chore: define notification payload contract
[ctx:safety] bug: severity not persisted on offline constat sync
```

---

## 2. Labels

Only the six Increment 1 bounded contexts get a `ctx:*` label now (architecture.md
section 7). The remaining contexts (Quality, Workforce, Stock, Reporting) get their
label just-in-time, when they enter active work - never up front.

### Context (`ctx:*`)

| Label | Bounded context | Role in Increment 1 |
|-------|-----------------|---------------------|
| `ctx:access` | Authentication, tenancy, RBAC | Foundational |
| `ctx:site` | Site (chantier) scoping | Foundational |
| `ctx:safety` | Safety constats | Core |
| `ctx:corrective-actions` | Corrective action lifecycle | Differentiator |
| `ctx:media` | Photos / attachments | Cross-cutting |
| `ctx:notifications` | Notifications | Cross-cutting |

### Type (`type:*`)

| Label | Meaning |
|-------|---------|
| `type:story` | User-facing vertical slice delivering business value |
| `type:spike` | Time-boxed technical investigation, no production code committed as outcome |
| `type:chore` | Internal work (tooling, CI, refactor, docs) with no direct user value |
| `type:bug` | Defect against expected behavior |

### Priority (`prio:*`)

| Label | Meaning |
|-------|---------|
| `prio:critical` | Blocks the increment or a safety-critical path |
| `prio:high` | Needed for the increment, next in line |
| `prio:normal` | Planned, not urgent |
| `prio:low` | Nice to have, deferrable |

### Special

| Label | Meaning |
|-------|---------|
| `offline` | Touches offline capture, local-first writes, or synchronization (the product differentiator). Applied on top of a `type:*` label. |

---

## 3. Definition of Ready (DoR)

An issue may be pulled into active work only when all of the following hold:

1. Business value is stated in one sentence (why this matters to the field user).
2. Acceptance criteria are written and testable.
3. Primary context is identified (`ctx:*` label set).
4. Known dependencies are linked (blocking issues, specs, ADRs).
5. Scope is small enough to fit within a single increment slice.
6. For anything touching sync: the offline behavior is described (what happens
   offline, what happens on reconnect).

---

## 4. Definition of Done (DoD)

An issue is closed only when all of the following hold:

1. Code is merged into `dev` (feature branch -> PR -> dev).
2. Every acceptance criterion is verified.
3. Tests exist as appropriate to the type (unit / integration / architecture).
4. If the work produced a non-trivial decision, an ADR is recorded per
   decisions-index.md: correct domain prefix (ADR-PROD / ADR-ARCH / ADR-ORG),
   zero-padded per-domain numbering, the standard template, and the ADR is added
   to the index. Existing ADRs are superseded, never edited.
5. Domain invariants are respected. Examples for Safety:
   - an accident-type constat requires a corrective action before it can be resolved;
   - a critical-severity constat requires at least one photo, enforced at the capture
     layer (UX/application) - photos remain optional in the domain model (see constat.md);
   - a constat is resolved only when all its corrective actions are closed.
6. No out-of-scope files were changed.

---

## 5. Milestones = Increments

Each milestone maps one-to-one to a product increment.

| Milestone | Increment |
|-----------|-----------|
| `Increment 1 - Safety -> Corrective action` | Safety constat -> Corrective action -> dashboard (ADR-PROD-001), offline PWA (ADR-ARCH-003) |

Future increments get their own milestone when scoped. An issue belongs to exactly
one milestone.

---

## 6. Relationship to ADRs

Issues track work; ADRs track decisions. When an issue surfaces a non-trivial
decision, it is recorded as an ADR under decisions-index.md governance and linked
from the issue. The issue is not a substitute for the ADR, and the ADR is not a
substitute for the issue.

---

## 7. Issue templates

Structured creation is enforced through issue templates in
`.github/ISSUE_TEMPLATE/`. The story template captures: context, business value,
acceptance criteria, offline behavior, dependencies, and DoD checklist.

See `.github/ISSUE_TEMPLATE/story.yml`.

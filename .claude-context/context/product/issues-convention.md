# GitHub Issues Convention

Scope: SaaS BTP repository. Defines how work items (stories, spikes, chores, bugs)
are titled, labeled, tracked, and considered ready/done.

GitHub Issues is the canonical tracker for living work items. The repository holds
durable specs, designs, and decisions only — business-process artifacts under
`docs/business-processes/`, decisions under `.claude-context/context/decisions/`
(see "Durable documentation layout"). Stories are never tracked in repo markdown.

---

## 1. Title format

Plain imperative, no prefix. Labels are the single source for context, type and
priority, so the title repeats none of them. Lower-case after the first word, no
trailing period, short enough to scan in a board column. One primary context per
issue: work spanning two contexts is usually two issues.

Examples:

- Create constat offline
- Enable GitHub security features
- Severity not persisted on offline constat sync

---

## 2. Labels

Only the six Increment 1 bounded contexts get a product `ctx:*` label now
(architecture.md section 7), plus `ctx:platform` for work on the repository
itself. The remaining product contexts (Quality, Workforce, Stock, Reporting) get
their label just-in-time, when they enter active work - never up front.

### Context (`ctx:*`)

| Label | Bounded context | Role in Increment 1 |
|-------|-----------------|---------------------|
| `ctx:access` | Authentication, tenancy, RBAC | Foundational |
| `ctx:site` | Site (chantier) scoping | Foundational |
| `ctx:safety` | Safety constats | Core |
| `ctx:corrective-actions` | Corrective action lifecycle | Differentiator |
| `ctx:media` | Photos / attachments | Cross-cutting |
| `ctx:notifications` | Notifications | Cross-cutting |
| `ctx:platform` | Repository, CI, tooling | No product context |

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

This is a personal-account repository, so organization-level issue types are
unavailable: the `type:*` labels carry that role.

---

## 3. Issue body structure

Every issue body follows the same six headings:

- Goal — what this achieves, in one or two sentences.
- Context — what is true today that makes it necessary; link the inventory, ADR or
  issue it comes from.
- Scope — what this issue covers.
- Out of scope — what it deliberately does not, so the scope cannot widen silently.
- Done when — invariants, stated as verifiable outcomes, never as steps.
- Proof — the method that will demonstrate those invariants.

---

## 4. Hierarchy and dependencies

A parent issue carries a goal; its sub-issues carry the work
(`gh issue create --parent`, or `gh issue edit --parent` afterwards). Parent
progress is visible through `subIssuesSummary`.

A dependency is not a hierarchy. Use blocked-by when one issue must complete before
another can start, and a parent/sub-issue link when one contains the other.

Dependencies are native: `gh issue create --blocked-by`,
`gh issue edit --add-blocked-by`, and their `--blocking` counterparts. Read them
with `gh api repos/OWNER/REPO/issues/<N>/dependencies/blocked_by`. A textual
`Blocked by: #N` in the body is a fallback, not the convention.

---

## 5. Branch names

`<type>/<kebab-case-description>`, type from Conventional Commits, with an optional
scope segment (for example `feature/access/bp-001-implementation`). Short and
descriptive, no issue number.

---

## 6. Definition of Ready (DoR)

An issue may be pulled into active work only when all of the following hold:

1. Business value is stated in one sentence (why this matters to the field user).
2. Acceptance criteria are written and testable.
3. Primary context is identified (`ctx:*` label set).
4. Known dependencies are linked (blocking issues, specs, ADRs). For work deriving
   from a Business Process, the BP unit under `docs/business-processes/BP-XXX/` is
   linked (see "Durable documentation layout").
5. Scope is small enough to fit within a single increment slice.
6. For anything touching sync: the offline behavior is described (what happens
   offline, what happens on reconnect).

---

## 7. Definition of Done (DoD)

An issue is closed only when all of the following hold:

1. Code is merged into `main` through a pull request (ADR-ORG-002), squash-merged,
   with one closing keyword per issue: `Closes #1` then `Closes #2`, never
   `Closes #1, #2`. An issue that produces no code is closed by a proof comment
   matching its Proof section, then closed with `--reason completed`.
2. Every acceptance criterion is verified.
3. Tests exist as appropriate to the type (unit / integration / architecture).
4. If the work produced a non-trivial decision, an ADR is recorded per
   decisions-index.md: correct domain prefix (ADR-PROD / ADR-ARCH / ADR-ORG),
   zero-padded per-domain numbering, the standard template, and the ADR is added
   to the index. Existing ADRs are superseded, never edited.
5. Domain invariants are respected, as defined by the bounded context's own model —
   for Safety, `.claude-context/context/architecture/safety-domain-model.md`. This
   convention does not restate them: a second copy is a second source of truth.
6. No out-of-scope files were changed.

---

## 8. Milestones = Increments

Each milestone maps one-to-one to a product increment.

| Milestone | Increment |
|-----------|-----------|
| `Increment 1 - Safety -> Corrective action` | Safety constat -> Corrective action -> dashboard (ADR-PROD-001), offline PWA (ADR-ARCH-003) |

Future increments get their own milestone when scoped. A milestone carries an
increment goal, so an issue belongs to at most one. Roadmap goals are carried by
the issue hierarchy instead (see "Hierarchy and dependencies"), and a roadmap
issue has no milestone.

---

## 9. Relationship to ADRs

Issues track work; ADRs track decisions. When an issue surfaces a non-trivial
decision, it is recorded as an ADR under decisions-index.md governance and linked
from the issue. The issue is not a substitute for the ADR, and the ADR is not a
substitute for the issue.

---

## 10. Issue templates

Structured creation is enforced through the templates in `.github/ISSUE_TEMPLATE/`,
with `config.yml` setting `blank_issues_enabled: false` so every issue routes
through a form.

Those templates predate this rewrite: their `ctx:*` dropdown omits `ctx:platform`,
and their structure does not match "Issue body structure". Aligning them — and
deciding whether a story template should follow the same structure as a tooling
issue — is tracked in #97.

---

## 11. Project board #9

Board #9 uses built-in workflows: auto-add from this repository, and closed moves an
item to Done. The former manual two-step add is obsolete.

---

## 12. Durable documentation layout

Work is tracked in Issues; durable specs, designs, and decisions live in the
repository. This section is authoritative for where those durable artifacts live;
CLAUDE.md carries the orientation summary.

A Business Process is the autonomous documentary unit. Anyone (human or AI) must be
able to pick up a BP and derive an implementation without conversation history.

```
docs/business-processes/BP-XXX/
  BP-XXX-specification.md          WHY: business intent, scope, actors, initial business rules
  BP-XXX-design-package.md         WHAT (closed business model): process, domain/MCD, invariants, lifecycle, boundaries
  BP-XXX-implementation-design.md  SOFTWARE HOW (durable): architecture, persistence, API, security/sync, technical constraints
```

Derivation chain — consigned vs derived:

```
Specification         (docs/business-processes/BP-XXX/)        consigned
Design Package        (docs/business-processes/BP-XXX/)        consigned — business model, closed
Implementation Design (docs/business-processes/BP-XXX/)        consigned — software contract, stable
Implementation Plan   (per tool: dev, Claude Code, other AI)   DERIVED — NOT consigned
Code
```

Rules:
- The Design Package holds no realization decisions. The Implementation Design is not
  an Implementation Plan in disguise: no task breakdown, no file-creation order, no
  tool-specific prompt. Anything resembling an execution step belongs to the
  (non-consigned) Implementation Plan.
- The three consigned documents must let any developer or AI derive a plan and a
  faithful implementation without the conversation history.
- Decisions (ADRs) are governed separately (see "Definition of Done" and
  "Relationship to ADRs", and decisions-index.md) and live at
  `.claude-context/context/decisions/`, not in the BP folder.
- `.claude-context/` holds agent context, conventions, architecture rules, decisions,
  and sessions — not produced product artifacts. Durable product specs/designs live
  under `docs/`.

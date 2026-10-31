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
| `ctx:access` | People, organizations, workspaces and their relations (ADR-ARCH-031) | Foundational |
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

1. Goal and Context are written: what the work achieves, and what makes it necessary.
2. "Done when" and "Proof" are written, and "Done when" is verifiable.
3. Primary context is identified (`ctx:*` label set).
4. Known dependencies are linked (blocking issues, specs, ADRs). For work deriving
   from a Business Process, the BP unit under `docs/business-processes/BP-XXX-<slug>/`
   is linked (see "Durable documentation layout").
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
2. Every outcome of "Done when" is verified by the method of "Proof".
3. Tests exist as appropriate to the type (unit / integration / architecture).
4. If the work produced a non-trivial decision, an ADR is recorded per
   decisions-index.md: correct domain prefix (ADR-PROD / ADR-ARCH / ADR-ORG),
   zero-padded per-domain numbering, the standard template, and the ADR is added
   to the index. Existing ADRs are superseded, never edited.
5. Domain invariants are respected, as defined by the owning bounded context's model
   and Business Process documents. This convention does not restate them: a second
   copy is a second source of truth.
6. No out-of-scope files were changed.

---

## 8. Milestones = Increments

Each milestone maps one-to-one to a product increment, as sequenced in
`docs/product/scope-phase-1.md`, and is named `Increment <N> - <goal>` — for
example `Increment 1 - Safety -> Corrective action`.

A milestone is created when its increment is scoped and its first issue exists,
never up front: an empty milestone states progress that does not exist. A
milestone carries an increment goal, so an issue belongs to at most one. Roadmap
goals are carried by the issue hierarchy instead (see "Hierarchy and
dependencies"), and a roadmap issue has no milestone.

---

## 9. Relationship to ADRs

Issues track work; ADRs track decisions. When an issue surfaces a non-trivial
decision, it is recorded as an ADR under decisions-index.md governance and linked
from the issue. The issue is not a substitute for the ADR, and the ADR is not a
substitute for the issue.

---

## 10. Issue templates

Four issue forms live in `.github/ISSUE_TEMPLATE/`: story, spike, chore and bug. A
form guides, it does not enforce:

- A form applies its `type:*` label and pre-fills no title. Its body lays out the
  six headings of "Issue body structure", then the one field its type needs, if
  any: offline behavior for a story, steps to reproduce for a bug, a time box for a
  spike. It ends with a `Primary context` dropdown.
- `config.yml` sets `blank_issues_enabled: false`, which hides the blank issue for
  the Read and Triage roles only. The maintainer still sees it, labeled
  "Maintainers only", and an issue created through `gh` or the API goes through no
  form.
- A dropdown answer cannot apply a label. The `Primary context` answer is recorded
  in the body only; the `ctx:*` and `prio:*` labels are set by hand after creation,
  whatever the form shows.

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
docs/
  domain/
    <context>.md            concepts and invariants shared by several processes
  business-processes/
    BP-XXX-<slug>/
      process.md            the business process
      acceptance.md         the expected behaviour, as identified scenarios
      contract.md           the software contract of this process
```

| Artifact | Holds | Validated by |
|---|---|---|
| `process.md` | Intent, actor, trigger, boundary with the owner of each excluded flow, precondition, flow, task contracts (trigger, performer, preconditions, inputs, rules, outputs, errors, event), states, the rules and decisions local to this process, open questions. Business language only. | The maintainer |
| `acceptance.md` | Scenarios `BPXXX-AC-nn` in Given / When / Then, each stating an observable outcome and no mechanism, covering only behaviour this process produces. | The maintainer |
| `contract.md` | What this process introduces or changes in software: the owning bounded context of each element, interfaces, transactional and security guarantees, failure, retry and concurrency, the guarantor of every invariant and scenario, the verification commands. It references conventions and ADRs by path and section, never copies them. | Agreement of the two reviewers (ADR-ORG-004) |
| `docs/domain/<context>.md` | Concepts and invariants `INV-<CONTEXT>-nn` that several processes create, read or change. Created when a second process consumes them, or when the context map makes the context foundational. | The maintainer |

Every artifact starts with a header: `status` (`draft` or `validated`),
`validated_by`, `validated_on`, and `applies`, the list of its normative references
(invariants, ADRs, conventions).

Rules:
- One authoritative source per rule. A rule used by one process lives in its
  `process.md`; a concept or invariant shared by several lives in `docs/domain/`; a
  cross-cutting technical decision is an ADR; a change to a convention is made in that
  convention, in the same pull request. Other artifacts reference a rule by its
  identifier; they never restate it.
- A normative reference points to an active decision: an ADR listed as accepted in
  decisions-index.md and not superseded. A reference kept for history is marked as such
  and is not listed in `applies`.
- Every scenario identifier appears in the name of at least one test that covers it,
  at the start of the name, with `-` written `_` where the language forbids it
  (`BP001_AC_13_...`). An invariant identifier is carried the same way by the tests
  that prove it. A test proves the scenario in the conditions it runs; the guarantor named in
  `contract.md` is what holds it in production.
- A tool or agent that consumes these artifacts stops and reports, rather than choosing,
  when an artifact it needs is not `validated`, a reference in `applies` is missing or
  no longer active, the contract contradicts the process, a domain file or an ADR, or a
  scenario cannot be met by the contract.
- None of these artifacts is an implementation plan: no task breakdown, no
  file-creation order, no tool-specific prompt. Plans and work breakdown live in issues.
- Decisions (ADRs) are governed separately (see "Definition of Done" and
  "Relationship to ADRs", and decisions-index.md) and live at
  `.claude-context/context/decisions/`, not in the BP folder.
- `.claude-context/` holds agent context, conventions, architecture rules, decisions,
  and sessions, not produced product artifacts. Durable product specs and designs live
  under `docs/`.

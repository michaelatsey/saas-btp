---
name: deliver-bp
description: Deliver one Business Process through its issues - derive the parent issue and its sub-issues from the BP's validated documents, have the breakdown reviewed by both reviewers, create the issues on agreement, then name the next sub-issue to run through /fix-issue, one per run. Never runs /fix-issue, never merges.
disable-model-invocation: true
---

# Deliver one Business Process

Arguments: `$ARGUMENTS`, that is `BP-XXX`, optionally followed by `--dry-run`.

A run does one of two things, decided by what GitHub holds (CLAUDE.md, Working rules: state
lives in GitHub Issues):

- the BP has no parent issue yet: derive its breakdown, have it reviewed, create the issues,
  then name the first sub-issue to run;
- the BP has its parent issue: name the next sub-issue to run.

Either way the run ends by printing one `next:` line, or by stopping for the maintainer, and
then stops. The maintainer runs `/fix-issue N`, merges, and runs `/deliver-bp BP-XXX` again.
This skill never runs `/fix-issue` (it cannot: `disable-model-invocation: true`), never
merges and never closes an issue.

`D=.claude/skills/deliver-bp/deliver.sh` holds the mechanical checks; `deliver.test.sh`
tests them. Run both from the repository root.

## Scratch directory

Once per run, use a scratch directory outside the repository: the session's scratchpad if
one is given, otherwise `mktemp -d`. Name it in the conversation. The breakdown, the issue
graph, every review input and output and the ledger live there, never in a repository file:
the implementation plan is never consigned (`issues-convention.md` section 12). `deliver.sh`
and `review.sh` refuse a path inside the repository.

## 1. Locate the BP

`$D title BP-XXX` prints the parent issue's title and fails unless exactly one
`docs/business-processes/BP-XXX-*/` exists. Stop if it fails.

## 2. Read the BP's issues

`$D graph BP-XXX <scratch>/bp-issues.json`. It finds the parent issue by its exact title,
from the issues list rather than the search index, and writes the parent, its sub-issues and
each sub-issue's blockers.

- `graph: no issue titled ...` (`"parent": null`): go to step 3.
- A parent: go to step 9.
- Exit 2 (two issues with the title, a listing at its limit, a failed read): stop.

## 3. Read the BP and what it cites

Read `process.md`, `acceptance.md` and `contract.md`, the `docs/domain/` files they cite,
and every ADR and convention in their `applies` lists: the ADR itself, not its title in the
index. This step runs only when the breakdown is to be derived: a run that only names the
next sub-issue reads GitHub, not the BP.

Stop and report, never choose, on any condition of `issues-convention.md` section 12: an
artifact that is not `validated`; a reference in `applies` that is missing or no longer
active in `decisions-index.md`; the contract contradicting the process, a domain file or an
ADR; a scenario the contract cannot meet. Changing a BP document is not this skill's work.

## 4. Derive the breakdown

Write, in `<scratch>/breakdown/`, one Markdown body per issue and `manifest.json`:

```json
{
  "bp": "BP-XXX",
  "parent": { "title": "<what step 1 printed>", "labels": ["ctx:...", "type:...", "prio:..."], "body": "parent.md" },
  "sub_issues": [
    { "key": "kebab-case-key", "title": "...", "labels": ["..."], "body": "kebab-case-key.md",
      "blocked_by": ["other-key", 184], "covers": ["BPXXX-AC-01", "INV-ACCESS-01", "contract.md §5"] }
  ],
  "deferred": [ { "id": "BPXXX-AC-02", "reason": "..." } ]
}
```

Rules:

- Every issue, the parent included, follows `issues-convention.md`: a plain imperative title
  with no prefix and no trailing period (section 1); exactly one `ctx:*`, one `type:*` and
  one `prio:*` label, plus `offline` when it touches sync (section 2); exactly the six `##`
  headings of section 3, in their order, each non-empty, and no other `##` heading; when it
  carries `offline`, a seventh, `## Offline behavior`, right after `## Proof`, saying what
  happens offline and on reconnect (DoR 6).
- Every body links the BP folder, `docs/business-processes/BP-XXX-<slug>/`, and the
  decisions its work rests on (DoR 4). Paths are repository-relative, never absolute.
- A sub-issue is one slice of work for one `/fix-issue` run, merged by one pull request
  (DoR 5). Its "Done when" states verifiable outcomes, never steps; its "Proof" names the
  tests, by the scenario and invariant identifiers they carry (section 12), and the commands
  of the contract's verification section.
- Dependencies are `blocked_by`: the key of another sub-issue, or the number of an existing
  issue (an open decision, a deployment). A dependency is never written as body text only
  (section 4). A sub-issue a pending decision blocks is blocked by that decision's issue.
- Coverage. Every scenario of `acceptance.md`, every invariant in the `applies` of
  `contract.md`, and every numbered section of `contract.md`, written `contract.md §N`, is
  either covered, that is listed in the `covers` of a sub-issue and named in its body, or
  deferred, that is listed in `deferred` with its reason and stated in the parent's "Out of
  scope". An identifier covered only in part (a client part the contract leaves out of the
  pilot, a proof pending a decision) is covered, and its deferred part is stated in the
  parent's "Out of scope".
- Work that a decision the BP cites prescribes, and that the repository does not hold yet
  (a library to copy, a change a decision requires in it, a test project), is a sub-issue of
  its own, or an existing issue named as a blocker. It is never left implicit in another
  issue's Context. When the decision asks for one pull request per change, each change is
  its own sub-issue. Check what the repository holds before deciding that something exists.
- Each sub-issue's "Proof" carries, for every identifier it covers, the proof level
  `contract.md`'s guarantor table sets for it (Domain, Features, Integration, Host); an
  identifier proven at two levels is covered at each, by one sub-issue or several.
- The parent carries the BP's goal; its "Done when" is that every sub-issue is closed and the
  BP's scenarios in scope are proven. Its Scope lists no task: the sub-issues are the work.
- Priorities: `prio:critical` blocks the increment; otherwise the BP's issue priority, lower
  for work no other sub-issue needs.
- A question only the client can answer is never guessed: stop and ask (ADR-ORG-004
  section 4).

## 5. Check the breakdown

`$D check <scratch>/breakdown BP-XXX` checks the mechanical part of the Definition of Ready
and the coverage of step 4: headings, empty sections, labels, the BP link, titles, keys,
`blocked_by` keys and cycles, covered or deferred identifiers, the parent title, and every
label of every issue against the repository's labels, read from GitHub. Exit 8: fix the
breakdown and run it again. Exit 2: the labels could not be read, or the list was empty;
`check` never passes without them; stop. A clean check prints the creation order.

The rest of the Definition of Ready is judgment: "Done when" verifiable (DoR 2), the slice
small enough (DoR 5), the offline behavior described (DoR 6), every known dependency linked
(DoR 4). Check each issue against items 1 to 6 yourself, and write the table, one row per
issue and one column per item, to `<scratch>/breakdown/dor.md`.

## 6. Dry run

With `--dry-run`: print the breakdown directory, the `check` output and the DoR table, and
stop. No review, no GitHub write.

## 7. Have the breakdown reviewed

Apply the "Agreement review" section of `.claude/skills/fix-issue/SKILL.md` as written:
scratch directory, cycles, the two reviewers, `review.sh` and its exit codes, the ledger,
deciding, and stopping for the maintainer. Only these points differ:

- The step is named `breakdown` (`breakdown-c1-claude.json`, `breakdown-c1-codex.json`, and
  so on).
- Each reviewer's input is, in this order: the task line "Review the issue breakdown below
  against BP-XXX's documents, the issues convention and the decisions index: coverage of the
  contract (contract.md), granularity, missing dependencies, and the Definition of Ready of
  every issue."; the repository-relative paths of the three BP documents; `manifest.json`
  and every body, verbatim, each under its file name; the DoR table; the latest output of
  `$D check`; then, from the second cycle on, that reviewer's own earlier blocking findings,
  each with what the author changed for it. There is no issue body: no issue exists yet.
- A revised breakdown goes through step 5 again before the second cycle, whose input carries
  the new `check` output and DoR table.

Agreement, as that section defines it, lets the run go on to step 8. Anything else stops it.

## 8. Create the issues

`$D create <scratch>/breakdown BP-XXX`. It runs `check` again, then, before writing anything,
refuses (exit 2) if an issue already has the parent's title, if a label does not exist, or if
an external blocker is not an issue of the repository. It then creates the parent, then each
sub-issue in the creation order, under the parent and blocked by its planned blockers;
records every number in `<scratch>/breakdown/created.json`; and reads GitHub back to verify
that the parent holds exactly the planned sub-issues with exactly their planned blockers
(ADR-ORG-004 section 3: in auto mode this runs without a permission prompt).

- Exit 0: report the numbers it printed, then go to step 9 with
  `$D graph BP-XXX <scratch>/bp-issues.json --parent N`, N the parent number in
  `created.json`, never a new lookup by title.
- Exit 9: some issues exist and the breakdown is not as planned. Stop, and report what it
  printed: the maintainer finishes or removes the partial creation before the next run.
- Exit 2: nothing was created; stop.

## 9. Name the next sub-issue

`$D next <scratch>/bp-issues.json` reads the file only. Among the open sub-issues with no
open blocker, in any repository, it names the first by priority (`prio:critical`, `high`,
`normal`, `low`), then by the lowest issue number:

- `next: /fix-issue N`;
- `next: none, no open sub-issue of #P is unblocked`, followed by each open sub-issue and its
  open blockers;
- `next: none, every sub-issue of #P is closed`: the BP's work is done; closing the parent is
  the maintainer's.

Exit 8 (`stop:`): no parent, a parent with no sub-issue, or an unblocked sub-issue without
exactly one priority label. Its labels are never guessed; stop for the maintainer.

Print the line and stop.

## When a run stops

A run stops for the maintainer on every escalation point of ADR-ORG-004 section 4 and of the
agreement review: a step 3 condition, a question only the client can answer, a disputed or
remaining blocking finding, a reviewer that is unavailable or did not come back, a secret in
an input, a write during a review, a validator failure, a partial creation, or a `stop:`
line. State the reason, the ledger if a review ran, and the scratch directory, and wait.

## Report

In the conversation: the step the run ended at; the issues created, with their numbers, if
any; for each review cycle, each reviewer's verdict and every finding with what was done
with it; for each second-reviewer run, what `review.sh codex` printed (Codex version, model,
`tokens used`, authentication, copy, permissions, `workdir:`, `sandbox:`); the `guard:`
lines; and the `next:` line.

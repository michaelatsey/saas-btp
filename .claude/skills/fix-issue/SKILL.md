---
name: fix-issue
description: Take one GitHub issue from reading to pull request - branch, change, tests, verification, agreement review with a second reviewer, commit, push, pull request. Merging stays human.
disable-model-invocation: true
---

# Fix one issue

Work on GitHub issue $ARGUMENTS, from reading it to opening its pull request.

One issue, one branch, one pull request. Anything you find that the issue does not
list is a separate issue: report it, do not fix it here.

## 1. Read the issue

```bash
gh issue view $ARGUMENTS
```

Read its Goal, Scope, Out of scope, Done when and Proof. If a point is ambiguous, or
"Done when" cannot be verified as written, ask before changing a file.

## 2. Plan the change, unless it is trivial

If the diff can be described in one sentence, skip this step and say so.

Otherwise - the approach is uncertain, the change spans several files, or the code is
unfamiliar - write a short plan in the conversation, never in a repository file: the
files to touch, the order of work, the tests, and the decisions in
`.claude-context/context/decisions/decisions-index.md` that bear on the change.

Then run the agreement review (below) on the plan, against the issue and the decisions
index. The plan reaches the reviewers through a scratch file outside the repository.
Create the branch only when the review ends in agreement; otherwise stop for the
maintainer.

## 3. Create the branch

Start from an up-to-date `main` with a clean working tree. Create the branch before
changing any file, named as the root `CLAUDE.md` prescribes.

## 4. Change only what the issue lists

Check `.claude-context/context/decisions/decisions-index.md` for decisions that bear on
the change. Never settle an open decision as a side effect: if the change appears to
require one, stop and say so.

## 5. Cover the change with tests

For a change in behaviour, write the test first and show it failing against the
current code; a test that passes before the change tests something else. For a change
with no behaviour to test (documentation, configuration), name the check that stands
in for a test.

## 6. Verify, and show the output

List the applications the change touches. For each one, read its `CLAUDE.md`
(`apps/<name>/CLAUDE.md`) and run the build and test commands it gives. If an
application has no `CLAUDE.md`, stop and say so. For a change outside `apps/`, run the
checks named in the issue's Proof.

Show the output; do not assert that it passed. If anything fails, fix it and run
again before going further. Keep the output in the scratch directory: step 7 sends it
to the reviewers.

## 7. Review the diff, for correctness and against the issue

Two reviews, in this order. Neither replaces the other: the first looks for bugs in the
diff, the second checks it against the issue.

For correctness: run the built-in `/code-review` with no target, so that it covers the
uncommitted changes, new files included. It runs in the background by default, so its
findings arrive later: wait for its findings before going further, and never commit
before they are in. Fix those that affect correctness; report the others without
fixing them. `/code-review` is not part of the agreement rule. If it led to a change,
run step 6 again.

Against the issue: mark every new file with `git add -N <file>`, so that `git diff main`
contains it. Then run the agreement review (below) on `git diff main` with the latest
output of step 6, focused on the issue's "Done when": every item addressed, each change
in behaviour covered by a test, and nothing changed outside the issue's scope.

A review that fails or does not come back is not a clean review: stop and say so.

Then run step 6 again.

## 8. Commit

Show `git diff --stat` and the commit message before committing. One commit, in the
format the root `CLAUDE.md` prescribes. The body explains the decision, not the diff:
what was wrong or missing, why this change rather than the alternative, and any
behaviour that changed as a side effect.

## 9. Push and open the pull request

Push the branch, then open the pull request into `main`. Its title is the commit
subject. Its body follows `.github/pull_request_template.md`; "Verification" states
only what was run or observed in this session, and carries the report of step 10, so
that the maintainer reviews without re-running anything; "Review" keeps its box
unticked, since it is the maintainer's to tick once they have read the full diff;
"Closing" ends with `Closes #$ARGUMENTS`.

In auto mode, commit, push and pull request creation run without a permission prompt;
in manual mode they still prompt (ADR-ORG-004, section 3). A prompt is answered, never
routed around: wait for the answer, never look for another way to run the command.

## 10. Stop

Do not merge. Report, in the pull request body and in the conversation:

- the files changed, and why;
- the tests added, and what each one would have caught;
- the verification output;
- each reviewer's verdict in each cycle of steps 2 and 7, and whether a first cycle
  blocked;
- every finding of the three reviews (the `reviewer` subagent, `/code-review`, the
  second reviewer) and what was done with each; for each blocking finding, its
  identifier, its origin or origins, its basis, the author's change and its status per
  cycle;
- for each second-reviewer run, the Codex version, the model, the `tokens used` and the
  authentication it used, as `review.sh codex` printed them;
- the `pnpm` and `ajv-cli` versions used;
- the write-guard records (`guard:` lines) of every review;
- anything found and deliberately not fixed;
- the pull request URL (conversation only);
- what the issue's Proof asks for that only the maintainer can observe.

## Agreement review (steps 2 and 7)

A step that needs approval advances without the maintainer when both reviewers keep no
blocking finding, within two cycles (ADR-ORG-004, section 2). What makes a finding
blocking is defined there and in `AGENTS.md`; the output contract is
`.claude-context/context/ai-os/review-output.schema.json`.

The reviewers:

- the `reviewer` project subagent (`.claude/agents/reviewer.md`, limited to `Read`,
  `Glob` and `Grep`);
- the second reviewer, Codex, run only through `review.sh codex`.

`review.sh` (next to this file) holds the mechanical checks, and `review.test.sh` tests
them; below, `R=.claude/skills/fix-issue/review.sh`, run from the repository root.

### Scratch directory

Once per run, use a scratch directory outside the repository: the session's scratchpad
if one is given, otherwise `mktemp -d`. Name it in the conversation. Every input,
output and record of the review lives there; `review.sh` refuses a path inside the
repository.

### One cycle

A cycle is one evaluation by each reviewer. A step has at most two cycles.

1. Build one input file per reviewer: a first line stating the task ("Review the plan
   below against issue #N and the decisions index." in step 2; "Review the diff below
   against issue #N, focused on its Done when: every item addressed, each change in
   behaviour covered by a test, nothing changed outside its scope." in step 7); then
   the issue body
   (`gh issue view N --json body -q .body`); then the plan (step 2) or `git diff main`
   (step 7); then, in step 7, the latest output of step 6; then, from the second cycle
   on, that reviewer's own earlier blocking findings, under their identifiers, each with
   what the author changed for it. Within a cycle, neither input holds the other
   reviewer's output.
2. The `reviewer` subagent:
   - `$R guard <dir>/<step>-c<n>-claude.before`;
   - delegate to the `reviewer` subagent, its prompt being the input file's content,
     verbatim;
   - write its final message, verbatim, to `<dir>/<step>-c<n>-claude.json`;
   - `$R guard <dir>/<step>-c<n>-claude.after`, then
     `$R compare <dir>/<step>-c<n>-claude.before <dir>/<step>-c<n>-claude.after`;
   - `$R check <dir>/<step>-c<n>-claude.json`.
3. The second reviewer:
   `$R codex <input> <dir>/<step>-c<n>-codex.json <dir>/<step>-c<n>-codex`. It scans
   the input for secret-shaped values before anything reaches Codex, records the
   authentication, guards the working tree around the run, runs
   `codex exec --sandbox read-only --ephemeral --ignore-user-config -C <repo>
   --output-schema .claude-context/context/ai-os/review-output.schema.json -o <output> -
   < <input>`, and checks the output.
4. Read the exit codes:
   - `3` (a secret-shaped value in the input; it was not sent), `4` (the working tree
     changed during a review), `6` (the second reviewer is unavailable: non-zero exit
     or no output, an exhausted usage limit or a default model the account rejects
     included), `7` (the validator itself could not run: `pnpm` or `ajv-cli` failed,
     which is not the reviewer's output): stop for the maintainer. Never retry with
     another tool or model, never pass `-m`, and never edit the input to get past the
     scan.
   - `5` from `check` (the output is not JSON, fails the schema, or has a verdict that
     disagrees with `blocking_findings`): one blocking finding of that reviewer in that
     cycle. The cycle goes on, and the finding is decided like any other (Deciding,
     below).
   - Any other exit code, `2` included (a path inside the repository, a usage error, a
     failing command): the review did not run; stop for the maintainer.
   - The subagent returns nothing: the review did not come back; stop and say so.

### The ledger

Keep a ledger in the scratch directory, per step. Number every blocking finding F1, F2
and so on, in the order first seen. A point raised by both reviewers gets one
identifier, records both origins, is revised once, and goes back to each reviewer as
its own earlier finding. For each blocking finding record its identifier, its origin or
origins, its basis, the author's change and its status per cycle. Record every
non-blocking finding and what was done with it.

### Deciding

- Neither reviewer keeps a blocking finding: agreement, the step continues.
- A blocking finding the author disputes: a disagreement, stop for the maintainer. The
  author never dismisses a blocking finding.
- A blocking finding after the first cycle: revise the plan or the diff for each one,
  and record the change in the ledger. In step 7, run step 6 again, so that the second
  cycle carries the current verification output. Then run the second cycle with both
  reviewers, including one that approved the first: a revised plan or diff is a new
  artifact.
- An invalid or inconsistent output (exit `5`) leaves nothing in the plan or diff to
  revise: record the author's change as "none: invalid output", and give that reviewer
  the `check` error as its earlier finding in the second cycle.
- A blocking finding after the second cycle: stop for the maintainer.

A blocking finding is resolved only when the same reviewer, reviewing the revised plan
or diff, keeps no blocking finding.

To stop for the maintainer: state the reason, the ledger and the scratch directory, and
wait.

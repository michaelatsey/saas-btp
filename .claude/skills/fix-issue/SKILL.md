---
name: fix-issue
description: Take one GitHub issue from reading to pull request - branch, change, tests, verification, subagent review, commit, push, pull request. Merging stays human.
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
unfamiliar - write a short plan in the conversation, never in a file: the files to
touch, the order of work, the tests, and the decisions in
`.claude-context/context/decisions/decisions-index.md` that bear on the change. Then
wait for the maintainer's approval before creating the branch.

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
again before going further.

## 7. Review the diff, against the issue and for correctness

Two reviews, in this order. Neither replaces the other: the first checks the diff
against the issue, the second looks for bugs in it.

Against the issue: give a subagent the issue text and the diff. Ask it to check that
every "Done when" item is addressed, that each change in behaviour has a test, and
that nothing outside the issue's scope changed. Act only on gaps that affect
correctness or the stated scope.

For correctness: run the built-in `/code-review` with no target, so that it covers the
uncommitted changes, new files included. It runs in the background by default, so its
findings arrive later: wait for its findings before going further, and never commit
before they are in. Fix those that affect correctness; report the others without
fixing them.

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
only what was run or observed in this session; "Review" keeps its box unticked, since
it is the maintainer's to tick once they have read the full diff; "Closing" ends with
`Closes #$ARGUMENTS`.

Commit, push and pull request creation each raise a permission prompt. That is
expected (ADR-ORG-003): wait for the answer, never look for another way to run the
command.

## 10. Stop

Do not merge. Report:

- the files changed, and why;
- the tests added, and what each one would have caught;
- the verification output;
- anything found and deliberately not fixed;
- the pull request URL;
- what the issue's Proof asks for that only the maintainer can observe.

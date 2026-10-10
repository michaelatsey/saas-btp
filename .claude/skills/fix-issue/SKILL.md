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

## 2. Create the branch

Start from an up-to-date `main` with a clean working tree. Create the branch before
changing any file, named as the root `CLAUDE.md` prescribes.

## 3. Change only what the issue lists

Check `.claude-context/context/decisions/decisions-index.md` for decisions that bear on
the change. Never settle an open decision as a side effect: if the change appears to
require one, stop and say so.

## 4. Cover the change with tests

For a change in behaviour, write the test first and show it failing against the
current code; a test that passes before the change tests something else. For a change
with no behaviour to test (documentation, configuration), name the check that stands
in for a test.

## 5. Verify, and show the output

List the applications the change touches. For each one, read its `CLAUDE.md`
(`apps/<name>/CLAUDE.md`) and run the build and test commands it gives. If an
application has no `CLAUDE.md`, stop and say so. For a change outside `apps/`, run the
checks named in the issue's Proof.

Show the output; do not assert that it passed. If anything fails, fix it and run
again before going further.

## 6. Have a subagent review the diff

Give a subagent the issue text and the diff. Ask it to check that every "Done when"
item is addressed, that each change in behaviour has a test, and that nothing outside
the issue's scope changed. Act only on gaps that affect correctness or the stated
scope, then run step 5 again.

## 7. Commit

Show `git diff --stat` and the commit message before committing. One commit, in the
format the root `CLAUDE.md` prescribes. The body explains the decision, not the diff:
what was wrong or missing, why this change rather than the alternative, and any
behaviour that changed as a side effect.

## 8. Push and open the pull request

Push the branch, then open the pull request into `main`. Its title is the commit
subject. Its body follows `.github/pull_request_template.md`; "Verification" states
only what was run or observed in this session; "Closing" ends with
`Closes #$ARGUMENTS`.

Commit, push and pull request creation each raise a permission prompt. That is
expected (ADR-ORG-003): wait for the answer, never look for another way to run the
command.

## 9. Stop

Do not merge. Report:

- the files changed, and why;
- the tests added, and what each one would have caught;
- the verification output;
- anything found and deliberately not fixed;
- the pull request URL;
- what the issue's Proof asks for that only the maintainer can observe.

# ADR-ORG-005 — Merge commands denied as a second layer, separate identity deferred

Status: accepted
Date: 2026-10-13
Refs: issue #139; ADR-ORG-004; parent #132; BP-001 pilot #138.

## Context
ADR-ORG-004 keeps merging human: the maintainer reads every pull request before merging it.
When it was accepted, nothing technical enforced that for Claude Code:

- `.claude/settings.json` had no rule matching a merge command. The former `ask` rules of
  ADR-ORG-003 never covered merging either.
- The ruleset on `main` requires a pull request, the `api-build-test` check and a squash merge,
  with zero approving reviews. Claude Code acts under the maintainer's GitHub identity, so the
  server cannot tell its merge from the maintainer's.
- In auto mode the only barrier was the classifier. Claude Code's permission-modes
  documentation lists, among the actions the classifier blocks by default: "Merging a pull
  request no human has approved, approving Claude's own pull request, or disabling CI checks".
  The classifier is a model reading the conversation: the same documentation says an approval
  stated in the conversation can clear a block. It exists in auto mode only: manual mode
  prompts the maintainer instead, and `bypassPermissions` mode runs neither the classifier nor
  a prompt.

Requiring one approval on `main` today would block the maintainer, who cannot approve a pull
request opened under their own identity. GitHub's own coding agent avoids this by working under
a distinct identity that cannot approve or merge its pull requests.

Claude Code's documentation states what a Bash deny rule is worth. It is resolved before the
classifier, it blocks in every permission mode, and it applies to each subcommand of a compound
command. But it matches the command text as written: it "covers the invocation Claude usually
produces and isn't a security boundary around the program".

## Decision

### 1. Merge commands are denied in the project settings
`.claude/settings.json` denies the four forms through which `gh` merges a pull request or arms
its automatic merge:

- `gh pr merge`, with any argument;
- the REST merge endpoint called through `gh api` (`/pulls/<number>/merge`);
- the GraphQL mutations `mergePullRequest` and `enablePullRequestAutoMerge` called through
  `gh api`.

### 2. These rules are a second layer, not a boundary
They stand next to the classifier's default block and stop the invocations Claude Code usually
writes. They do not stop a merge written another way: `gh` called by its path or inside
`sh -c`, a GraphQL mutation passed through a pipe, a shell variable or a file so that its name
is absent from the `gh api` command, any other spelling the patterns do not match, another HTTP
client carrying the token, or the GitHub web interface. Where neither managed settings nor a
Team or Enterprise plan apply, an installed mod that answers the permission check can also
approve a call a deny rule refuses. No instruction, skill or review may rely on these rules as
a guarantee that a merge cannot happen.

On the server, a merge into `main` is protected by the ruleset alone, which does not require an
approval. Arming auto-merge is also refused there for as long as the repository setting
disallows it; no rule covers changing that setting. "Merge stays human" is therefore enforced
in depth, not absolutely.

### 3. A separate GitHub identity for Claude Code is deferred
Whether Claude Code works under its own GitHub identity (a GitHub App or a bot account), with
one required approval on `main`, is not decided here. A separate identity only pays off together
with a required approval, and its costs are not measured: token handling, commit attribution,
and the Claude.ai synchronisation of the repository. The question is revisited after the BP-001
pilot (#138), together with auto-merge, on the interruptions and review times it measures.

## Consequences
- A `/fix-issue` run that writes `gh pr merge` or one of the three `gh api` forms is refused by
  Claude Code before the classifier is consulted, in every permission mode, within the limits
  of section 2.
- This adds to ADR-ORG-004, whose Consequences say the deny rules are unchanged: the fifteen
  `Read` rules are kept and four `Bash` rules are appended. Nothing else in ADR-ORG-004 changes;
  no allow or ask rule is added.
- The REST rule matches the path, not the HTTP method, so the read-only "is it merged" request
  on the same path is refused too. `gh pr view --json state,mergedAt` answers the same question.
- The rules match the command text, so they also refuse commands that cannot merge:
  `gh pr merge --help`, `gh pr merge --disable-auto`, and any `gh api` call whose arguments
  merely mention one of the two mutation names or contain `/merge` after a `/pulls/` segment,
  for example an inline review comment on a file named `sync/merge.ts`. `gh pr create`,
  `gh pr comment` and `gh issue comment` are not affected.
- The rules apply only to a session started at the repository root, where Claude Code reads
  `.claude/settings.json`, and on a checkout that carries them: a branch created before this
  decision lacks them until it is rebased. The file sits in the working tree, so Claude Code
  can edit it: such a write goes to the classifier in auto mode and is allowed in
  `bypassPermissions` mode.
- Until the identity question is decided, a merge by Claude Code written in a form the rules do
  not match is stopped only by the classifier in auto mode, or by the maintainer's answer to
  the permission prompt in manual mode.
- If auto-merge is adopted after the BP-001 pilot, the `enablePullRequestAutoMerge` and
  `gh pr merge` rules have to be revisited with it.

## Alternatives considered
- Rely on the classifier alone: nothing to maintain, but it is absent in `bypassPermissions`
  mode, and an approval read from the conversation can clear its block.
- A `PreToolUse` hook that inspects the whole command text: catches more spellings than a
  prefix rule, but it is a script to write, test and maintain, and still not a boundary while
  the token can merge.
- Broader patterns such as `Bash(gh pr * merge *)`: they would match more spellings, but also
  refuse any `gh pr create` whose title or body contains the word.
- Require one approval on `main` now: the only server-side guarantee, but it blocks the
  maintainer for as long as Claude Code shares their identity.
- Decide the separate identity now: the complete answer, but its costs are unmeasured and it is
  tied to the auto-merge decision that ADR-ORG-004 already defers to the BP-001 pilot.

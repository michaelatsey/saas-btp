# ADR-ORG-004 — Autonomous delivery with a second reviewer, merge stays human

Status: accepted
Date: 2026-10-13
Supersedes: ADR-ORG-003
Refs: issue #133; pilot #75; parent #132.

## Context
The maintainer works alone and needs to ship products fast without lowering their quality. The
end-to-end pilot (#75) proved that Claude Code can take an issue from reading to pull request
following the repository's conventions, showing its verification output and stopping before the
merge. It also showed where the time goes: each issue stops for the maintainer at the plan, the
commit, the push and the pull request, before the merge.

Those stops do not protect `main`. Permission rules match command text and can be bypassed by a
differently written command; the server-side ruleset (pull request required, required checks,
squash only) is what protects the default branch.

The maintainer already has a second model challenge plans and decisions by hand, because a
model from another provider fails in ways uncorrelated with Claude's. GitHub applies the same
principle to its own coding agent: its pull requests must be reviewed and merged by a human,
under the repository's rulesets and required checks.

## Decision

### 1. A second reviewer from another provider
A model from another provider than the author's reviews, read-only, every plan, issue
breakdown, design artifact and diff that would otherwise need the maintainer's approval. Codex
is the first choice. The reviewer never writes to the repository. Its output follows one
committed schema (verdict, blocking and non-blocking findings, each tied to a requirement, a
decision or a convention), so the tool can be replaced without changing the workflow.

### 2. Agreement replaces approval, within a cap
A step that needs approval advances without the maintainer when the author and both reviewers
(the fresh-context Claude review and the second reviewer) keep no blocking finding. A finding
counts as blocking only if it affects correctness, a stated requirement or a recorded decision.
After two review cycles on the same artifact, a remaining blocking finding goes to the
maintainer. While no second reviewer is available, every step that would advance on agreement
stops for the maintainer.

### 3. Git and GitHub authority
- Claude Code commits and pushes on working branches, and opens pull requests, without a
  permission prompt.
- Claude Code creates the issues derived from a confirmed Business Process without a prompt.
- Merging stays human: the maintainer reads every pull request before merging it.
- `main` stays protected by its ruleset; GitHub is reached through `gh` only; no GitHub MCP
  server is enabled.
- Auto-merge is not used.

### 4. Where the maintainer is always asked
- One validation sitting per Business Process: the brief, the Specification, the Design
  Package, and the decisions they raise.
- Every merge.
- Any irreversible action: destructive schema change, deletion of data or files outside a
  working branch, anything touching production.
- Any disagreement still open after the review cap.
- Any question only the client can answer; it is never guessed.

### 5. The design method's tooling
The Process-First Design Method stays the standard. It runs through the `solution-design`
plugin (michaelatsey/solution-design). The run-01 agent blueprint
(`process-first-agent-blueprint-run-01.md`) is retired: the plugin replaces it.

## Consequences
- An issue interrupts the maintainer once, at the merge, unless an escalation point is hit.
- The pull request carries what the maintainer needs to decide without re-running anything:
  verification output, every review finding and what was done with it, and what was
  deliberately left out.
- The quality of unattended work now rests on verification: required checks on `main`, tests
  written first, and local verification that runs the full suite (#137).
- `.claude/settings.json` drops the `ask` rules on commit, push and pull request creation; its
  deny rules are unchanged.
- Running without a prompt relies on auto mode, whose classifier reviews each action; in manual
  mode these commands still prompt. No allow rule is added.
- `/fix-issue` and the plugin's gates must implement the agreement rule and the escalation
  points (#135, solution-design#2 and #4).
- Usage of the second reviewer counts against its provider's limits; the BP-001 pilot (#138)
  measures it.
- Auto-merge is revisited after the BP-001 pilot (#138), on its measured interruptions and review
  times.

## Alternatives considered
- Keep ADR-ORG-003 as is: safe, but four or five stops per issue, none of which protects `main`.
- Auto-merge on green checks now: fewest stops, but the local suite skips integration and
  migrator tests (#75), so a green run proves less than it appears.
- A second Claude subagent only: free and already in place, but its blind spots correlate with
  the author's.
- A custom orchestrator or n8n between the two models: more to build and maintain than the
  plugin and two skills.

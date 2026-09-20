# ADR-ORG-003 — Claude's git authority, gated by permission rules

Status: accepted (provisional: to be revisited after the end-to-end pilot, #75)
Date: 2026-09-20
Refs: issue #79 (D3); ADR-ORG-002.

## Context
A standing rule in Claude Code auto memory stated that Claude performs no git operation and uses no
custom agents. The target workflow from Anthropic guidance has an explicitly invoked `/fix-issue`
skill that implements, tests, commits, pushes and opens the PR, with reviews run as subagents in a
fresh context. Permission rules are enforced by Claude Code itself, whereas instructions only shape
what Claude attempts.

## Decision
Claude Code may run `git commit`, `git push` and `gh pr create`, each gated by an `ask` permission
rule requiring explicit owner approval. Merging stays human. GitHub is reached through `gh` only.
This supersedes the auto memory rule "Claude never runs git".

## Consequences
- The auto memory rule and `~/workspace/.claude/CLAUDE.md` must be aligned (Claude Code phase).
- `ask` rules match command text, so they are not a security boundary: the server-side ruleset
  (PR required, protected default branch) remains the real boundary.
- No GitHub MCP server is enabled, so no API path bypasses the gates.

## Alternatives considered
- Keep the rule (Claude produces code only): safe, but excludes the official workflow.
- Local commits only, push and PR by the owner: fewer gates, but still a manual hand-off.

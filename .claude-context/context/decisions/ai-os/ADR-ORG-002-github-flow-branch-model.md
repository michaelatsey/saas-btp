# ADR-ORG-002 — GitHub flow branch model

Status: accepted
Date: 2026-09-20
Refs: issue #78 (D2).

## Context
The repository used `main` (default) plus `dev` (integration). PRs targeted `dev`, so closing
keywords were ignored (they only apply to PRs targeting the default branch). The ruleset enforces
linear history, so every `dev → main` promotion is a squash that recreates a divergence: the
squash of #26 left `main` holding a commit absent from `dev`, and a direct merge now conflicts on
15 files although both hold the same content.

## Decision
Adopt the GitHub flow: short-lived branches → PR → `main` (default branch), squash merge, branch
deleted after merge. `dev` is retired after a final promotion.

## Consequences
- Closing keywords work on every PR; issues close automatically at merge.
- One ruleset, on `main`; automatic deletion of merged branches can be enabled safely.
- No more promotions, hence no more squash divergence.
- Conventions stating "feature branches from `dev`" must be updated.

## Alternatives considered
- `dev` as default branch, `main` as release branch: closing keywords would work, but each
  squash promotion recreates the divergence, and it needs two rulesets and a guard against
  deleting `dev`.

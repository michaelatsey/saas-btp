# ADR-ORG-001 — The repository stays public

Status: accepted
Date: 2026-09-20
Refs: issue #77 (D1).

## Context
The Claude.ai project must read the repository: the "Reprise roadmap" procedure reads public
github.com pages, and the project knowledge will be synced from GitHub. The README claims
"Private and proprietary" while the repository is public. On GitHub Free, rulesets and the
minimal security features (secret scanning, push protection, code scanning, Dependabot alerts)
are free for public repositories.

## Decision
The repository stays public.

## Consequences
- Claude.ai keeps read access; the resume procedure stays valid.
- Any committed secret would be public: secret scanning and push protection become a priority.
- The README must be corrected together with an explicit licence statement: public visibility
  grants no reuse right.

## Alternatives considered
- Private repository: protects the source code, but requires GitHub Pro for rulesets and breaks
  the Claude.ai resume procedure (no GitHub connector available in the directory).

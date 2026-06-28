# Architecture

Status: NOT YET DEFINED — fills in at AI OS Palier 1, after Phase 1 scope.

> This file will hold the modular monolith decomposition: bounded contexts,
> module dependency graph, offline-first / sync strategy, and how the MicroKit
> ecosystem is consumed.

## To define

- Bounded contexts / modules (driven by Phase 1 scope).
- Module dependency graph (allowed dependencies, forbidden ones).
- Offline-first architecture and sync strategy (PowerSync, conflict resolution).
- MicroKit packages consumed and where.
- Deployment topology (Coolify: api / web, prod + staging).

## Decided so far

- Backend: .NET 10 modular monolith (not microservices — see CLAUDE-SAAS-BTP.md 20).
- Contract boundary: OpenAPI, not C#->TS generation (ADR-ARCH-001).

See `../decisions/decisions-index.md` for all architecture decisions.

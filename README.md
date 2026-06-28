# SaaS BTP

Vertical B2B SaaS for construction operations (field execution, QHSE audits,
non-conformities, corrective actions, reporting) — offline-first.

## Monorepo layout

```
apps/
  api/        .NET 10 modular monolith (Hexagonal · DDD · CQRS · MicroKit) — isolated from the JS workspace
  web/        Next.js + TypeScript + Shadcn/ui + TailwindCSS
  mobile/     React Native (Expo), Phase 2 — offline-first via PowerSync
packages/     Shared JS packages (api-client generated JIT from openapi.json)
design/       Figma <-> code bridge (tokens + exports), AI OS Palier 3
infra/        Docker, Coolify, Supabase
scripts/      setup / db / ci utilities
docs/         product / architecture / runbooks
```

## Contract boundary

The frontend/backend contract is the committed OpenAPI document produced by
`apps/api`. TypeScript clients are generated from it into `packages/api-client`.
The .NET API stays fully isolated from the pnpm/JS workspace. See
`ADR-ARCH-001` (OpenAPI contract boundary).

## Stack

- Backend: .NET 10 modular monolith
- Web: Next.js 16+ · TypeScript · Shadcn/ui · TailwindCSS
- Data: Supabase (PostgreSQL + Auth + Storage)
- Mobile (Phase 2): React Native (Expo) · PowerSync
- Infra: Hetzner VPS · Coolify · Cloudflare

## Status

Project bootstrap — repository skeleton only, no application code yet.

## License

Private and proprietary. All rights reserved. Not for distribution.


## Setup dev

Point important sur le partage du hook
core.hooksPath est une config locale, pas committée. Donc le hook te suit (le fichier .githooks/pre-push est versionné), mais sur une nouvelle machine tu devras refaire une seule fois :

```bash
bashgit config core.hooksPath .githooks
```

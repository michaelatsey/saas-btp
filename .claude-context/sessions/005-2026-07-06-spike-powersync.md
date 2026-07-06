# Session — 2026-07-06 (Step 5.0: PowerSync Web offline spike)

Continues: 004-2026-07-03-step3-stories.md.
Goal: Step 5.0 — run spike #3 (PowerSync Web offline + camera on target Android) to
gate ADR-ARCH-003 (increment-1 client: offline PWA vs native Expo).

---

## Starting context

- Step 3 complete: 8 stories as issues #3-#10, labeled + milestone #1 + board #9.
- Attack order put #3 (spike) first — de-risks offline (the differentiator) and gates
  ADR-ARCH-003 before any apps/api scaffolding.
- ADR-ARCH-003 status was "accepted, conditional on spike" pending this run.

---

## Completed

### Harness (disposable)

- Spike harness scaffolded under docs/spikes/powersync-web/: Vite + TS +
  vite-plugin-pwa + @powersync/web 1.38.6 + @powersync/attachments. Disposable by
  design.
- Disposable backend: Supabase project spike-powersync-web + PowerSync Cloud free dev
  instance, one global sync rule (SELECT * FROM constats).
- Served via cloudflared quick tunnel over `pnpm preview` (production build), NOT the
  dev server.

### Gates — all 3 PASS on target Redmi / Chrome / installed WebAPK

- Gate 1 — offline persistence + sync: OPFSCoopSyncVFS (wa-sqlite, OPFS) backend,
  ~10GB quota, 5/5 constats no loss, triple-verified (local diagnostics + Supabase
  table + PowerSync Sync Diagnostics).
- Gate 2 — offline photo, decoupled: 2a latency (row synced before large photo),
  2b sabotaged-upload retry (row synced while upload broken, queue auto-retried on
  un-sabotage), 2c volume + offline force-kill (100% upload, no dupes). The flagged
  alpha @powersync/attachments proved MATURE on the capture/deferred-upload/retry/
  volume paths.
- Gate 3 — durability across full reboot: persist() granted (persisted(): true),
  data survived a full phone reboot WITHOUT uninstall, auth-independent counters
  disambiguated token-expiry from eviction (eviction not reachable at ~10GB quota).

### Findings + ADR

- findings.md written under docs/spikes/powersync-web/ — the only surviving artifact.
- ADR-ARCH-003 gate cleared. Verdict: PWA retained for the increment-1 field client;
  native Expo stays deferred to Phase 2. Five tolerable build constraints carried into
  the ADR (online-first hydration, SW/asset config care, token refresh, uninstall
  wipes non-synced OPFS data, multi-photo UI to build). None weighs toward Expo.

### Merge + teardown

- Committed findings + ADR (Option A: harness NOT committed), PR #12 squash-merged to
  dev, issue #3 closed, branch deleted.
- Teardown done: PowerSync instance, Supabase project, cloudflared tunnel, local .env,
  and the PWA on device all removed.

---

## Incidents / lessons

| Incident | Lesson |
|----------|--------|
| Serving `pnpm dev` through the tunnel gave a dev SW with no precache; offline the CSS + PowerSync JS/WASM were missing (false CSS/diagnostics-offline failures). | Testing a PWA offline requires the PRODUCTION build (`pnpm preview`), not the dev server. Real lesson for the future Next.js PWA. |
| Manifest declared icons that were not in the build; Chrome offered only "Add to home screen". | Missing manifest icons silently degrade a WebAPK to a shortcut — ship real >=512px PNGs. |
| Going offline before the SW reached "activated" reproduced the missing-asset failure. | First launch must be online to hydrate the precache + seed the local DB (offline-first constraint, not PWA-specific). |
| Uninstalling the PWA cleared local data. | Uninstall wipes OPFS local storage (non-synced data lost); SW code updates do NOT wipe. |
| "Pending photos" counter appeared stuck. | Our diagnostics read `photo_id` instead of `attachments.state` — our defect, not an SDK defect. |
| Stray root pnpm-lock.yaml + node_modules appeared. | Created by a `pnpm install` run from the repo root; removed before commit. |

---

## Current position

- Step 5.0 (spike) DONE; ADR-ARCH-003 cleared (PWA retained).
- Next: Step 5.1 scaffold apps/api on the first bounded context; Step 4 btp-* agents to
  emerge JIT from the first domain story (#4 access or #5 constat).

---

## Open items / debt

- Offline conflict-resolution strategy for safety data (future ADR, still open).
- Back-button behavior in an installed PWA on Android: NOT tested (single-screen
  harness) — must validate in the real client (known PWA friction).
- Token refresh after long offline sessions — architecture requirement for the real
  client.
- Multi-photo per constat + gallery picker — product UI to build.
- Tooling to set up cold (out of session): Context7 auth, powersync-ja/agent-skills
  evaluation, persistent corepack in .zshrc.

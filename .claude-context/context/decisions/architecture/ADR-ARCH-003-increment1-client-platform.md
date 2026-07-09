# ADR-ARCH-003 — Increment 1 field client = offline PWA (not native Expo yet)

Status: accepted (2026-06-28); spike-gate cleared 2026-07-06 (see Consequences)
Date: 2026-06-28

## Context

Increment 1 (Safety constat -> Corrective action -> dashboard) needs two client
experiences: field capture (offline, photo-first, Android, gloves, poor network) and
desktop review/validation/dashboard. Offline-first is a core bet from increment 1,
but native mobile (Expo) is planned for Phase 2. Question: does increment 1 ship as
an offline web PWA, or do we advance the native Expo app to increment 1?

## Decision

Increment 1 ships as an offline-capable PWA built with Next.js + the PowerSync Web
SDK. One web codebase serves both field capture and the desktop dashboard. The native
Expo app is deferred to Phase 2 and built once the pilot validates the product AND
surfaces concrete PWA limitations.

## Consequences

- Easier: one codebase (Next.js) for field + dashboard instead of two; faster
  time-to-market; pilot distribution without app stores; consistent with the
  progressive method (prove the hypothesis cheaply, build the heavy native client
  when real usage justifies it).
- Harder / risk: offline PWA on low-end Android may be less reliable than native,
  risking a false-negative pilot (failing on tech, not product).
- MITIGATION (gating) — CLEARED 2026-07-06: a technical spike of PowerSync Web offline
  + camera capture ran on a real target Android device (Redmi / Chrome / installed
  WebAPK). All three gates passed — Gate 1 offline persistence + sync, Gate 2 offline
  photo decoupled (2a latency / 2b sabotaged-upload retry / 2c volume + force-kill),
  Gate 3 durability across full reboot. Every blocker en route was fixable harness/config,
  never a platform limit; the flagged alpha @powersync/attachments risk proved mature on
  web for the offline-capture / deferred-upload / retry / volume paths. The
  escalate-to-Expo condition did not trigger. Findings:
  docs/spikes/powersync-web/findings.md. The "accepted" status is no longer conditional —
  the spike gate is satisfied.
- Phase 2 native app is informed by pilot evidence (what the PWA could not do well).
- BUILD CONSTRAINTS (from the spike, tolerable / documented): (1) online-first hydration
  — one online launch to seed the precache + local DB before full offline; (2) SW/asset
  config care — serve the production build, ship real >=512px manifest icons; (3) token
  refresh after long offline sessions (platform-neutral architecture requirement); (4)
  uninstalling the PWA wipes non-synced OPFS data (SW code updates do not); (5) multi-photo
  per constat + gallery picker is standard PWA UI work to build. None weighs toward Expo.

## Rationale (verified 2026-06)

- PowerSync officially supports web offline: the JS Web SDK gives a local SQLite
  database (instant offline read/write) and is documented for use with PWA tech for a
  true offline-first web experience. SDK is mature (v1.38+, recent offline reconnect
  fixes).

## Alternatives considered

- Build Expo native now for increment 1: rejected for now — doubles the codebase for a
  solo founder and front-loads heavy work before the product hypothesis is proven.
  Remains the Phase 2 path and the spike-escalation path.
- Online-only web for increment 1: rejected — offline is a core bet that must be
  proven in increment 1, not deferred.

# ADR-ARCH-003 — Increment 1 field client = offline PWA (not native Expo yet)

Status: accepted (2026-06-28), gated by a technical spike (see Consequences)
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
- MITIGATION (gating): before the full increment-1 build, run a technical spike of
  PowerSync Web offline + camera capture on a real target Android device. If the spike
  shows offline web is not reliable on field devices, escalate to Expo for increment 1.
  This ADR's "accepted" status is conditional on that spike passing.
- Phase 2 native app is informed by pilot evidence (what the PWA could not do well).

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

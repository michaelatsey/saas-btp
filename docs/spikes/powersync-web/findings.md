# Findings — Spike #3: PowerSync Web offline + camera on target Android

Gates ADR-ARCH-003 (increment-1 field client: offline PWA vs Expo).
Disposable spike; this file is the only surviving artifact.

## Environment
- Device / browser: Redmi (low-end Android) + Chrome, installed as a WebAPK
- PowerSync Web SDK version: @powersync/web 1.38.6
- Supabase project: spike-powersync-web (disposable) | PowerSync Cloud (free, dev instance)
- Serving: Cloudflare tunnel -> local **production build** (`pnpm build` + `pnpm preview`),
  NOT the dev server (see pre-gate config gotchas below)
- Date run: 2026-07-05/06

## Gate 1 — Offline persistence + sync
- Result: PASS
- Storage backend actually used (OPFS / wa-sqlite+IndexedDB): **OPFSCoopSyncVFS (wa-sqlite,
  OPFS)** — the Chromium default; IndexedDB fallback was not used.
- Storage quota observed: ~10.2 GB quota / 9.1 MB usage. Large — NOT the constrained
  low-end quota we expected on a cheap Android. Consequence: Gate 3 eviction will be hard
  to trigger naturally; record that caveat when running Gate 3.
- Constats created / landed in Postgres: 5 / 5. Local count held at 6 (1 pre-existing + 5)
  across offline force-kill + relaunch (data served from local SQLite while offline).
- Data loss or corruption: none. Cross-verified via three independent sources — local
  diagnostics strip (6), Supabase table (6 rows, no dupes, fields intact), and PowerSync
  Sync Diagnostics.
- Notes: offline create -> force-kill -> relaunch-offline -> reconnect -> sync all clean.
  OPFS-backed wa-sqlite confirmed empirically (diagnostics strip + presence of the OPFS db
  file), settling the SDK's OPFS-vs-IndexedDB question for this device.

## Gate 2 — Offline photo, decoupled
- Result: PASS (all three sub-tests 2a + 2b + 2c pass)
- Decoupling under LAG (2a — row synced before large photo finished, no sabotage): YES —
  evidence: the constat row reached Postgres before the large photo finished; the counter
  flipped pending 1 -> 0 / synced 0 -> 1; file landed in Supabase Storage, single write,
  no dupes, row marked SYNCED. Row does not wait on the attachment.
- Decoupling under FAILURE (2b — row synced while upload sabotaged): YES — evidence: with
  "Break photo upload" ON, on reconnect the constat row still synced despite the photo
  upload failing (photos pending held at 1). On un-sabotage, the attachment queue
  auto-retried and the photo uploaded (pending 1 -> 0, synced +1, file in Storage).
  Decoupling under failure + retry resilience to transient upload failure.
- Volume + durability (2c — multiple photos, offline force-kill): YES — evidence: multiple
  constats each with a photo (incl. one large) queued offline, survived an offline
  force-kill, and all uploaded 100% on reconnect (pending -> 0, synced = total), no dupes
  in Storage.
- Photos recovered + uploaded: 100% across all three sub-tests (pending -> 0, synced =
  total, no dupes in Storage).
- @powersync/attachments web maturity (mature / not mature — decisional toward Expo if not):
  MATURE for the offline-capture / deferred-upload / retry / volume paths on the target
  device. The earlier "stuck counter" was our own diagnostics reading `photo_id` instead of
  `attachments.state`, not an SDK defect — now fixed.
- Notes: Data point TOWARD keeping the PWA — decoupling holds under latency (2a) and
  sabotaged upload (2b), the queue auto-retries and recovers with no manual intervention and
  no row/photo loss, and volume + force-kill (2c) uploads 100% clean.
- PRODUCT feature to build (NOT a platform limit / not Expo-only): the spike harness is
  single-photo with no gallery multi-select. The real client needs multi-photo per constat
  plus a gallery picker — standard PWA work, achievable on the web platform; it does not
  weigh toward Expo.

## Gate 3 — Durability / eviction
- Result: PASS (clean run)
- persist() granted: YES  | persisted(): true
- Local data survived force-kill + reboot (verified independent of token state): YES —
  constats + pending photos created offline survived a full phone reboot WITHOUT uninstall;
  counts stayed intact via the auth-independent counters, and on reconnect with a fresh
  token everything synced up (rows to Supabase, photos to Storage, pending -> 0).
- Token expiry disambiguated from eviction on reload: YES — data was present after reboot
  independent of token state; the fresh-token reconnect synced cleanly, confirming the
  reload state was a token concern, not eviction.
- Eviction observed under normal use: NO — not reachable; ~10 GB quota on target device,
  favorable to PWA.
- Notes:
  - Uninstalling the PWA wipes OPFS local storage — non-synced data is lost on uninstall.
    This is normal Android/Chrome behavior, NOT a durability failure. Code updates via the
    service worker do NOT wipe storage; only a manual uninstall does.
  - Dev token 12h expiry surfaced the need for token refresh after long offline sessions —
    an architecture requirement for the real client. Applies to PWA or Expo equally; not a
    platform limit.
  - Eviction under disk pressure not reachable given the ~10 GB quota on the target device —
    favorable to PWA.

## Rider — intrinsic PWA-on-Android friction (observations, no gate)
- Back button: NOT OBSERVED — the harness is single-screen, so Android back-button behavior
  inside the installed PWA was not exercised. Flag for the real client: PWA back-button
  handling on Android (exit vs in-app navigation vs state loss) is a known friction point and
  must be validated once the client has real navigation.
- Camera latency: camera opened smoothly, no notable latency capturing photos on the target
  device.
- Install / fullscreen: real WebAPK install obtained after fixing the manifest icons;
  standalone app window with no browser URL bar confirmed, including offline. Good
  install/fullscreen experience on Android once icons are correct.
- Chrome-specific quirks: none observed during the gates — no unexpected Chrome behavior
  encountered.

### Pre-gate config gotchas (all fixable harness/serving issues — NOT platform limits)
Recorded because they cost real time before Gate 1 could run, and they are genuine ops
notes for the eventual Next.js PWA — but none is an intrinsic PWA-on-Android constraint,
so none weighs toward Expo:
- **Dev server has no production service worker.** Serving `pnpm dev` through the tunnel
  gives a dev SW that does not precache the module/asset graph, so offline the CSS and the
  JS/WASM that boot PowerSync were missing (unstyled page + diagnostics stuck on "loading").
  Fix: serve the production build (`pnpm build` + `pnpm preview`). Workbox config was
  already correct; the failure was serving method.
- **Missing manifest icons silently degrade a WebAPK to a shortcut.** The manifest declared
  `/icon-192.png` + `/icon-512.png` but the files didn't exist in the build; `vite preview`
  returned `index.html` for them, so Chrome offered only "Add to home screen" (a shortcut),
  not "Install app" (a real WebAPK). Fix: ship real ≥512px PNGs at the manifest paths.
- **First launch must be online.** The installed PWA needs one online launch to hydrate the
  Workbox precache (~8 MB incl. wa-sqlite WASM) and seed the local DB before it works fully
  offline; going offline before the SW reaches "activated" reproduces the failure above.

## Decision (ADR-ARCH-003 mapping)
- 3 gates pass + clean rider            -> PWA for the MVP
- 3 gates pass + tolerable rider limits -> PWA, limits documented in the ADR
- any single gate fails                 -> Expo (PWA eliminated)

**Verdict:** PWA retained for the increment-1 field client. ADR-ARCH-003 confirmed; the
native Expo app stays deferred to Phase 2.

**Landing zone:** "3 gates pass + tolerable documented limits -> PWA, limits documented in
the ADR." Not the fully-clean branch: the run surfaced a handful of tolerable limits, all
either fixable ops/config or platform-neutral architecture requirements — none an intrinsic
PWA-on-Android blocker, none weighing toward Expo. Tolerable limits carried into the ADR:
- Online-first hydration: the installed PWA needs one online launch to hydrate the Workbox
  precache (~8 MB incl. wa-sqlite WASM) and seed the local DB before it works fully offline.
- Service-worker / asset config care: production build must be served (dev SW does not
  precache the module/asset graph); real manifest icons (>=512px) must ship or the WebAPK
  silently degrades to a shortcut.
- Token refresh after long offline sessions: the dev token 12h expiry surfaced the need for
  token refresh — an architecture requirement for the real client (applies to PWA or Expo
  equally, not a platform limit).
- Uninstall wipes non-synced local data: uninstalling the PWA clears OPFS storage, so any
  non-synced local data is lost (normal Android/Chrome behavior; SW code updates do NOT wipe
  storage — only a manual uninstall does).
- Multi-photo is UI work: the harness is single-photo; multi-photo per constat + gallery
  picker is standard PWA UI work to build, not a platform capability gap.

**Rationale:** Gates 1, 2 (2a/2b/2c) and 3 all pass on the target device (Redmi / Chrome /
installed WebAPK). Every blocker encountered en route was a fixable harness/serving/config
issue, never an intrinsic platform limit. The pre-identified alpha risk — @powersync/
attachments maturity on web — proved MATURE for the offline-capture / deferred-upload /
retry / volume paths, removing the main reason to escalate to Expo. Storage quota on target
(~10 GB) makes eviction effectively unreachable, further favoring the PWA.

**ADR-ARCH-003 action:** confirm accepted + document limits. The spike gating condition in
Consequences is satisfied (spike passed); lift the "conditional on spike" wording, record
the spike result, and carry the tolerable limits above into the ADR as build constraints.

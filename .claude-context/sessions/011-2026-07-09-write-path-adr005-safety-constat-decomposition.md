# Session — 2026-07-09 (Safety Constat decomposition on paper; write-path ADR-ARCH-005 decided + merged)

Continues: 010-2026-07-08-story-a-currentsite-implemented.md.
Purpose: decompose the first Safety story (Constat) on paper before any code, and
settle the load-bearing write-path question (does the .NET domain sit on the write
path, or only on read/reaction). No product code this session — a decision
(ADR-ARCH-005) plus aggregate/endpoint framing only.

## Sequencing agreed

First Safety increment runs in three ordered steps:
1. Decompose the Constat on paper [this session].
2. Wire Supabase (infra only: project + secrets in Bitwarden/Doppler + connection +
   verify Story A GET /context 200/403 with a real token).
3. Safety schema + Constat aggregate code (first product persistence).
Rationale: the schema depends on the frozen aggregate; wiring infra separately
de-risks it; Story A live HTTP verification unblocks at step 2.

## Constat = finding, not ticket

- The Constat is a Safety finding (a fact). It is NOT the corrective lifecycle. The
  detection -> assignment -> correction -> validation -> closure cycle is
  CorrectiveActions (ADR-ARCH-002), consumed later via SafetyConstatRaised.
- Field split from Innov_Boost's constat table:
  - Safety keeps: id, tenantId (Access), siteId (currentSite), type, severity,
    corpsDeMetier, occurredAt, location, descriptionBreve, descriptionDetaillee,
    observations, observer.
  - Moves to CorrectiveActions: Statut (treatment), Responsable, Action,
    Suivi/validation, deadline/verification/closure.
  - Media (photos): deferred (no Media BC / no capture UI in Story 1).
  - Event emission (SafetyConstatRaised): later slice — outbox/MicroKit.Messaging
    not wired in the product yet (session 007). Story 1 persists, it does not emit.

## Aggregate shape (frozen)

- observer = ObserverId + ObserverSnapshot{nom, fonction} frozen at creation
  (constat is a near-legal proof; must stay faithful to the moment T).
- severity = Severity VO (Minor / Major / Critical) in Story 1 — near consumer:
  critical-severity alerts (architecture.md).
- No status field in Story 1: existence = recorded. No single-value enum (noise).
  Cancelled deferred until a real cancellation need (who/why/traceability). No Draft
  (UI state, not domain; offline "pending sync" is an infra flag, not a domain state).
- Rejected (YAGNI / wrong context): the enrichment list (Probability, Risk Level,
  Regulation, Theme, Zone/Batiment/Niveau/topology — topology belongs to Site),
  Origin, and the configurable checklist template (Phase 2 per VISION 12-13).
  category deferred (no increment-1 consumer). Media flag deferred.
- Invariants: site required, tenant required, type + severity required,
  descriptionBreve required, occurredAt required / non-future / immutable, observer
  snapshot frozen.
- Guardrails to extend to the Safety module (architecture tests, like #4/#17):
  anti-corrective, anti-Membership (ADR-ARCH-004 extended), hexagonal (Domain
  references nothing), Safety never references CorrectiveActions/Notifications/Media.

## Write path — ADR-ARCH-005 (decided + merged)

- Spike 005 validated the offline MECHANISM but did NOT decide the production write
  path (it used Supabase-direct, no .NET in the loop). Verified against PowerSync
  docs (2026-07-08): the write path is developer-defined via uploadData(), which can
  target a custom .NET API; direct-to-Postgres is NOT imposed.
- Decision: uploadData() -> .NET API. PowerSync is transport/replication, not a write
  authority. The .NET domain is the single write entry point; online and offline are
  the same command hitting the same aggregate. CQRS made explicit: commands via API,
  reads from local SQLite synced by PowerSync (ADR-ARCH-003).
- Story 1 builds ONLY the API segment (aggregate + write endpoint + GET). No
  PowerSync client this story.
- Queue-safety constraint recorded: never a queue-blocking 4xx on validation failure.
  The full rejection protocol (accepted-for-processing vs synced-back
  Rejected/NeedsReview + HTTP semantics) is DEFERRED to the offline client slice and
  the conflict-resolution ADR.
- Invariants minimal but NOT anemic: deterministic / always-true / offline-verifiable
  rules stay on the aggregate; rules needing server or cross-context state (responsable
  exists, site open) live in other workflows.
- Client-generated aggregate id; the id FORMAT is deferred to the Constat story (this
  next step), recorded in naming.md — cross-cutting.
- GPT review folded in before commit: authoritative-endpoint principle, CQRS framing,
  testing consequence, minimal-not-anemic clarification, deferred rejection protocol,
  id-format deferred.
- Merged to dev via PR (branch docs/adr-arch-005-write-path). This session's
  housekeeping registers it in decisions-index.md.

## Endpoint contract (paper)

- POST /constats: site from currentSite (X-Site-Id, resolved by the Site module —
  never in URL/body); body carries the client-generated ConstatId; idempotent on
  ConstatId (already present -> 200, not 409); success -> 201.
- GET /constats/{id}: read-back (Story 1 round-trip proof).
- GET /constats: list scoped to currentSite (optional in Story 1, proves scoping).
- Story 1 stays plain REST (400 on invalid). The 2xx-non-blocking queue semantics is
  deferred (ADR-005) to the offline client slice.

## id format — recommended, pending confirmation

- UUIDv7 recommended: time-ordered index locality on an append-heavy constat table,
  native in .NET 10 (Guid.CreateVersion7, RFC 9562), Postgres uuid type,
  generatable client-side in JS. Over v4/GUID (index fragmentation) and ULID
  (non-native type + a lib both sides).
- Caveats to validate at persistence wiring (not blockers of the format choice):
  .NET's v7 has no intra-ms monotonic counter and an endianness representation quirk
  (check the Npgsql Guid->uuid mapping preserves order); the client generates the id
  in JS, so a uuidv7 JS lib is needed, not only Guid.CreateVersion7.

## Next / re-entry

- Confirm UUIDv7 (OPEN) -> Constat is paper-complete.
- Then step 2: wire Supabase (project + secrets Bitwarden/Doppler + connection +
  Story A GET /context 200/403 verification). First product persistence
  (DbContext + MicroKit.Persistence entry) depends on it.
- Then step 3: Safety schema + Constat aggregate code.
- Persistence seam (repository port in Application, EF Core adapter in
  Infrastructure, DbContext in Safety.Infrastructure — mirrors Access/Site) to be
  drawn at step 2/3, not in the void.

### Re-entry prompt for the web orchestration chat

"On reprend le SaaS BTP. Lis la derniere session
(011-2026-07-09-write-path-adr005-safety-constat-decomposition.md) et
build-checklist.md. Etat : ADR-ARCH-005 (write path = uploadData -> API .NET, PowerSync
= transport, pas autorite d'ecriture) decide et merge sur dev. Le Constat est decompose
sur papier : finding (pas ticket), aggregate fige (ObserverId+Snapshot, Severity VO, pas
de status, category/Media deferes), guardrails anti-corrective/anti-Membership a etendre
au module Safety. Contrat endpoint pose (POST /constats idempotent sur ConstatId, GET,
REST simple 400 en Story 1, zero client PowerSync). Reste a confirmer : format d'id
UUIDv7 (ma reco, natif .NET 10). Une fois confirme = paper-complete, prochain move =
cabler Supabase (etape 2, debloque aussi la verif HTTP de Story A), puis schema + code
Constat (etape 3, premiere persistance produit). Cadre-moi avant tout code, une etape a
la fois, doc gh/MicroKit verifiee, jamais --delete-branch quand la head est dev,
confirmation avant toute action destructive."

## Open items / debt (carried)

- id format UUIDv7 confirmation (this session's open decision).
- MicroKit follow-ups from #4 (findings 2-4) — docs/microkit-followups-from-story-4.md.
- Remove the Microsoft.OpenApi 2.10.0 pin once AspNetCore.OpenApi ships a patched
  transitive (NU1903).
- Offline conflict-resolution strategy + domain-rejection response protocol (future ADR).
- Story A live HTTP verification pending a real Supabase token.
- core.hooksPath points to an absent .githooks dir (pre-push guard inactive locally).
- Directory.Packages.props stale header comment (Auth preview.2 vs preview.3 pins).
- Naming: module-Site vs aggregate-Site — revisit at the first real Site aggregate.

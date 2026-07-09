# Session — 2026-07-09 (Safety Constat paper-complete; write-path ADR-ARCH-005 decided + merged)

Continues: 010-2026-07-08-story-a-currentsite-implemented.md.
Purpose: settle the load-bearing write-path question (does the .NET domain sit on the
write path), then decompose the first Safety story (Constat) fully on paper — aggregate,
endpoint contract, id convention, domain model. No product code this session — one
decision (ADR-ARCH-005) plus frozen paper design.

## Sequencing agreed

First Safety increment runs in three ordered steps:
1. Decompose the Constat on paper [this session — DONE].
2. Wire Supabase (infra only: project + secrets in Bitwarden/Doppler + connection +
   verify Story A GET /context 200/403 with a real token).
3. Safety schema + Constat aggregate code (first product persistence).
Rationale: the schema depends on the frozen aggregate; wiring infra separately de-risks
it; Story A live HTTP verification unblocks at step 2.

## Constat = finding, not ticket

- The Constat is a Safety finding (a fact). It is NOT the corrective lifecycle. The
  detection -> assignment -> correction -> validation -> closure cycle is
  CorrectiveActions (ADR-ARCH-002), consumed later via SafetyConstatRaised.
- Field split from Innov_Boost's constat table:
  - Safety keeps: id, tenantId (Access), siteId, type, severity, corpsDeMetier,
    occurredAt, location, descriptions/observations, observer.
  - Moves to CorrectiveActions: Statut (treatment), Responsable, Action,
    Suivi/validation, deadline/verification/closure.
  - Media (photos): deferred (no Media BC / no capture UI in Story 1).
  - Event emission (SafetyConstatRaised): later slice — outbox/MicroKit.Messaging not
    wired yet (session 007). Story 1 persists, it does not emit.

## Aggregate shape (frozen) — full model in context/architecture/safety-domain-model.md

- observer = ObserverSnapshot(userId, fullName, function?) frozen at creation (proof
  fidelity). Consolidated into one VO (userId is the reference, fullName/function the
  frozen copy).
- severity = Severity VO (Minor / Major / Critical). Standalone — NOT a placeholder for
  a future Severity x Probability matrix (rejected YAGNI).
- No status field: existence = recorded. No single-value enum. Cancelled deferred to a
  real cancellation need. No Draft (UI state; offline "pending sync" is an infra flag).
- occurredAt (business, client, immutable) vs createdAt (technical, server audit) —
  never conflated; dashboards read occurredAt (else skewed by offline sync lag).
- Rejected (YAGNI / wrong context): enrichment list (Probability, Risk Level, Regulation,
  Theme, Zone/Batiment/topology — topology is Site's), Origin, configurable checklist
  template (Phase 2). category deferred. Media/Evidence deferred (documented as the
  future reference-only shape, name "Evidence"). Location coordinates deferred (need GPS
  capture). Status/Cancel deferred.
- Invariants (via Create() factory, no public ctor): site/tenant/type/severity/observer
  required, shortDescription non-empty, occurredAt required/immutable and
  `occurredAt <= createdAt + clock-drift tolerance` (not `<= now`, to not reject a
  slightly-fast field device at sync). All deterministic + offline-verifiable.
- Guardrails to extend to the Safety module (architecture tests): anti-corrective,
  anti-Membership (ADR-ARCH-004), anti-Media-binary, hexagonal (Domain references
  nothing), no cross-context reference, no event emission this story.

## Write path — ADR-ARCH-005 (decided + merged)

- Spike 005 validated the offline MECHANISM but did NOT decide the write path (Supabase-
  direct, no .NET in the loop). Verified against PowerSync docs (2026-07-08): the write
  path is developer-defined via uploadData(), can target a custom .NET API; direct-to-
  Postgres is NOT imposed.
- Decision: uploadData() -> .NET API. PowerSync is transport/replication, not a write
  authority. The .NET domain is the single write entry point; online and offline are the
  same command. CQRS explicit: commands via API, reads from local SQLite synced by
  PowerSync (ADR-ARCH-003).
- Story 1 = API segment only (aggregate + write endpoint + GET). No PowerSync client.
- Queue-safety constraint: never a queue-blocking 4xx on validation failure; the full
  rejection protocol is DEFERRED to the offline client slice + conflict-resolution ADR.
- Invariants minimal but NOT anemic (deterministic / always-true / offline-verifiable
  stay on the aggregate).
- GPT review folded in before commit; merged to dev via PR (branch
  docs/adr-arch-005-write-path). Registered in decisions-index.md.

## Endpoint contract (paper)

- POST /constats:
  - body = CreateConstatRequest (DTO, NOT the aggregate). Chain:
    CreateConstatRequest -> CreateConstatCommand -> handler -> Constat.Create().
    Slice: Features/CreateConstat/.
  - siteId travels IN the payload (business data, persisted, historical), validated
    against the tenant site catalogue (ISiteScopeProvider). NOT via X-Site-Id: an offline
    replay batch may carry constats captured under different current sites, so the
    "current" header at sync time is meaningless for a write.
  - observer derived from the JWT identity, never from the body.
  - client-generated ConstatId; idempotent on ConstatId (existing -> 200, not 409);
    success -> 201.
- GET /constats/{id}: read-back (round-trip proof).
- GET /constats: list scoped to currentSite (X-Site-Id) — read/scoping only; optional
  Story 1. (X-Site-Id survives for reads, not for writes.)
- Story 1 stays plain REST (400 on invalid). The 2xx-non-blocking queue semantics is
  deferred (ADR-005) to the offline client slice.

## Id convention — confirmed

- UUIDv7, client-generated, strongly-typed id VO (readonly record struct
  ConstatId(Guid Value)); applies to ALL aggregates. Native .NET 10 (Guid.CreateVersion7,
  RFC 9562), Postgres uuid, JS-generatable. The id is the idempotency key.
- Caveats to validate at persistence wiring (not blockers): no intra-ms monotonic counter
  + endianness quirk (check Npgsql Guid->uuid ordering); client needs a uuidv7 JS lib.
- Recorded in naming.md as the project-wide convention.

## Domain model drawn + frozen

- context/architecture/safety-domain-model.md produced: aggregate, VOs (ConstatType,
  Severity, ObserverSnapshot, Location, Observation, id VOs), invariants, factory,
  boundaries, deferred items, mermaid class diagram.
- GPT re-openings rejected to hold the frozen decisions: ConstatStatus{Recorded,Cancelled}
  (GPT contradicted its own earlier "no status"), Attachments/Evidence now (no Media
  context, no producer), RiskLevel prep (Probability rejected earlier), Location lat/long
  (no GPS producer in Story 1). Adopted from GPT: ObserverSnapshot consolidation, Create()
  factory, invariant list, "Evidence" naming for the future.

## Next / re-entry

Constat is paper-complete (write path, API contract, id, domain model). Next move:
- Step 2: wire Supabase (project + secrets Bitwarden/Doppler + connection + Story A
  GET /context 200/403 verification). First product persistence (DbContext +
  MicroKit.Persistence) depends on it.
- Step 3: Safety schema + Constat aggregate code (Domain -> Application -> Infrastructure
  -> API), mirroring Access/Site hexagonal layout.
- Persistence seam to be detailed at step 2/3, against a real DB, not in the void.

### Re-entry prompt for the web orchestration chat

"On reprend le SaaS BTP. Lis la derniere session
(011-2026-07-09-write-path-adr005-safety-constat-decomposition.md), build-checklist.md et
context/architecture/safety-domain-model.md. Etat : le Constat est COMPLET sur papier —
ADR-ARCH-005 (write path = uploadData -> API .NET, PowerSync = transport) decide et merge ;
aggregate fige (ObserverSnapshot, Severity VO, pas de status, occurredAt client vs createdAt
serveur, Media/Evidence + coords + Cancel deferes) ; contrat endpoint (POST /constats, body
DTO CreateConstatRequest -> Command -> Create(), siteId dans le payload valide contre le
catalogue tenant PAS via X-Site-Id, observer depuis le JWT, idempotent sur ConstatId ->200,
REST simple 400 en Story 1, zero client PowerSync) ; id = UUIDv7 client, VO typee, dans
naming.md ; guardrails anti-corrective/anti-Membership/anti-Media a etendre au module Safety.
Prochain move = cabler Supabase (etape 2 : projet + secrets Bitwarden/Doppler + connexion +
verif Story A /context 200/403), puis schema + code Constat (etape 3, premiere persistance
produit). Cadre-moi avant tout code, une etape a la fois, doc gh/MicroKit verifiee, jamais
--delete-branch quand la head est dev, confirmation avant toute action destructive."

## Open items / debt (carried)

- MicroKit follow-ups from #4 (findings 2-4) — docs/microkit-followups-from-story-4.md.
- Remove the Microsoft.OpenApi 2.10.0 pin once AspNetCore.OpenApi ships a patched
  transitive (NU1903).
- Offline conflict-resolution strategy + domain-rejection response protocol (future ADR).
- Story A live HTTP verification pending a real Supabase token.
- core.hooksPath points to an absent .githooks dir (pre-push guard inactive locally).
- Directory.Packages.props stale header comment (Auth preview.2 vs preview.3 pins).
- Naming: module-Site vs aggregate-Site — revisit at the first real Site aggregate.
- naming.md: add the UUIDv7 client-generated id convention.

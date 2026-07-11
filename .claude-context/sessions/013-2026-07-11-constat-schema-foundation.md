# Session 013 — Constat schema + persistence foundation (Story 1, part 1)

Date: 2026-07-11
Issue: #34 (Story 1 — Constat capture) — still OPEN, branch 2 closes it.
PR: #36 (squash-merged into dev). Branch: feature/safety/constat-schema (deleted).
Refs: ADR-ARCH-005, ADR-ARCH-006, safety-domain-model.md, sql.md, naming.md,
constat-persistence.md.

## What this session did

Closed the last architectural locks before the product's first business persistence, then
implemented and reviewed the write-side foundation. Access (#4) and Story A (#17) are
persistence-free, so Constat carries the first DbContext and the first table — the terrain was
genuinely greenfield.

## Decisions frozen (docs merged before any code)

- **UUIDv7 id convention** (naming.md). Aggregate ids are UUIDv7 in a strongly-typed VO, generated
  by the *creator of the aggregate* — client-side for offline-captured aggregates, where the id
  doubles as the idempotency key. Raw Guid/string is never an identifier in Domain.
  Wording note: "generated client-side" was rejected as too absolute — a future server-created
  aggregate would contradict it.

- **ADR-ARCH-006 — DbUp owns the DDL.** The original framing ("DbUp vs EF migrations") was wrong:
  it is not an ORM choice, it is a *DDL-ownership* choice. EF Core remains the runtime ORM
  (architecture.md already committed to it). DbUp owns schema, RLS, triggers, publications.
  No EF migrations, no Supabase CLI migrations, single journal, dedicated migrator run in
  CI/deploy — never at API startup. Anti-drift integration test is the mandated guardrail.
  Rejected alternatives are recorded in the ADR.

- **sql.md — SQL/DDL conventions** (new file, separate from naming.md: identity/domain vs database
  surface). snake_case, uuid PK, timestamptz UTC, pk_/ck_/fk_/ix_ naming, closed sets as
  varchar+CHECK with snake_case tokens via an *explicit* EF converter, NOT NULL by default,
  audit fields only when a behavior demands them, xmin as the (deferred) optimistic-concurrency
  strategy for mutable aggregates, RLS on every business table.

- **safety-domain-model.md §8 amended.** "Domain references nothing (no MicroKit)" was imprecise.
  Safety.Domain depends on the shared DDD kernel (MicroKit.Domain + MicroKit.Result) — the kernel
  is what the aggregate is built *on*, not an outbound dependency. Infrastructure (EF, ASP.NET,
  MicroKit.Persistence/AspNetCore/Messaging) and other bounded contexts stay forbidden.
  This is the first MicroKit consumption in saas-btp.

- **safety-domain-model.md §10 amended.** "Plain REST (400 on invalid)" was a shorthand, not a
  contract decision. All ConstatErrors are ErrorCategory.Validation, which MicroKit maps to 422 —
  and 422 is semantically right (well-formed JSON, failing business rules). 400 stays for malformed
  JSON, handled upstream by ASP.NET. The kernel wins over the doc; the doc gets corrected.

- **Closed-set tokens stay snake_case end-to-end** (naming.md). The camelCase wire rule governs
  *property names*, not closed-set *values*. Tokens are the persisted CHECK values; a second wire
  representation would require a mapping table for no benefit.

## Schema decisions (0001_safety_create_constats.sql)

- Schema `safety` created JIT with its first table. No pre-created empty schemas for contexts that
  have no table (rejected a proposal to create access/site/media/notifications upfront — YAGNI).
- **No foreign keys, deliberately**: tenant_id (no tenants table; comes from the JWT), site_id
  (Site is config-bound; validated app-side via ISiteScopeProvider), observer_user_id (auth.users is
  GoTrue-managed; app DDL never references the GoTrue schema; the observer is a frozen snapshot,
  not a live reference). Integrity is a domain concern (ADR-ARCH-005).
- **No version / deleted_at / updated_at.** Constat is insert-only: no UPDATE surface, so no
  concurrency to protect. Deferring costs nothing — Postgres exposes `xmin`, usable as an EF
  concurrency token with zero DDL, applied to the first *mutable* aggregate when one exists.
  (Rejected the argument "add version now because other aggregates will change" — speculative.)
- No `created_by`: `observer_user_id` IS the business author, frozen. A technical duplicate would
  be ambiguous.
- `created_at` is app-stamped (IClock), no DB default: the "server" is the .NET app, one source.
- RLS enabled, **no policy yet** — Story 1 has no non-privileged consumer (Data API off; PowerSync
  uses sync rules, not RLS; the domain connects as owner and bypasses). Writing a policy for a path
  that does not exist would be untestable infra.
- PowerSync publication deferred: no PowerSync client in Story 1.
- Index: PK only. The dashboard index (tenant_id, site_id, occurred_at) serves a read path that does
  not exist yet.

## Review gates (three fresh-context prompts — saas-btp has no agents yet)

**Architect** → MERGE WITH FIXES, no blockers. Surfaced: the aggregate validated DateTime *values*
but not their *Kind*, while Npgsql throws on non-UTC into timestamptz — an infra exception where the
factory promises a typed Result. Also: ObserverSnapshot content unvalidated (empty UserId / blank
FullName produced a valid Constat, defeating its proof value).

**Dependency** → MERGE. MicroKit.Domain and MicroKit.Result are *dependency-free*: Domain purity
holds transitively, not just by convention. Preview matrix resolves with no NU1605. It also
corrected the brief: the props sit at the repo root and DO govern tools/ (the migrator was never
outside CPM scope).

**Public API** → MERGE WITH FIXES. The branch-2 handler sketch (the real test of the surface) found
the gap: `record Location(string Description)` does not null-check, so `new Location(null!)` passed
Create and died at SaveChanges — asymmetric with FullName/UserId/ShortDescription, all of which are
gated. Also: the two bare DateTimes are silently transposable.

## Fixes applied from the reviews

- Create rejects non-UTC occurredAt/createdAt (OCCURRED_AT_NOT_UTC / CREATED_AT_NOT_UTC).
  **Reject, never coerce** — converting an Unspecified kind means guessing the field device's
  timezone, unacceptable for a proof-value timestamp.
- Create rejects empty observer UserId and blank FullName.
- Create rejects null Location.Description (LOCATION_DESCRIPTION_REQUIRED). Empty string stays
  valid. Normalizing null → "" in the VO ctor was explicitly refused: same reasoning as UTC.
- Null Observation gets its own code (OBSERVATION_REQUIRED) instead of collapsing into
  SHORT_DESCRIPTION_REQUIRED — the Observer gate already separates container-null from field-invalid.
- Guard ordering preserved: no existing error code changed.

**The rule these fixes serve** (now in Create's XML remarks): every expected-invalid input MUST be
observable through Create's Result. No invalid input may be detected for the first time by
persistence, serialization, or a CLR exception.

## Verification

- `dotnet build -c Release`: 0 warnings, 0 errors.
- 40 domain unit + 25 architecture guardrails + 6 integration = 71 tests green.
- Anti-drift integration test run against **postgres:17-alpine** (matches the Supabase target,
  server_version 17.6): DbUp applies 0001 to a clean container, the real DbContext round-trips every
  column, both token converters, timestamptz UTC — and **UUIDv7 ordering is preserved through the
  Npgsql Guid→uuid mapping**, closing the caveat §6 flagged as "to validate at persistence wiring".

## Open debt (tracked, not fixed)

- **Tenant RLS policy is mandatory before ANY non-privileged consumer** (PostgREST / PowerSync).
  An RLS-enabled table with no policy silently returns zero rows to non-owners — a trap, not an error.
- Anti-drift test is blind to a HasMaxLength(n) vs varchar(n) mismatch (every tested string is short)
  and to a NOT NULL column mapped as optional. Fix: assert EF facet metadata, or round-trip a
  boundary-length value.
- The forbidden-type architecture test is a name-prefix heuristic: `public string Status { get; }` on
  Constat would not be caught (the property's type is System.String). Scope documented, accepted.
- SaasBtp.slnx header comment still says "Empty for now — projects added JIT"; the solution now holds
  ten projects.
- **No CI.** 71 tests, a migrator and a Testcontainers anti-drift suite, and nothing runs them
  automatically. Needs its own issue.

## Carried into branch 2 (closes #34)

- IConstatRepository + EF impl; CreateConstat (DTO → command → handler); POST /constats (idempotent
  on the client ConstatId — replay returns the existing resource, never 409); GET read-back; host
  wiring (AddSafetyModule is defined but never invoked today); ProblemDetails (the empty-body 403
  debt is now actionable — real domain error types exist).
- **Timestamps: the handler converts with `.ToUniversalTime()`, NEVER `DateTime.SpecifyKind()`.**
  SpecifyKind overwrites the kind without converting: 08:00+02:00 would be stored as 08:00Z — a
  silent two-hour corruption of a proof timestamp. An explicit offset or Z is an unambiguous instant
  and converts losslessly; a naked datetime (Kind=Unspecified) has an unknown zone and is passed
  through to the domain, which rejects it. The domain remains the validating authority.
- Pass `occurredAt:` / `createdAt:` as **named arguments** — transposition compiles and the drift
  check usually still passes.
- Clients must send `Z`-suffixed timestamps; state it in the API contract, and cover the wire formats
  (Z / offset / naked) in a round-trip test.

## Environment note (not a project issue)

Docker Desktop's containerd snapshotter ("Use containerd for pulling and storing images") breaks
large image pulls with `failed to copy: httpReadSeeker … EOF`, on any registry. Disabling it fixes
the pull. Unrelated to the product; recorded so the next Docker-blocked session doesn't rediagnose it.

# Session — 2026-07-08 (Story #4 Access: apps/api scaffold + GET /me, green, uncommitted)

Continues: 006-2026-07-06-next-step-access-handoff.md.
Goal: Execute Story #4 (Access) per the Plan-Mode plan — scaffold the minimal
apps/api (.NET 10 modular monolith) carrying the Access module on MicroKit.Auth +
MicroKit.Tenancy with Supabase auth, proven end to end through one protected
endpoint GET /me that resolves the caller's context from JWT claims only. Deliver
green build + architecture tests + unit tests; leave everything uncommitted for a
manual commit; no live Supabase run (needs the owner's real project + secrets).

## What shipped

- apps/api .NET 10 modular monolith, 6 projects attached to SaasBtp.slnx:
  - Host: SaasBtp.Api (thin composition root; wires the Access module + native
    .NET 10 OpenAPI; no business logic).
  - Access module: SaasBtp.Access.Domain / .Application / .Infrastructure
    (Hexagonal: pure center, host-agnostic use case, driving+driven adapters).
  - Tests: SaasBtp.Access.UnitTests, SaasBtp.Architecture.Tests.
- Root scaffolding: global.json (SDK 10.0.107 / rollForward latestMinor, mirrors
  MicroKit), Directory.Build.props (net10.0, Nullable, ImplicitUsings, doc file,
  Release warnings-as-errors), Directory.Packages.props (Central Package
  Management).
- One vertical slice Features/ResolveCurrentContext/ (query + handler + result +
  errors) driving GET /me. Returns { userId, email, tenantId, roles } (org-level)
  from claims only. Tenant is read from ITenantContext (populated by the resolution
  middleware, store-validated), not from the raw claim — so the endpoint exercises
  the full Auth -> Tenancy chain rather than echoing the token.
- Behavior: anonymous -> 401 (RequireAuthorization); authenticated with an unknown/
  forged tenant -> 403 (tenant not resolved); authenticated + valid seeded tenant
  -> 200. No 500 path (the tenancy pipeline returns failures, never throws).

## Scope decisions honored

- Claims-only resolution. NO siteId, no site collection, no membership shape.
- ADR-ARCH-004: no Membership entity/aggregate/repository in Access; enforced by
  architecture rules (anti-Membership + anti-EF), green from day one.
- No EF Core / DbContext / migration. The tenant store is config-bound, not
  persistence.
- No MediatR: a plain handler invoked directly by the endpoint (MicroKit.MediatR
  introduced at the first slice with real commands/domain events).
- Validator omitted for the input-less query (scope-presence enforced as handler
  errors). The slice template stays mandatory for all future input-carrying slices.
- Single-strategy tenancy: only AuthTenantResolutionStrategy is registered
  (AddMicroKitAuthMultitenancy); AddAspNetCoreResolution is NOT called, so the flat
  claims strategy that would fail on every Supabase request is never in the loop.

## Integration trace — MicroKit as the first real product consumer

Story #4 is the first external product consuming MicroKit.Auth + MicroKit.Tenancy
from nuget.org. Five frictions hit; how each was worked around inside SaaS BTP:

| # | What we hit (product side) | Workaround in SaaS BTP |
|---|----------------------------|------------------------|
| 1 | MicroKit.Auth.Supabase preview.2 was un-consumable: SupabaseAuthOptions props were init-only but the only overload is AddMicroKitAuthSupabase(Action<T>), so the documented wiring did not compile (CS8852). | Consume MicroKit.Auth 1.0.0-preview.3 (props made settable); wire the clean o => { o.ProjectUrl/Issuer/Audience } lambda as originally planned. Pinned via Directory.Packages.props. |
| 2 | MicroKit.Auth.Supabase ships no ASP.NET Core authentication scheme — only IJwtValidator (SupabaseJwtValidator). | Hand-wrote SupabaseAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions> in Access.Infrastructure delegating to IJwtValidator (Bearer -> validate -> ticket / NoResult / Fail). This is the bridge the product now carries. |
| 3 | ConfigurationTenantStore needs an awkward appsettings shape: TenantId is record TenantId(Guid Value) with no TypeConverter, so the binder needs a nested "Id": { "Value": "<guid>" }, not a scalar. | appsettings.json seeds the dev tenant with the nested Id.Value form (dev tenant GUID 00000000-0000-0000-0000-000000000001). |
| 4 | An ITenantStore is mandatory even for pure claims resolution: the pipeline resolves the id from the strategy, then calls _store.FindAsync; a store miss is a failure Result -> CurrentTenant stays null -> 403. | Registered UseConfigurationStore() seeded from the "Multitenancy" section. Treated as security-by-design (rejects a forged/unknown tenant_id), not persistence — "no EF in #4" preserved. |
| 5 | Microsoft.AspNetCore.OpenApi 10.0.9 pulls a transitive Microsoft.OpenApi 2.0.0 (NU1903 / GHSA-v5pm-xwqc-g5wc, parser DoS), which fails the Release build under warnings-as-errors. | Pinned Microsoft.OpenApi 2.10.0 (latest patched 2.x) via CPM + a direct PackageReference in SaasBtp.Api; verified /openapi/v1.json still generates. Remove the pin once AspNetCore.OpenApi ships a patched transitive. |

MicroKit-side follow-ups for findings 1-4 (settable-options API test, ship an
ASP.NET Core Supabase scheme, TenantId TypeConverter/IParsable + README notes,
document the mandatory store) are tracked in the MicroKit repo, NOT here — this
trace records only the product-side facts and workarounds.

## Out of scope / deferred (drift guard)

- siteId / site scope from issue #4 is deliberately NOT implemented — deferred by
  ADR-ARCH-004 (membership ownership still open). This session delivers identity +
  tenant + org-level roles only; the "current siteId / scoped to site" half of
  issue #4's acceptance criteria is a future Site story. To be flagged on the PR
  and left open on the issue.
- No other module (Site, Safety, CorrectiveActions, Media, Notifications), no
  MicroKit.Messaging / outbox, no speculative Domain aggregate.

## Verification results (all green)

- dotnet restore: green (MicroKit.Auth preview.3 + MicroKit.Tenancy preview.1 +
  MicroKit.Result preview.2 resolve from public nuget.org; no folder-feed / local
  pack / nuget.config needed).
- dotnet build -c Release (warnings-as-errors): 0 errors, 0 warnings.
- SaasBtp.Architecture.Tests: 9/9 (hexagonal boundaries + anti-EF + anti-Membership
  guardrails).
- SaasBtp.Access.UnitTests: 8/8 (handler x4, Supabase auth handler x4).
- OpenAPI: /openapi/v1.json returns 200 (OpenAPI 3.1.1), /me documented; anonymous
  GET /me -> 401.
- Live Supabase GET /me (200 with a real ES256 JWT; 403 with an unseeded tenant):
  NOT run — owner runs it with the real Supabase project.
- Deviation noted: perf analyzers CA1859/CA1861 are NoWarn in the two TEST projects
  only (test code favors clarity over micro-perf, matching MicroKit's own test
  convention); product projects carry zero suppressions under full
  warnings-as-errors.

## Git state at session end

- Nothing committed. Branch feature/access/scaffold-resolve-current-context, HEAD
  still 1540e08. Working tree: M SaasBtp.slnx; new Directory.Build.props,
  Directory.Packages.props, global.json, apps/api/. Owner creates the branch from
  dev, commits, and pushes manually.

## Next / re-entry

- Owner: commit (single feat(access): covering scaffold + module + slice) + open PR
  base dev, flag the deferred siteId (ADR-ARCH-004) on the PR and issue #4.
- After merge: update build-checklist.md (Step 5.1 done as the first act of #4);
  do NOT close the site-scope half of #4.
- JIT (unchanged from 006): CI path-filters (5.2) and OpenAPI->TS generator (5.3)
  trigger on first real need; btp-* agents still emerge from friction, not on spec.

## Re-entry prompt for the web orchestration chat

Paste this in a new web chat to resume cleanly:

"On reprend le SaaS BTP. Lis la derniere session
(007-2026-07-08-access-scaffold-me.md) et build-checklist.md. Story #4 (Access) est
faite : scaffold apps/api + endpoint protege GET /me (claims-only), vert
(build/arch/unit), committee et mergee sur dev. Le siteId / site-scope de #4 reste
deliberement reporte (ADR-ARCH-004, membership ownership ouvert) ; l'issue #4 reste
ouverte sur cette moitie. Les follow-ups MicroKit issus de #4 (findings 2-4) sont en
attente, a traiter dans le repo MicroKit sur la branche docs/findings-story-4-integration
(liste : docs/microkit-followups-from-story-4.md). Prochaine decision : qu'est-ce qu'on
attaque ? Soit la session findings MicroKit (docs/findings-story-4-integration), soit la
prochaine story SaaS BTP. Cadre-moi le choix avant tout code, une etape a la fois."

## Open items / debt (carried)

- Remove the Microsoft.OpenApi 2.10.0 pin once Microsoft.AspNetCore.OpenApi ships a
  patched transitive.
- Site scoping / membership ownership: future Site story, gated on a membership ADR.
- Offline conflict-resolution strategy for safety data (future ADR).
- Back-button behavior in the installed PWA on Android: validate in the real client.
- Token refresh after long offline sessions (real-client architecture requirement).
- Multi-photo per constat + gallery picker (product UI).
- Cold tooling: Context7 auth, powersync-ja/agent-skills eval, persistent corepack
  in .zshrc.

# ADR-ARCH-011 — Onboarding by invitation: the token is the authorization to exist

Status: accepted
Date: 2026-07-14
Refs: ADR-ARCH-005 (the .NET domain is the write authority; action context in the payload),
ADR-ARCH-006 (DbUp owns the DDL), ADR-ARCH-007 (GoTrue writes app_metadata by a post-INSERT
UPDATE — why no trigger on auth.users can be honest), ADR-ARCH-008 (identity model: profiles,
tenants, memberships), ADR-ARCH-009 (relationship-based authorization over two independent
edges), ADR-ARCH-010 (the site is the authorization grain).
Issues: #48 (this), #55 (existing-identity management), #50 (RLS).

## Context

Since #45 there is NO trigger on `auth.users`. Nothing provisions a business identity, and nothing
guards account creation. Public signup is disabled in the Supabase dashboard as a temporary stopgap.
This ADR replaces the stopgap with a model.

Three facts constrain the design, and each of them kills a naive approach:

1. **Public signup cannot be re-enabled.** It was the trigger's job to decide whether a new
   `auth.users` row was "creating a company" or "joining one" — and a trigger can only tell them apart
   from a client-supplied flag, which is the role-escalation path (ADR-ARCH-007/008). Removing the
   trigger removed the guess; re-opening signup would remove the guard.
2. **The Admin API cannot be the entry point.** Supabase states explicitly that `inviteUserByEmail` is
   built to invite users to an app and NOT for multi-tenant applications, and that they do not plan to
   support multi-tenant invitations — the invite system must be implemented by the application, because
   it depends entirely on how each product structures its tenants. A QHSE consultant working for two
   client companies already has an account; an onboarding path that creates the auth user as its FIRST
   step therefore breaks on the second tenant — exactly the capability epic #44 exists to deliver. The
   identity must be created only when it does not exist, and that is a decision the DOMAIN makes, not
   the Admin API.
3. **A person can be onboarded onto a SITE with no membership in the site's tenant** (ADR-ARCH-009: the
   two edges are independent). A subcontractor or an external inspector is invited to a site by the
   general contractor and never becomes a member of its organization. So onboarding cannot be modelled
   as "joining an organization".

## Decision

**Onboarding is an invitation workflow. The invitation token IS the authorization to exist in the
product.** It replaces, on the correct side of the boundary, what the `auth.users` trigger used to
attempt: the trigger had to GUESS intent from client data; the token is a secret minted by an
authenticated administrator and verified against the database before any account is created.

Onboarding authorization and runtime authorization stay distinct: the TOKEN authorizes entry into the
product (once), the JWT proves identity at runtime (every request), and MEMBERSHIPS authorize action
(per request, ADR-ARCH-009). Three mechanisms, three lifetimes.

### The single account-creation path

`POST /invitations/accept` is the ONLY path in the entire product that creates an `auth.users` row.
The client NEVER calls `signUp()`.

**`createUser` (Admin API), NOT `inviteUserByEmail`.** Verified against the official documentation:
`createUser` sends no e-mail and accepts `email_confirm: true` to mark the address as verified at
creation — which is exactly our case, since the bearer of the token has already proven control of the
address. The invitation e-mail is sent by OUR provider, carrying OUR link, so the account-creation path
and the e-mail path stay decoupled. Given Supabase's own position (Context §2), this ADR is not a
workaround; it is the documented path.

**Public signup stays disabled permanently.** It is not a stopgap until #48 — it is a property of the
model. Re-enabling it would restore an unguarded entry point into the product. Verified: disabling
signup blocks `signUp()` ONLY; existing users keep signing in and the Admin API keeps creating users.
The model is coherent — signup off forever, Admin API the only door.

(`RegisterTenant` is the single exception: the very first account of a new tenant, when no administrator
exists yet to authorize it. Its own guardrails are stated below.)

### Data model — two tables, each total, neither polymorphic

`access.invitations` — the workflow:

    id, email, token_hash, status, expires_at,
    tenant_id, tenant_role NULL,
    invited_by, created_at, accepted_at NULL

- `tenant_id` is **NOT NULL**: every invitation is issued BY a tenant, including one that invites an
  external person. This is what the issuer's authorization is checked against.
- `tenant_role NULL` means **no organization edge will be created** — i.e. the invitee is EXTERNAL. One
  column carries the whole distinction. Externality remains a relational fact (ADR-ARCH-009), not a
  role: no `sub_contractor` token, ever.
- `token_hash`, never the token. See the security section.
- `expires_at` is **NOT NULL**: every invitation expires. Default validity **7 days**, held in
  CONFIGURATION, not hard-coded — it is a product parameter, not an architectural constant. (24h, the
  Supabase default, is too short for the field: a site manager travelling between chantiers does not
  read e-mail daily. We do not use Supabase's invite links anyway — this is our token, our table, our
  rule.) Like every timestamp, it is domain-stamped: no `DEFAULT now() + interval` in the DDL
  (ADR-ARCH-005).
- `status IN ('pending', 'accepted', 'revoked')`. **`expired` is NOT a stored status: it is DERIVED from
  `expires_at < now()`.** Storing it would create a second source of truth for a fact the first one
  already carries, and would need a job to flip it — two states that can silently disagree. A status
  stores only what cannot be deduced.
- An expired or revoked invitation is **never deleted**. It is the audit trail: "Jean invited Paul on
  3 March; Paul never answered."

`access.invitation_sites` — 0..N site edges, one row per site:

    invitation_id, site_id, site_role, valid_from, valid_until NULL

Every column NOT NULL except `valid_until`. The shape mirrors `site.site_memberships` exactly, so
acceptance is a COPY, not a translation.

An invitation MAY grant access to SEVERAL sites at once (0..N): a site assignment is an edge COLLECTION,
not part of the invitation's identity. Inviting a plumber onto Cocody, Plateau and Riviera is one
invitation, one e-mail, one transaction.

**No `invitation_grants` polymorphic table.** A nullable `(tenant_id, site_id, tenant_role, site_role)`
quadruple with a `CHECK (tenant_id IS NOT NULL OR site_id IS NOT NULL)` guards nothing that matters: it
permits `tenant_id` with `site_role`, `site_id` with `tenant_role`, and both at once. The real invariant
would then live only in C#, unverifiable in the database — the failure mode this project has been
correcting since #45 ("the constraint is the guard, not the comment"). An organization edge and a site
edge are different SHAPES, not two instances of an abstract one. When a genuinely new perimeter exists,
it gets its own typed table (append-only), not a nullable column in a generic one.

### The two commands

**`CreateInvitation`** — issued by an authenticated administrator.

- Organization edge (`tenant_role` set) → the issuer must hold an active `owner`/`admin` membership in
  `tenant_id`.
- Each site in `invitation_sites` → the issuer must hold `site_role = 'site_manager'` on that site, OR a
  tenant role with cross-site scope (today `owner`).
  **PARTICIPATION IS NOT PERMISSION TO ADMINISTER.** A `member` on a site — a subcontractor, for
  instance — passes `CanActInSiteScopeAsync` and must NOT be able to invite people onto the general
  contractor's site. Conflating "may act in the scope" with "may manage the membership" is an escalation
  path of the same family #45 spent itself closing. This is a PERMISSION, resolved in C# from the role
  held on the edge (ADR-ARCH-009) — NOT a new port on `ISiteScopeProvider`.
- The role is set BY THE SERVER, from the issuer's request, and is never read from the invitee. A request
  that tries to set its own role is rejected.

**`AcceptInvitation`** — ONE domain operation. The business writes are transactional; the GoTrue account
creation is an EXTERNAL SIDE EFFECT and cannot be inside the transaction.

    1. lock the invitation row (FOR UPDATE)
       -> status = 'pending'? expires_at > now()? otherwise fail
    2. LOOK THE ACCOUNT UP FIRST (Admin API, by e-mail)
    3. if absent -> createUser (email_confirm = true)        [EXTERNAL, non-transactional]
    4. THE GOLDEN RULE: the authenticated e-mail must match the invited e-mail, else 403
    5. POSTGRES TRANSACTION:
         INSERT access.profiles                (if the person is new)
         INSERT access.memberships             (if tenant_role IS NOT NULL, is_active = true)
         INSERT site.site_memberships          (one per invitation_sites row)
         UPDATE invitations SET status = 'accepted', accepted_at = <domain-stamped>
       COMMIT

**There is NO distributed transaction, and none is needed.** GoTrue is an external HTTP service; a
Postgres `ROLLBACK` cannot undo an `auth.users` row already created. If step 5 fails after step 3
succeeded, the account exists with NO business identity and the invitation stays `pending`.

**This is a TOLERABLE transient state, and it self-heals — no compensation, no saga.** Step 2 looks the
account up BEFORE creating it, so a retry FINDS the orphan and does not recreate it. Read-before-write
IS the mechanism. And by ADR-ARCH-008 an account with no business identity can do nothing: it holds a
valid token, no `observer_*` claim, no membership. The model already absorbs this state — here it is
merely reached by a failed retry instead of by design.

**Do NOT add compensation logic** (deleting the orphaned `auth.users` row on failure). It would race with
a concurrent retry and delete an account that is about to be used.

**`AcceptInvitation` is IDEMPOTENT.** A repeated acceptance (double click, retried request, unstable
network) must return the SAME resulting edges — never a duplicate, never an error. The database already
guarantees it structurally: `ux_memberships__tenant_id_user_id` and
`ux_site_memberships__site_id_user_id` make a second insert impossible. The command reads the
already-`accepted` invitation and returns its edges. Same discipline as the idempotent POST on
`ConstatId`.

`is_active = true` on acceptance. There is **no `pending` state on the membership**: "not yet accepted"
lives in `access.invitations`, where it has a meaning. It must not pollute the authorization edge, where
`is_active = false` already means "revoked" — two different facts must not share one column (the
ADR-ARCH-009 discipline).

The profile must exist BEFORE the invitee's first authenticated call that requires business claims. The
JWT hook stamps `observer_name` only when a profile row exists (0003), so an invitee whose profile
arrived late would hold a valid token that cannot create a constat — failing opaquely. Creating the
profile inside the acceptance transaction closes that window structurally. (Exactly when the hook runs
relative to account creation is listed under Open — it is to be verified, not assumed.)

### Tenant provisioning — an OPERATOR operation, not a product endpoint

The first account of a new tenant has, by definition, nobody to authorize it. Rather than build a
self-service registration path with its full anti-abuse surface (rate limiting, captcha, e-mail
verification, duplicate handling, support), **Phase 1 does not expose self-service tenant
registration at all.**

Tenant creation is an ADMINISTRATIVE operation performed by the operator. It creates, in one
transaction: the tenant, the initial owner's identity (`auth.users` via `createUser` + profile), and
the `owner` membership. It is then followed by an invitation to that owner.

**This is NOT a public API.** There is no `POST /tenants/register` and no `POST /tenants/provision`
reachable by an anonymous caller. Anyone reading this ADR and building such an endpoint has
misread it.

Rationale: the product targets a single client for market entry, with accompanied onboarding. A
public registration flow would be pure cost for a capability nobody needs. Exposing self-service
tenant registration later is a PRODUCT decision, and it requires its own ADR — because it reopens the
one guarded entry point this ADR exists to protect.

### Security invariants (non-negotiable)

- **The token is a cryptographic SECRET, not an identifier.** 32 bytes from a CSPRNG, base64url-encoded
  for the URL; the database stores its SHA-256. **Never a UUID** — a UUID is an identifier, not a secret,
  and UUIDv7 is partially predictable from its embedded timestamp.
- **The database stores `token_hash`, never the token.** A dump of `access.invitations` must not yield a
  single usable invitation. The clear token exists only in the e-mail.
- **The acting identity comes from the JWT (`sub`), never from the request body.** Same rule as the
  observer on a constat (safety-domain-model).
- **The invited e-mail must match the authenticated e-mail.** Without it, anyone holding a leaked token
  could consume an invitation addressed to someone else with their own account.
- **Roles are server-imposed.** No role value is ever read from the invitee's request.
- **Expiry is enforced in the domain**, not by a DB `DEFAULT` (ADR-ARCH-005).

## Consequences

- The product has exactly ONE way in. Every `auth.users` row is traceable to an invitation (`invited_by`,
  `created_at`) or to a tenant registration. That is an audit property, not a side effect.
- A person invited by two tenants holds ONE `auth.users` row, ONE profile, and two edges. Multi-tenant
  works by construction rather than by exception — which was the point of epic #44.
- Invitations carry SITES, never lots (ADR-ARCH-010). This holds whichever way the open question in
  ADR-ARCH-010 is answered, so #48 is not blocked by it.
- **The invitation is an INDIRECT writer of the site edge, and only at acceptance time.** It does NOT
  remove the need for a DIRECT writer for people who already have an account: assigning an existing
  employee to a second site must not e-mail them "create your account". That is `AssignSiteMember` (#55).
  Do not conclude from this ADR that #55 is redundant.
- An `auth.users` row with no business identity may exist TRANSIENTLY (a created account whose acceptance
  transaction failed). It is harmless by ADR-ARCH-008 and is recovered by the next acceptance attempt.
  **It is not an incident.**
- UX consequence to design for, not to discover: an invitee who ALREADY has an account (because they work
  for another tenant) must be told to sign in before accepting. The invitation landing page branches on
  "account exists / does not exist" — the verify endpoint returns that fact.
- E-mail delivery becomes a production dependency of the onboarding path. Provider choice is not settled
  here.

## Alternatives considered

- **`inviteUserByEmail` as the entry point (the original #48 scope).** Rejected: Supabase itself states it
  is not built for multi-tenant applications, and it fails on an existing e-mail — breaking the second
  tenant, the exact capability epic #44 delivers.
- **Re-introducing a trigger on `auth.users` to provision the profile.** Rejected in ADR-ARCH-008 and
  reaffirmed: the trigger must guess intent from a client flag, which is the escalation path. The token
  does not guess — it is verified.
- **`signUp()` on the client, then a "join with code" step.** Rejected: it requires public signup, which
  re-opens the unguarded entry point.
- **A `pending` token on `access.memberships` instead of an invitation table.** Rejected: it makes "not
  yet accepted" and "revoked" indistinguishable on the authorization edge, and it gives the invitation no
  home for its token, expiry, or audit trail.
- **A polymorphic `invitation_grants` table.** Rejected above.
- **A UUID as the invitation token.** Rejected: an identifier is not a secret.
- **`expired` as a stored status.** Rejected: it is derivable from `expires_at`, and storing it needs a job
  to keep it true.
- **Compensating the orphaned `auth.users` row on failure.** Rejected: read-before-write already recovers
  it, and a delete would race with a concurrent retry.

## Open

- **`createUser` behaviour on an EXISTING e-mail.** The documentation does not state it. MUST be tested
  against the real Supabase project before implementation — assumed to fail, which is why the command
  looks the account up FIRST and only creates it when absent. Do not assume; this project has been burned
  by GoTrue before (ADR-ARCH-007).
- **JWT hook timing on first sign-in.** The profile is created inside the acceptance transaction, so it
  exists before any sign-in. To be confirmed on the real project, not assumed.
- **Admin API authentication from .NET.** A reported bug shows `auth.admin.createUser` returning
  `403 bad_jwt` with the new `sb_secret_` keys via supabase-js. We call GoTrue over HTTP from .NET, so
  likely unaffected — but it is the same family of trap already met on this project (new API keys go on
  the `apikey` header only). Verify on the first call.
- **E-mail provider.** Not settled.
- ~~`RegisterTenant` exposure model~~ — **CLOSED**: no self-service in Phase 1. Tenant creation is an
  operator provisioning operation (see above). A public self-service flow requires a future ADR.

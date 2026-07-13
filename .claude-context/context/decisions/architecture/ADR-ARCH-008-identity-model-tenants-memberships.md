# ADR-ARCH-008 — Identity model: profiles as human projection, tenants + memberships as the authorization bridge

Status: accepted
Date: 2026-07-13
Supersedes: ADR-ARCH-004 (Ownership of Membership deferred)
Refs: ADR-ARCH-005 (.NET domain = write authority; action context in the payload), ADR-ARCH-006
(DbUp owns the DDL), ADR-ARCH-007 (GoTrue writes app_metadata by a post-INSERT UPDATE — trigger
design rule), migrations 0002_access_identity_model (portable: profiles + tenants + memberships) and
0003_access_identity_auth (auth-coupled: the supabase_auth_admin grant + SELECT policy + the JWT
hook), issues #45 (this), #48 (InviteMemberCommand), #49 (.NET tenant resolution repoint),
#50 (RLS policies).

## Context

The earlier model (ADR-ARCH-004 era, shipped in #39/#40) made the tenant part of a user's
identity: `access.profiles` carried a `tenant_id`, a signup trigger provisioned that row from
GoTrue metadata, and the JWT hook copied `tenant_id` into `app_metadata` as a static claim.
ADR-ARCH-004 deliberately DEFERRED where "membership" (user <-> tenant/site <-> role) would live,
so no membership model existed yet.

Three problems forced a decision now, before any real member/role stories are built on top of the
deferred design:

- One tenant per profile is wrong. A person can work for more than one company over time (and, for
  a QHSE consultant, at once). Binding the human's identity row to a single tenant makes the second
  tenant unrepresentable without duplicating the human.
- Tenant-in-the-JWT means revocation waits for token expiry. Removing someone from a tenant should
  take effect on the next request, not at the next refresh.
- Provisioning at the auth boundary cannot stay honest. The earlier signup trigger (the since-removed
  0004/0006 provisioning, per ADR-ARCH-007) had to guess intent: "this new auth user is creating a
  company" vs "joining an
  existing one" are different operations with different authorization, and a trigger can only tell
  them apart from a client-supplied flag. Branching provisioning on a client flag is precisely the
  role-escalation path.

## Decision

Split one conflated concept — "identity" — into three, and give each its own home.

- `access.profiles` is the pure 1:1 human projection of `auth.users`: WHO the person is (email,
  full_name, optional given/family name and job_function). NO tenant, NO role. It answers "who is
  this human", nothing about access. (0002 declares this final shape directly — no `tenant_id`, with
  `email` + given/family name parts + `job_function`.)
- `access.tenants` + `access.memberships` are the ONLY authorization bridge. A membership is a row
  "user U belongs to tenant T with role R", carrying `is_active`. A user's tenants and roles are
  DATA, queried per request — never token claims. Revocation is immediate: flip `is_active`.
- The JWT is proof of IDENTITY only: `sub`, `email`, and the observer claims (`observer_name`,
  `observer_function`) that Safety stamps onto a constat. It carries NO tenant, ever. (0003 defines an
  identity-only hook that reads only `full_name` + `job_function`; there is no trigger on `auth.users`.)
- Action context (tenantId, siteId) travels in the command PAYLOAD, never in a header and never in
  the token (ADR-ARCH-005): the offline client generates commands that replay verbatim, so the
  acting tenant/site must be part of the command, not ambient request state.
- No trigger on `auth.users`. Onboarding is an explicit application operation: `InviteMemberCommand`
  (#48) creates the profile + membership, and can distinguish "create a company" from "join one"
  because it is a domain command with an authenticated caller, not a blind INSERT trigger.

### Deliberate break — "1 auth user = 1 profile" no longer holds

This is a decision, not a side effect. AUTHENTICATION NO LONGER IMPLIES ONBOARDING. A user can exist
in `auth.users`, sign in, and hold a valid token while having NO business identity: no profile, no
tenant, no membership. Authentication, business identity, and authorization are now THREE distinct
concepts:

- Authentication (GoTrue / `auth.users`): the person can prove who they are.
- Business identity (`access.profiles`): the product knows who they are.
- Authorization (`access.memberships`): the person may act within a tenant.

A valid token proves identity; it does NOT prove access to the product. Only a membership does, and
the .NET domain is what rules on it.

## Consequences

- Easier: multi-tenant membership, immediate revocation, and honest onboarding (an explicit command
  that authorizes, instead of a trigger that guesses).
- Easier: `access.profiles` stops being a mirror of GoTrue that must be kept in sync; it is captured
  once at invitation. `email` is a SNAPSHOT — `auth.users.email` stays AUTHORITATIVE, GoTrue permits an
  email change with no resync, so it WILL drift; accepted (documented in 0002). Its one consumer is the
  tenant member-list read path (`memberships -> profiles`), which cannot reach `auth.users`; nothing
  else reads it (the JWT carries `email` natively). `ux_profiles__email` is a LOCAL invariant against
  two profile rows sharing an e-mail identity — it does NOT guard the drift and must not be read as if
  it did.
- Harder / must be enforced downstream (#49): every scoped path must now handle "authenticated but
  no business identity". Corollaries, stated so they are not rediscovered as bugs:
  - `/me` and every scoped READ must REFUSE a user with no profile, with an explicit error — never a
    silent fallback, never a profile created on the fly. (Enforcement is #49.)
  - The main WRITE path is already structurally guarded: `safety.constats.observer_full_name` is
    NOT NULL and frozen, sourced from the `observer_name` JWT claim, which the hook stamps ONLY when
    a profile row exists. A profile-less user therefore cannot create a constat — the command fails
    in the domain. This is a property of the model, not a bolt-on check.
  - The JWT hook degrading silently (no profile -> event returned unchanged, no log, no
    `observer_*`) is CORRECT under this model. It is not a missing guard.
- Superseded: ADR-ARCH-004's "Access owns no membership" guardrail is lifted — Access now owns
  `tenants` + `memberships` (the org-level authorization bridge). Site-level assignment (a site
  membership + operational role) remains a separate, later concern under the Site context; this ADR
  does not annex it.
- ADR-ARCH-007 remains factually true (GoTrue still writes `app_metadata` by a post-INSERT UPDATE),
  but it no longer has a live subject: with no trigger on `auth.users`, nothing in our schema reacts
  to that UPDATE anymore.

## Rebaseline (squash of 0002-0006)

This model was NOT migrated forward with `ALTER`/`DROP IF EXISTS` scripts. The old Access scripts
(`0002_access_create_profiles` through `0006_access_profiles_provisioning_fix`) were DELETED and
replaced by two scripts that DECLARE the final shape directly: `0002_access_identity_model` (portable)
and `0003_access_identity_auth` (auth-coupled).

This is safe here and only here: `access.profiles` is empty, the app is not deployed, and the single
`auth.users` account is disposable — there is no data to protect. The append-only migration rule exists
to protect DATA, not script history; with no data, a squash loses nothing. The reset window (drop the
`access` schema, delete the journal rows for 0002-0006, re-run 0002/0003) is used exactly once and then
closes: `0001_safety_create_constats` is untouched, and from this baseline forward the migration set is
append-only again. The human-run reset sequence is documented in the validation checklist.

## Alternatives considered

- Keep tenant in the profile, add a second table only for extra tenants. Rejected: keeps the
  privileged "primary tenant" as identity, so revocation of the primary and the multi-tenant model
  stay inconsistent; two mechanisms for one relation.
- Keep tenant as a JWT claim (array of memberships in the token). Rejected: revocation still waits
  for token expiry, and the token grows with membership; authorization belongs in queryable data the
  domain evaluates per request, not in a snapshot minted at login.
- Keep the signup trigger, add a client `intent` flag to choose create-company vs join. Rejected:
  authorization driven by a client-supplied flag at the auth boundary is the escalation path the
  trigger design was already straining against (ADR-ARCH-007); onboarding is a domain command
  (#48), not a database trigger.

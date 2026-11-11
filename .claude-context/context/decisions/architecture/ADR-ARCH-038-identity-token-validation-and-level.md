# ADR-ARCH-038 — The API validates the provider's access token itself and reads the confirmed-contact level from a claim of that token

Status: accepted
Date: 2026-11-11
Refs: issue #200; parent #163 (BP-002); BP-001 (`docs/business-processes/BP-001-found-company/`);
ADR-ARCH-018; ADR-ARCH-019; ADR-ARCH-025; ADR-ARCH-026; ADR-ARCH-027; ADR-ARCH-028;
ADR-ARCH-031; ADR-ARCH-032; ADR-ARCH-034; ADR-ARCH-035; ADR-ARCH-037; ADR-ORG-004.

## Context
BP-001's contract leaves three questions to the identity ADR of BP-002 (`contract.md` §9): how
the API validates a token, where it reads the "confirmed contact" level that founding requires
(`process.md`, BP001-D1), and how fresh that level is (`contract.md` §4). Seven host proofs
wait for it (`contract.md` §7: BP001-AC-06 to BP001-AC-11, BP001-AC-23). The contract already
fixes the answers: 401 for no caller or a rejected token, with no detail; 403 only for the
level; checks in the order authentication, body (400, 422), level, unit of work
(`contract.md` §2).

The person is external to the product and known by the provider's user identifier
(ADR-ARCH-031 §2). The API sets that identifier in `app.user_id` from a token it has validated,
and `saasbtp_api` holds no grant on `auth` (ADR-ARCH-032 §3). The API never writes to the
identity provider (`contract.md` §4). The stack uses Supabase, Auth included (ADR-ARCH-026 for
staging, on the Free plan); no ADR records it as BP-002's provider, and choosing one is outside
this decision (#163, Out of scope). This ADR validates the tokens of the provider in use today
and keeps what depends on it small.

What the providers document, read on 2026-11-11:

- Supabase signs access tokens with the project's key "in use", "the current key": an
  asymmetric key, ES256 or RS256, or the legacy shared secret, which is "No longer recommended.
  Available for backward compatibility." Ed25519 (`EdDSA`) is listed as "Coming soon". The JWKS
  holds "only their public keys" of asymmetric keys, and "does not return any keys if you are
  not using asymmetric JWT signing keys". The Management API lists each key of a project with
  its `algorithm` and `status` (`in_use` in its example) at
  `GET /v1/projects/{ref}/config/auth/signing-keys`. Keys are published at
  `https://<project-ref>.supabase.co/auth/v1/.well-known/jwks.json`; "This discovery endpoint is
  cached by Supabase's edge servers for 10 minutes", and an application should "not cache this
  data for longer". A key is rotated by adding a standby key, rotating (both stay trusted), then
  revoking; Supabase recommends "waiting at least 20 minutes when creating a standby signing
  key, or revoking a previously used key". No page read documents an OpenID Connect discovery
  document, and none says which algorithm a new project gets.
- A user's access token carries `iss` (`https://<project-ref>.supabase.co/auth/v1`), `aud`
  (`authenticated`), `sub` ("The user ID (UUID)"), `exp`, `iat`, `role`, `aal`, `session_id`,
  `email`, `phone` and `is_anonymous`. No claim states whether a contact is confirmed. The
  default lifetime is one hour; "Setting a value over 1 hour is generally discouraged".
- The user record carries `email_confirmed_at` and `phone_confirmed_at` ("If null, it means
  that the user's email is not confirmed", and the same for the phone). "Having Confirm Email
  disabled ... implicitly confirms the user's email in the database". The project settings
  `auth.email.enable_confirmations` and `auth.sms.enable_confirmations` make users confirm the
  e-mail address or the phone number "before signing in".
- `GET /auth/v1/user`, which returns that record, shares the "Sign-ups and sign-ins" limit with
  sign-up, recovery and one-time codes. That limit applies per IP address, 30 requests per 5
  minutes by default, and can be customized. Forwarding the end user's address
  (`Sb-Forwarded-For`) must be enabled explicitly for new projects and requires a secret key,
  and secret keys "provide full access to your project's data, bypassing Row Level Security".
- A custom access token hook "runs before a token is issued", token refreshes included, and may
  add claims; a top-level claim is the documented pattern (`user_role`). A Postgres hook is
  executed by `supabase_auth_admin`, which needs `execute` on the function and `usage` on its
  schema; the guide recommends against `security definer` and recommends revoking the function
  from `anon`, `authenticated` and `public`. A Postgres hook has 2 seconds, and its error fails
  the sign-in. The Custom Access Token hook is available on the Free and Pro plans.
- ASP.NET Core 10 validates a JWT bearer with `JwtBearerOptions`. A token failing signature,
  issuer, audience or expiry is answered 401, and `IncludeErrorDetails`, on by default, adds the
  reason to `WWW-Authenticate`. A scheme's forbid action "is called by Authorization when an
  authenticated user attempts to access a resource they're not permitted to access", and for "A
  JWT bearer scheme" it returns 403; an `IAuthorizationMiddlewareResultHandler` can replace the
  default challenge or forbid answer. `TokenValidationParameters.DefaultClockSkew` is 300 seconds.
  The `ConfigurationManager<T>` that holds signing keys refreshes them every 12 hours by default
  and allows a requested refresh at most every 5 minutes by default. `RefreshOnIssuerKeyNotFound`,
  on by default, requests one when a key is unknown. "If the configuration has been retrieved,
  but cannot be updated, then the exception will be logged and the current configuration will
  be returned." When an exception reaches `JwtBearerHandler` (`dotnet/aspnetcore`, `main`, read
  2026-11-11), it rethrows unless an `AuthenticationFailed` handler sets a result.
- The repository's `sql.md` ("Auth-coupled scripts") keeps a script that names a GoTrue role
  out of the Testcontainers run. It validates such a script against a real Supabase project by
  a checklist, "never by faking `auth.users` / `supabase_auth_admin`". Its authority was the
  removed ADR-ARCH-005.

## Decision
1. **What the host accepts.** The API host authenticates a request as a JWT bearer with the
   ASP.NET Core handler. It accepts a token only when all of the following hold, and refuses it
   otherwise:
   - It is signed with a key from the provider's JWKS, with an algorithm in the allow-list
     ES256, RS256. An unsigned token and an HS256 token are refused; the API holds no signing
     secret. This requires each project's key in use to be an asymmetric ES256 or RS256 key:
     with the legacy secret in use the JWKS holds no key and every token is refused, and with
     another algorithm every token is refused. That key is therefore a setting of this
     decision, checked with those of decision 5 (Consequences).
   - `iss` equals the configured issuer, `https://<project-ref>.supabase.co/auth/v1` for the
     project of the environment.
   - `aud` equals the configured audience, `authenticated`.
   - `exp` is present and not past, with a clock skew of 30 seconds.
   - `iat` is present, and `exp` is at most one hour after it. The host enforces Supabase's
     default lifetime instead of trusting the project's setting.
   - `sub` is a UUID.
   - `is_anonymous` is not `true`. No process defines an anonymous person; until BP-002 does,
     an anonymous session is not a caller.

   Authentication is the fallback policy of every endpoint. The anonymous endpoints say so
   explicitly with `AllowAnonymous`: the health endpoints, as today (ADR-ARCH-018), and
   `GET /api/v1/ping`, which stays anonymous. Authentication runs before the body is read, so a
   request without a valid token is answered 401 whatever its body.
2. **Keys and their rotation.** The host reads the signing keys from the JWKS address directly,
   not from a discovery document. It does so through the handler's `ConfigurationManager`, with a
   retriever of its own that turns the JWKS into the signing keys, so that the handler's refresh
   rules apply. Keys set once at startup, or fetched outside the configuration manager, are
   not used. The host refreshes the keys every 10 minutes (`AutomaticRefreshInterval`), and on a
   token signed by a key it does not hold, at most once every 5 minutes (`RefreshInterval`).
   A standby key published 20 minutes before it signs is held before its first token. A revoked
   key leaves the host's keys at the first successful refresh after Supabase's edge cache drops
   it, about 20 minutes after revocation.
   Before the first successful fetch, no token is accepted. The request is answered 500, the
   handler's rethrow left as it is, and nothing is written: the host could not validate the
   token, which does not make it invalid (`contract.md` §2, "any other failure"). After the
   first fetch, the last keys fetched stay in use while a refresh fails, so a key revoked
   during an outage of the JWKS stays trusted until a fetch succeeds.
3. **The subject.** The subject is `sub`, read without claim mapping (`MapInboundClaims` off).
   It is the provider's user identifier (ADR-ARCH-031 §2) and the only value the API sets in
   `app.user_id` (ADR-ARCH-032 §3).
4. **401.** No token, or a token that fails a check of decision 1, is answered 401 with a bare
   `WWW-Authenticate: Bearer` challenge: `IncludeErrorDetails` is off, so the answer reveals
   nothing (`contract.md` §2). The host logs the reason internally, as ADR-ARCH-032 §5 does for
   other refusals.
5. **The confirmed-contact level.** BP-002 defines what a confirmed contact is. This ADR fixes
   how the provider's attestation of it reaches the API: the provider attests that at least one
   contact of the person, an e-mail address or a phone number, is confirmed.
   - **What states it.** One boolean claim of the access token, `contact_confirmed`. The
     validated token is the only source; the API calls the provider for nothing.
   - **Who sets it.** For Supabase, a custom access token hook, a Postgres function executed by
     `supabase_auth_admin`, not `security definer`. It sets the claim to `true` when the user is
     not anonymous and `email_confirmed_at` or `phone_confirmed_at` is set, and to `false`
     otherwise, overwriting any claim of that name. It reads the provider's own user record and
     nothing else.
   - **Which confirmations count.** A confirmation counts whatever path of the provider set it:
     a code or a link the provider sent; an upstream identity provider reporting the e-mail
     address verified, if BP-002 enables one; the project's operator, through the provider's
     administration. Which of these paths exist is BP-002's and the operator's; the API holds
     no key that could set one.
   - **The settings it depends on.** The timestamps mean a confirmation only while the project
     requires both confirmations, and the claim exists only while the hook is enabled.
     Three settings of each project are therefore part of this decision:
     - e-mail confirmations required;
     - phone confirmations required;
     - the custom access token hook enabled, pointing at the versioned function.

     A disabled hook fails closed: the claim is absent, and every founding is refused. A
     disabled confirmation fails open: the provider confirms users implicitly. The settings are
     checked, so that a change is detected rather than trusted (Consequences).
   - **Where the API reads it.** A module never reads a claim. It reads the caller's subject
     and whether they have a confirmed contact through a port of its Features project
     (`Ports/`, ADR-ARCH-034 decision 1). Its Adapters project implements the port from the
     principal the host authenticated, from `sub` and `contact_confirmed` alone.
6. **Freshness.** A token states the level at its issuance; the hook runs again at every
   refresh. An operation that requires a level therefore accepts only a recent token. Its `iat`
   must be at most 60 seconds before the API processes the request, or 90 seconds with the
   skew. An older token, valid otherwise, is answered 401 for that operation, never 403. The
   check runs in the authorization step, before the body is read, so the order of `contract.md`
   §2 holds. There the JWT bearer scheme would forbid the authenticated caller with 403, which
   `contract.md` §2 reserves for the level. The host therefore produces this 401 explicitly, as
   a challenge with the bare `Bearer` header of decision 4, in place of the default forbid. The
   client refreshes its session and submits again, as it already does on 401
   (`contract.md` §6); the server's refusal, not the client, guarantees the bound. Every other
   operation accepts a token for its whole lifetime (decision 1).

   `contract.md` §4 leaves this freshness to this ADR. The level the founding reads is
   therefore the one at most 90 seconds before the request is processed (TSK-2's
   precondition, BP001-D1). For BP001-AC-10:
   - a request kept while the founder lost the level is submitted after the connection
     returns. A token held through that wait is refused 401 once it is older than the bound;
   - the refreshed token states the level as it is then, and the founding is refused 403.

   What is not seen is a loss within the 90 seconds before processing, the time a refreshed
   token takes to reach the API.
7. **403.** A missing `contact_confirmed`, or one that is not `true`, does not meet BP001-D1,
   and the founding answers 403 (`contract.md` §2). The founding operation checks it after the
   body (400, 422), in the order of `contract.md` §2. It is not an authorization policy of the
   endpoint, which would answer before the body is read.
8. **What depends on the provider.** Only these:
   - the issuer, the audience and the JWKS address: configuration of each environment, not
     secrets;
   - the signing key in use (decision 1), the hook that sets `contact_confirmed`, and the three
     settings of decision 5.

   The claims the API reads (`sub`, `contact_confirmed`, `is_anonymous`), their meaning, the
   answers, the domain and the processes do not depend on it. Another provider fits when it
   issues an asymmetrically signed token with a UUID subject and these claims; otherwise a new
   ADR changes the reader. Channels, recovery, a stronger factor (`aal`) and anonymous sign-in
   stay BP-002's.
9. **What the host checks for BP-001.** These are the host proofs `contract.md` §7 marks
   "pending the identity ADR". Each runs on the production validation path, configured with a
   test issuer, audience and JWKS address. The test signs its own tokens with a test ES256 key
   published by an in-process JWKS. No test calls the provider. A founding token is recent
   (decision 6) unless a case says otherwise.
   - **BP001-AC-11.** Each of the following is answered 401 with nothing written:
     - no token, and a malformed token;
     - an unsigned token, an HS256 token, a bad signature, and an unknown key;
     - another issuer, another audience, a token without `exp`, a token without `iat`, a token
       whose `exp` is more than one hour after its `iat`, and an expired token beyond the skew;
     - a `sub` that is not a UUID, and an anonymous token;
     - no token with a body that would be answered 422, which proves that authentication comes
       first.
   - **BP001-AC-07.** With a valid token holding the level, an empty company name, and a company
     name made only of white space, are each answered 422 naming `organizationName`, with
     nothing written.
   - **BP001-AC-08.** With a valid token holding the level, an empty workspace name is answered
     422 naming `workspaceName`, with nothing written.
   - **Order of the level check.** For BP001-AC-07 and BP001-AC-08, the same bodies with a token
     without the level are also answered 422. This proves that the body is checked before the
     level.
   - **BP001-AC-09.** A valid token whose `contact_confirmed` is `false`, and one without it,
     are each answered 403, with nothing written.
   - **BP001-AC-10.** One request is sent five times, giving exactly one organization:
     - first with a token holding the level but issued more than 90 seconds earlier, as the
       token held through the wait: 401, not 403, nothing written;
     - then with a recent token without the level: 403, nothing written;
     - then with a recent token holding it: 201;
     - then again: 200;
     - then with a recent token without the level: 403, not 200, which proves that the level is
       checked before the unit of work.
   - **BP001-AC-06, BP001-AC-23.** These host proofs need a recent, valid token holding the
     level, and nothing else from this ADR.

   The other behaviours this ADR decides are each checked too, in the host's tests unless
   stated otherwise:
   - **Boundaries.**
     - A token expired 29 seconds earlier is accepted, and one expired 31 seconds earlier is
       refused.
     - A token whose `exp` is exactly one hour after its `iat` is accepted, and one a second
       longer is refused.
     - For the founding, a token issued 89 seconds earlier is accepted, and one issued 91
       seconds earlier is refused 401, not 403, with a bare `Bearer` challenge.
   - **The fallback policy.**
     - An endpoint other than the founding that declares no authorization of its own (one the
       test maps, if the host has none yet) answers 401 to a request without a token.
     - Each health endpoint and `GET /api/v1/ping` answers a request without a token as it
       does today, without 401.
   - **Keys.** These run against the in-process JWKS:
     - a key added after start is accepted after the refresh on an unknown key;
     - a key removed is refused after the automatic refresh;
     - a failed refresh keeps the last keys;
     - with no key ever fetched, a request is answered 500 and nothing is written.
   - **401.** It carries a bare `Bearer` challenge, with neither `error` nor
     `error_description`.
   - **The ports.** In each module's Adapters tests, the subject comes from `sub`, and the level
     is held only when `contact_confirmed` is `true`.
   - **The hook.** Its script is auth-coupled (`sql.md`), so it is validated on the staging
     project by a checklist: an unconfirmed user gets `false`, a confirmed one `true`, an
     anonymous one `false`, and a forged `contact_confirmed` in the claims is overwritten.
   - **The settings check.** It is tested on recorded configurations: a matching one passes,
     and each of the four settings (the signing key in use and the three of decision 5),
     changed alone, fails it.

## Consequences
- BP-001's implementation (#138) builds the validation, the module ports, the hook and the
  settings check, and writes the host proofs above. It also:
  - checks that each Supabase project, staging (ADR-ARCH-026) and production, signs with an
    asymmetric key, migrating from the legacy secret where it does not;
  - keeps the default token lifetime;
  - versions the hook in the repository.
- The hook's script is auth-coupled. This ADR makes `sql.md`'s rule for such scripts its own,
  so that the rule no longer rests on the removed ADR-ARCH-005. The script is listed by name
  among the scripts excluded from the Testcontainers run, and it is validated against the
  staging project by the checklist of decision 9, never against a faked `auth` schema.
- The hook is `language plpgsql`, so creating it resolves no `auth` object and needs no grant on
  `auth`. `supabase_auth_admin` receives `execute` on it and `usage` on its schema; `execute` is
  revoked from `PUBLIC`, `anon` and `authenticated`. Its schema and how it reaches each project
  are settled by #138, within ADR-ARCH-019, ADR-ARCH-027 and ADR-ARCH-028. If that needs a grant
  on `auth` to a product role, a new ADR decides it first.
- A hook error or timeout fails every sign-in, so the hook stays one read of the user record.
- The settings check reads each project's Auth configuration and signing keys, and fails when
  the key in use is not an ES256 or RS256 key (decision 1) or a setting of decision 5 differs.
  The provider documents both under its Management API (`/v1/projects/{ref}/config/auth` and
  `/v1/projects/{ref}/config/auth/signing-keys`). Where the check runs and which fields it
  reads are settled by #138 from the provider's documentation.
- The check needs a Management API credential. That credential is stronger than anything the API
  or the runner holds, and neither receives it. #138 places it under ADR-ARCH-025, or under a
  new ADR if no role fits. The API itself holds no provider credential.
- The maintainer makes or approves every change to the production project (ADR-ORG-004 §4): the
  key migration, the hook and the settings. Only staging changes within #138's delivery.
- Data authorization does not depend on the level's freshness: an ended access applies from the
  next statement (ADR-ARCH-032, Consequences).
- The client store refreshes the session before it submits a kept founding request and after
  a 401, then submits the same request again (`contract.md` §6). On a poor connection, a
  refreshed token that takes more than the bound to arrive is refused again. The client then
  repeats the refresh: the founding is not lost, but it can take more than one round trip.
- Requiring confirmation before sign-in constrains BP-002 on Supabase. If BP-002 admits signing
  in before a contact is confirmed, implicit confirmation removes the hook's source. A new ADR
  then chooses another source before the setting changes; the settings check stops the change
  from passing silently. Until then, a signed-in person without a confirmed contact
  (BP001-AC-09) arises only through the paths of decision 5: for example, an operator
  clearing a confirmation.

## Alternatives considered
- **Reading the level from `GET /auth/v1/user` at each founding.** Rejected. Every call would
  come from the API's one address, so the per-IP limit would cap the whole platform. Raising
  the limit enough would also raise it for sign-up, recovery and one-time codes, which share
  it. Forwarding the user's address needs a secret key with full access, bypassing RLS.
- **The provider's administration API with a secret key.** Rejected for the same credential.
- **Taking any non-anonymous session as confirmed, because sign-in requires a confirmation.**
  Rejected. It fails open whenever a path admits a session without a confirmed contact, a
  setting turned off or a channel BP-002 adds. No standard claim states a confirmation, so the
  API could not tell. The claim reads the user's confirmation explicitly and fails closed when
  the hook is missing. Both depend on the confirmation settings, which is why decision 5 fixes
  them and the check watches them.
- **The legacy HS256 secret.** Rejected. The API would hold a secret able to mint tokens, and
  Supabase no longer recommends it.
- **`auth.uid()`, or a grant on `auth` for the API's role.** Rejected by ADR-ARCH-032 §3.
- **A product table of identity levels.** Rejected. The product would keep a copy of a fact the
  provider owns (ADR-ARCH-031 §2), fed through a call the provider would have to make.
- **The defaults: a clock skew of 5 minutes, keys refreshed every 12 hours.** Rejected. The
  skew lengthens every token by 5 minutes, and the refresh keeps a revoked key for hours,
  against Supabase's guidance.
- **The level as an endpoint authorization policy.** Rejected. It would answer 403 before 400
  and 422, against the order of `contract.md` §2.
- **The level as fresh as the token's lifetime, one hour.** Rejected. A request kept offline
  for less than an hour would be founded with a token issued before the level was lost,
  against BP001-AC-10 and TSK-2's precondition.
- **Relying on the client to refresh before submitting.** Rejected as the guarantor. The
  server cannot check the client; it checks the token's age instead (decision 6).
- **Copying MicroKit.Auth (`MicroKit.Auth.Supabase`), read at `michaelatsey/microkit` `main`
  `e8bca2e92e0fa3e236e4c56e9a3680ea6f7f5c5e` on 2026-11-11.** Not chosen now. Its validator
  states that it has no allow-list of algorithms, and it caches keys for 60 minutes by default.
  It brings its own validator and current-user layer beside the framework's handler. Decision 1
  needs only that handler, and ADR-ARCH-035 §2 copies a library only when a decision needs it.
  A later decision may copy it under ADR-ARCH-037 and adapt it to this one.

## References
- Supabase documentation, read 2026-11-11: Auth "JWT Signing Keys", "JSON Web Token (JWT)",
  "JWT Claims Reference", "User sessions", "Users", "Rate limits", "Understanding API keys",
  "Auth Hooks", "Custom Access Token Hook", "General configuration"; Database "Custom Claims &
  Role-based Access Control (RBAC)"; CLI "Configuration" (`auth.email.enable_confirmations`,
  `auth.sms.enable_confirmations`); Management API "List all signing keys for the project"
  (`GET /v1/projects/{ref}/config/auth/signing-keys`).
- Microsoft Learn, read 2026-11-11: "Configure JWT bearer authentication in ASP.NET Core"
  (aspnetcore-10.0); "Overview of ASP.NET Core authentication" (Forbid); "Customize the behavior
  of authorization middleware"; `JwtBearerOptions.IncludeErrorDetails`,
  `RefreshOnIssuerKeyNotFound`, `AutomaticRefreshInterval`, `RefreshInterval`;
  `TokenValidationParameters.DefaultClockSkew`;
  `ConfigurationManager<T>.DefaultAutomaticRefreshInterval`, `DefaultRefreshInterval`,
  `GetConfigurationAsync`. `dotnet/aspnetcore`, `main`,
  `src/Security/Authentication/JwtBearer/src/JwtBearerHandler.cs`, read 2026-11-11.
- `.claude-context/context/architecture/conventions/sql.md`, "Auth-coupled scripts".
- BP-001 `process.md` (BP001-D1), `acceptance.md` (BP001-AC-06 to BP001-AC-11, BP001-AC-23),
  `contract.md` (§2, §4, §7, §9).

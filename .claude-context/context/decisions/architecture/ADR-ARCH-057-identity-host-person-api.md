# ADR-ARCH-057 — The identity host's person API: tokens issued for itself, its session check, one lock order, its answers, the limits on a code for a contact already held, its OpenAPI document, and the stop link after a recovery's effect

Status: accepted
Date: 2026-11-28
Refs: issue #249 (BP-002's contract); parent #163 (BP-002); BP-002 `process.md` (TSK-1 to TSK-10,
BP002-R2, BP002-R7, BP002-R12), `acceptance.md`; ADR-ARCH-018; ADR-ARCH-033; ADR-ARCH-040;
ADR-ARCH-042; ADR-ARCH-043; ADR-ARCH-044; ADR-ARCH-046; ADR-ARCH-047; ADR-ARCH-049; ADR-ARCH-050;
ADR-ARCH-053; ADR-ARCH-054; ADR-ARCH-055; ADR-ORG-004.

## Context
BP-002's signed-in operations (TSK-3 to TSK-6, the requester's cancel of TSK-7, TSK-10) change the
account, which the Identity context owns (ADR-ARCH-042 decision 1); the API host never references
the Identity module, so they run on the identity host. No decision in force says how that host
authenticates a person's own requests: ADR-ARCH-043 is the API host's, and ADR-ARCH-044 decision 1
issues access tokens with the single audience `saasbtp-api`. Writing BP-002's contract (#249) also
found four points no decision settles: the error answers of the identity host (ADR-ARCH-040 is the
API's); the order in which its units of work lock rows, so that two of them never wait on each
other; the limits on a code that proves a contact the account already holds (ADR-ARCH-055 decisions
1 and 5 cover sign-in codes and codes proving a new contact only); and where the identity host's
OpenAPI document is committed (ADR-ARCH-018 decision 4 names `apps/api/openapi/v1.json`, the API's).

The maintainer's directions of 2026-11-28, in the /fix-issue 249 session: write the decision as a
new ADR in the contract's pull request, "choosing the mechanism from OpenIddict's and Microsoft's
official documentation: the identity host accepts only tokens issued for itself, and checks the
session as the API does"; settle the OpenAPI document's location in it; count the codes for a
contact already held "in ADR-ARCH-055 §5's daily ceiling per route, without Turnstile"; apply
ADR-ARCH-055 decision 4's per-IP limit to them; answer a failed Siteverify call like a refused token;
write this ADR accepted in that pull request, as the ADRs of #248 were in #252, the maintainer's
review before the merge being the control; and, on the diff review's finding that TSK-7's Errors
("The recovery has already taken effect: told so, and told about the dispute (TSK-8) and ... the
assisted recovery (TSK-9)") contradict ADR-ARCH-049 decision 3's last bullet, state that this ADR
partly supersedes that bullet for a valid secret whose recovery has taken effect, and carry the stop
secret in the link's fragment.

What the sources state, read on 2026-11-28. [DOC] marks a fact of an official documentation page;
[SRC] a fact read in OpenIddict's source at tag 7.9.0, which shows behaviour, not a documented
commitment (as in ADR-ARCH-042).
- [DOC] OpenIddict, "Authorization storage": a pass-through handler answers an error with `Forbid`,
  the scheme `OpenIddictServerAspNetCoreDefaults.AuthenticationScheme`, and the properties
  `OpenIddictServerAspNetCoreConstants.Properties.Error` and `ErrorDescription` (shown for the
  authorization endpoint); "access tokens are considered valid even if the attached authorization
  was revoked" unless `EnableAuthorizationEntryValidation()` is called, which requires "a direct
  access to the server database".
- [DOC] OpenIddict, "Migrating to OpenIddict 7.0": "the allowed audiences and resources must now be
  registered in the server options" (`RegisterAudiences`, `RegisterResources`), the server validating
  "the standard `audience` and `resource` parameters used in token exchange requests".
- [DOC] OpenIddict, "Implementing token validation in your APIs": `UseLocalServer()` imports "the
  configuration from the local server instance, including the signing and encryption keys"; the
  guide does not describe an audience check. "Token formats": access tokens "are always issued with
  a `"typ": "at+jwt"` header".
- [SRC] `OpenIddictServerAspNetCoreHandlers.Exchange.cs` and `OpenIddictServerAspNetCoreHandlerFilters.cs`:
  with `EnableTokenEndpointPassthrough`, `EnablePassthroughMode` skips the built-in handling of every
  token request, so the application builds the principal for every grant, refresh included.
- [SRC] `OpenIddictServerHandlers.Protection.cs`, `AttachTokenMetadata`: "For access and identity
  tokens, set the public audience claims using the private audience claims from the security
  principal", from `principal.GetAudiences()`; `OpenIddictExtensions.cs`: `SetAudiences` writes that
  private claim and replaces any earlier value.
- [SRC] `OpenIddictServerHandlers.Exchange.cs`, `ValidateGrantedScopes`: a refresh request's `scope`
  must be a subset of the refresh token's scopes ("the authorization server MUST ensure that it
  doesn't contain scopes" not granted); `ValidateScopeParameter`: "using the "scope" parameter with
  grant_type=refresh_token is deliberately allowed".
- [SRC] `OpenIddictServerBuilder.cs`: `RegisterAudiences` registers audiences "(exclusively used with
  the OAuth 2.0 Token Exchange flow)"; `RegisterScopes` registers scopes "so they can be returned as
  part of the discovery document".
- [SRC] `OpenIddictValidationHandlers.Protection.cs`: the validation handler's audience check skips
  when no audience is configured; the 8.0.0-preview.4 release note says that skip was removed.
- [DOC] Microsoft Learn, "Authorize with a specific scheme in ASP.NET Core": an endpoint or a policy
  names the scheme it depends on; "By specifying a single scheme, the corresponding handler runs".
  `TokenValidationParameters.ValidAudience`: "a valid audience that will be used to check against the
  token's audience"; `IssuerSigningKeys`: the keys "used for signature validation".
- [DOC] Microsoft Learn, "Generate OpenAPI documents": `OpenApiDocumentsDirectory` sets where the
  build-time document is written, `--file-name` in `OpenApiGenerateDocumentsOptions` its name;
  "Build-time OpenAPI document generation functions by launching the apps entrypoint with a mock
  server implementation", and code paths can be "conditioned behind a check of the entry assembly"
  `GetDocument.Insider`.
- [DOC] OpenIddict documents the `resource` parameter for authorization and pushed authorization
  requests and for token exchange; no page read states it for the refresh grant.

## Decision
0. **Status.** Accepted in the pull request of #249, on the maintainer's directions above; the
   maintainer's review before the merge is the control (ADR-ORG-004 §4).
1. **An audience of the identity host's own.** Every access token carries exactly one audience,
   `saasbtp-api` or `saasbtp-identity`, never both. The identity host's person endpoints accept only a
   token whose `aud` is `saasbtp-identity`; the API keeps requiring `saasbtp-api` (ADR-ARCH-043
   decision 1.1, unchanged). Neither host accepts the other's token. Both audiences are registered
   with `RegisterAudiences`, as ADR-ARCH-043 decision 1.1 states for `saasbtp-api` [DOC]; in 7.9.0
   that registration serves token exchange requests [SRC], which are not enabled (decision 2), so it
   neither grants nor restricts the audience decision 2 sets.
2. **How the client obtains an identity token: from its refresh token.** The refresh token is held
   by the client and the identity host only; the API never receives it.
   - A refresh request (rolling, ADR-ARCH-044 decision 3) that carries `scope=identity` is answered
     with an access token whose only audience is `saasbtp-identity`. Any other refresh, and the
     sign-in grant, is answered with one whose only audience is `saasbtp-api`.
   - The token endpoint's pass-through (ADR-ARCH-042 decision 2) builds the principal for every
     grant [SRC] and sets its audience with `SetAudiences`, the source of the token's public `aud`
     [SRC].
   - The sign-in grant grants the scopes `offline_access`, `api` and `identity`, registered with
     `RegisterScopes`. A refresh's `scope` must be a subset of them [SRC]; the principal's scopes are
     not narrowed, so the next refresh token keeps all three.
   - Same session (`sid`), lifetime (10 minutes), ES256 signature, no encryption and claims as
     ADR-ARCH-044 decision 1, except `aud`.
   - The client sends one refresh at a time on its one chain: a refresh token reused beyond the
     30-second leeway ends the session (ADR-ARCH-044 decision 3).
   - Token exchange is not enabled.
   - The field client is a public client permitted the token endpoint, the custom sign-in grant,
     the refresh grant and the scopes `offline_access`, `api` and `identity`, and nothing else.

   This extends ADR-ARCH-044 decision 1 with an access token for the identity host; the rest of
   ADR-ARCH-044 holds.
3. **Validation.** ASP.NET Core's JwtBearer handler, in a scheme of its own that only the person
   endpoints name [DOC]; the operator console keeps its cookie scheme (ADR-ARCH-050 decision 2), and
   neither scheme is accepted on the other's endpoints. The rules are those ADR-ARCH-043 decision 1
   has the API apply, except:
   - the issuer is the identity host's own;
   - `ValidAudience` is `saasbtp-identity`;
   - ES256 only, narrowing ADR-ARCH-043's "ES256 or RS256": the identity host signs with ES256
     only (ADR-ARCH-044 decision 1);
   - the keys are the public keys of the host's own signing certificates, read in process from the
     server's signing credentials, not fetched from its own JWKS. The credentials change only at a
     restart (ADR-ARCH-044 decision 6), so the host validates with exactly the keys it signs with or
     keeps for validation.

   Held from ADR-ARCH-043 decision 1: `typ` `at+jwt`, no HS256 nor unsigned token, `exp` with a 30-second skew, `exp`
   at most one hour after `iat`, `sub` a UUID, the bare 401 challenge with `IncludeErrorDetails` off,
   authentication before the body.

   ADR-ARCH-043 decision 1.1's fallback policy, on a host with two schemes: no default scheme; the
   fallback authorization policy requires an authenticated user of the person scheme; the console's
   endpoints name the cookie scheme; every anonymous endpoint says so explicitly: the sign-in code
   request, the stop at sign-in, the operator's contact information, the token endpoint's
   pass-through, the console's passkey sign-in endpoints (ADR-ARCH-050 decision 2), the stop page, the requester's page of an assisted recovery, discovery, the JWKS, the
   health endpoints, and the providers' callbacks, which each provider's signature authenticates.
4. **The session check, as the API does it** (ADR-ARCH-043 decision 2).
   - After validation and before authorization, once per request, no cache, the host reads whether
     the OpenIddict authorization `sid` exists, is valid and belongs to `sub`. True: the request goes
     on. False, or `sid` missing or not a UUID: 401 with the bare challenge, nothing written. The
     read fails: 500, nothing written.
   - The host reads its own table as `saasbtp_identity` (ADR-ARCH-053 decision 3), not through
     `identity.session_is_active`, which is keyed on `app.user_id` for `saasbtp_api`. The read runs
     on its own connection, without a context, in an explicit read-only transaction, `sid` and `sub`
     as parameters. It is an access outside a unit of work of the identity host, beside the API's of
     ADR-ARCH-053 decision 5; ADR-ARCH-053 decision 4 is extended by this one exception.
   - Every unit of work acting for a signed-in person reads it again inside its transaction, at
     step (g) of decision 5, locking the authorization's row `for share`. Ending a session revokes
     its authorization by updating that row, so it waits for every unit of work of that session in
     flight to end, and every later one reads it revoked: a session ended while a request was in
     flight lets that request finish, and nothing of that session is written afterwards.
5. **One lock order for every unit of work of the identity host.** A unit of work takes its locks in
   this order, and in no other:
   - (a) only for a recovery's effect whose new contact belongs to another account (BP002-R14):
     ADR-ARCH-054 decision 3's exclusive advisory lock on that other account;
   - (b) the rows kept for codes that it issues, verifies or updates: first the accepted Turnstile
     token's row (ADR-ARCH-055 decision 1); then the code challenges (ADR-ARCH-042 decision 2), in
     ascending order of the normalized value;
   - (c) the recovery request waits (ADR-ARCH-056 decision 1), kept per requesting contact and per
     new contact, in ascending order of the normalized value;
   - (d) the notice and stop-code rows of a recovery that it issues, verifies or updates
     (ADR-ARCH-049 decisions 3 and 4, the per-notice factor failures included), in ascending order of
     the notice's identifier;
   - (e) the row of every account whose means it reads or changes (the Identity user), in ascending
     order of identifier; then the rows that belong to those accounts: contacts, the factor's codes
     and counters (ADR-ARCH-047 decision 3), factor proofs, the recovery. These are locked only by a
     unit of work that holds their account's row, so that row serializes them;
   - (f) OpenIddict's token rows of the sessions it redeems or revokes;
   - (g) OpenIddict's authorization rows: `for share` for the session re-read of decision 4, `for
     update` to revoke one;
   - (h) last, the day-wide counts of ADR-ARCH-055 decision 5 (the daily ceiling), each incremented in
     one statement; nothing is awaited after them.

   A row of (b), (c), (d) or (h) that may not exist yet is created by an insert that does nothing on
   conflict, then locked; two first requests for one contact therefore serialize on it. A unit of
   work never writes a row of an earlier class than the last it holds without having locked it in its
   turn. Placed in the order:
   - a sign-in, or the stop taken at sign-in, takes (b) for its contact, verifies the code, and only
     after a correct code looks the account up; a sign-in then takes (e); the stop then takes (c) for
     the requesting contact's wait, (d) for the notices and (e) for the account, and reads the
     recovery again;
   - a signed-in code request locks (b) before it reads the account; a signed-in operation that
     proves contacts takes (b) for all of them before (e);
   - a stop from the notice, a cancel and an effect first read the recovery without a lock, to learn
     its requesting contact and notices; then take (c) for that contact's wait, (d) for the notices,
     (e) for the accounts; then read the recovery again, and answer as for an ended recovery if it
     has ended meanwhile. A stop with a factor code takes (e) before the factor's rows;
   - ending sessions (TSK-10, a recovery's request and effect) takes (e), then (f) to revoke the
     tokens, then (g) to revoke the authorizations;
   - a refresh reads the account and the session without a lock in the pass-through, then takes (f)
     as OpenIddict redeems the token, then, in the same transaction, (g) `for share` to check the
     session again (ADR-ARCH-044 decision 4), refusing it if it ended; a refresh token reused beyond
     the leeway takes (f) as OpenIddict revokes the chain, then (g) to revoke the authorization
     (ADR-ARCH-044 decision 3).

   A lock later in the order is never held while one earlier in it is awaited, so no two units of
   work wait on each other in opposite orders. The API's units of work take only ADR-ARCH-054
   decision 4's shared advisory lock and no lock of `identity`. A deadlock PostgreSQL reports despite
   this order is a defect: answered 500 and logged, never retried silently. That OpenIddict's handlers
   take (f) before the identity service takes (g), as stated for the refresh and its reuse, is
   [SRC]-level and established by the tests of decision 11.
6. **The identity host's answers.** Its `/api/v1` endpoints answer in ADR-ARCH-040's format:
   problem details returned by the endpoint; no body on 400, 401 and 500; a 422 as validation problem
   details naming only the fields at fault, one fixed message per field. The faults BP-002 tells apart
   take distinct statuses, each with a fixed body, the same for every contact:

   | Fault | Status | `title` |
   |---|---|---|
   | A wrong code, or a malformed one | 422, validation problem details whose `errors` names the code's field, with its one message "The code is wrong." | ADR-ARCH-040's validation title |
   | An expired or replaced code | 410 | "The code has expired." |
   | A lock or a limit | 429, with the `Retry-After` header of RFC 9110 | "Too many attempts." |
   | A refused Turnstile token, or Siteverify failing (ADR-ARCH-055 decision 1) | 403 | "The check failed. Try again." |
   | The factor retired for this session after 5 failures (ADR-ARCH-047 decision 3), which has no end | 403 | "Sign in again to use your backup codes." |

   Every other refusal an operation needs takes a fixed `title` of its own, stated by BP-002's
   contract. No field ADR-ARCH-040 does not name is used. The OpenIddict endpoints answer in OAuth
   2.0's own format (RFC 6749 §5.2: `error`, `error_description`), which is not an ADR-ARCH-040 body;
   their values are BP-002's contract's.
7. **A code proving a contact the account already holds** (removing it, changing it, registering,
   replacing or removing the factor with a code for every contact; BP002-R7):
   - counted in ADR-ARCH-055 decision 5's daily spending ceiling of its route (SMS or e-mail), with
     the sign-in codes and the codes proving a new contact; above the ceiling it is refused, nothing
     sent, answered 429 with when to try again, with the alerts of that decision;
   - counted against ADR-ARCH-055 decision 4's per-IP limit;
   - under ADR-ARCH-046 decision 4's interval and hourly limit and ADR-ARCH-047 decision 1's lock, as
     every code of the contact;
   - no Turnstile token: the person is signed in.

   This extends ADR-ARCH-055 decisions 4 and 5; its decisions 1 and 6 are unchanged.
8. **OpenIddict's validation handler is not chosen** (`UseLocalServer`,
   `EnableAuthorizationEntryValidation`). Its documentation states neither an audience check nor a
   `typ` check; the audience check is seen in source only, with a skip that a release note of 8.0
   previews removes; and the JwtBearer rules are the API's, already tested.
9. **The OpenAPI document.** The person endpoints are Minimal APIs under `/api/v1` (ADR-ARCH-018
   decisions 1 to 3). Their v1 document is generated at build time and committed at
   `apps/api/openapi/identity-v1.json` (`OpenApiDocumentsDirectory`, `--file-name identity-v1`)
   [DOC], CI failing on a difference as ADR-ARCH-018 decision 4 states. ADR-ARCH-018 decision 5 holds:
   the registrations that need a secret or the database (OpenIddict's server and its certificates,
   the stores, the delivery adapters, Turnstile) are skipped when the entry assembly is
   `GetDocument.Insider` [DOC], and registered in every other run, where production still refuses
   development certificates (ADR-ARCH-044 decision 6). The OpenIddict endpoints, the console and the
   stop page are not in the document. This extends ADR-ARCH-018 decision 4 to a second host; the API's
   document is unchanged.
10. **The stop link's secret, and a recovery that has taken effect.** This partly supersedes the last
    bullet of ADR-ARCH-049 decision 3 ("Opened with an unknown, used or ended secret, the page says
    only that there is nothing to stop"), for one case, on the maintainer's direction:
    - The link carries the secret in its fragment (`/stop#<secret>`), which a browser never sends in a
      request; the page reads it and posts it in the body. It is never in a path, a query or a log.
    - A valid secret whose recovery has taken effect is answered as TSK-7's Errors require: told that
      the recovery took effect and when, the dispute (TSK-8) with the operator's contact information,
      and the assisted recovery (TSK-9). The holder of that secret holds the notice, which already
      told them that the recovery existed, so nothing more is revealed.
    - An unknown secret, or one whose recovery was stopped, cancelled or did not take effect, is still
      answered only that there is nothing to stop, as ADR-ARCH-049 decision 3 states.

    The rest of ADR-ARCH-049 decision 3 holds.
11. **Verification.** The identity host's tests (`apps/api/tests/SaasBtp.Identity.IntegrationTests`)
    and the API host's:
    - a `saasbtp-api` token refused 401 by the identity host; a `saasbtp-identity` token refused 401 by
      the API; no token is issued with both audiences;
    - a refresh with `scope=identity` gives `aud` `saasbtp-identity` only, the next refresh without it
      `saasbtp-api` only, and the refresh token still grants the three scopes; a refresh asking a scope
      not granted is refused; a token of each audience is issued with both audiences registered;
    - the key, `typ`, skew, lifetime and session cases of ADR-ARCH-043 against the identity host; the
      session read in its own read-only transaction; a failed read answered 500;
    - the console cookie not accepted on a person endpoint, a person token not accepted by the console;
    - each status of decision 6 the same for a contact an account holds and one none holds, a failed
      Siteverify call included; a refusal still counting the failure it must count;
    - decision 5's order: an effect and a unit of work of the other account at once; a correct sign-in
      with a contact concurrent with a signed-in operation proving it (removing it, registering the
      factor); a signed-in code request concurrent with a sign-in with the same contact; an effect
      concurrent with a stop from the notice; a stop by factor code concurrent with replacing the
      factor; a refresh token reused beyond the leeway concurrent with ending every other session and
      with signing out of that session; an ordinary refresh concurrent with ending that session
      (BP002-AC-61), refused once it ended; a
      stop concurrent with a new recovery request through the requesting contact; two first code
      requests for a new contact at once, one 202 and one 429; each ends without a deadlock; a session
      ended while its unit of work is in flight, which then writes nothing more;
    - the stop link's secret in no request path, query or log; the page for a valid secret whose
      recovery took effect, and for an unknown one;
    - the fallback policy: a person endpoint without a token answered 401, a console endpoint without
      its cookie refused, each anonymous endpoint answered without either;
    - a code for a held contact counted in the ceiling and refused above it, counted per IP, and
      accepted without a Turnstile token;
    - the OpenAPI document checked as the API's is, and generated with no secret configured.

    Each [SRC]-only behaviour of decisions 2 and 5 is established by these tests before it is relied on, as
    ADR-ARCH-042 decision 2 does for the sign-in exchange.

## Consequences
- The client holds two kinds of access token and refreshes for the identity audience only before an
  operation of the identity host; every refresh goes through the one rolling chain.
- When the SMS ceiling is reached (ADR-ARCH-055 decision 5), the signed-in changes that need an SMS
  code wait for the reset at midnight UTC as sign-in does: removing or changing a phone number with
  its code, and registering, replacing or removing the factor with a code for every contact when one
  of them is a phone number. What remains possible: the same changes with e-mail codes, those the
  factor allows (removing a contact, replacing or removing the factor), cancelling one's recovery,
  and the stop from a notice, outside the ceiling (ADR-ARCH-055 decision 6).
- The identity host makes one more database round trip per authenticated request, as the API does
  (ADR-ARCH-053, Consequences).
- ADR-ARCH-040's format now governs a second host, with the statuses of decision 6; ADR-ARCH-040's
  Consequences ask a new answer to come through an ADR, which decision 6 is.
- The index notes the extensions of ADR-ARCH-018 decision 4, ADR-ARCH-040 (to the identity host),
  ADR-ARCH-044 decision 1, ADR-ARCH-053 decision 4 and ADR-ARCH-055 decisions 4 and 5, and the part of
  ADR-ARCH-049 decision 3 that decision 10 supersedes.

## Alternatives considered
- **The identity host accepting `saasbtp-api` tokens, or one token with both audiences.** Rejected by
  the maintainer's direction: a token sent to the API would be accepted by the identity host.
- **Token exchange with the API access token as subject.** Rejected: whoever holds an API token, the
  API included, could obtain an identity token; the API's token would be accepted one hop later.
- **Token exchange with the refresh token as subject.** Not chosen: whether OpenIddict redeems the
  refresh token it exchanges is not stated.
- **RFC 8707's `resource` parameter on the refresh grant.** Not chosen: OpenIddict documents it for
  other requests, not the refresh grant.
- **A second refresh chain for the identity host.** Rejected: rolling refresh tokens end the session
  on a reuse (ADR-ARCH-044 decision 3), and two chains on one authorization would compete.
- **A cookie for persons.** Rejected by ADR-ARCH-042 decision 2.
- **Introspection.** Rejected, as ADR-ARCH-043 rejects it for the API: a call per request.
- **OpenIddict's validation handler.** Decision 8.
- **Codes for a held contact outside the ceiling.** Rejected: about 120 SMS a day per contact at five
  an hour, about $59 a day at ADR-ARCH-045's $0.4925, against ADR-ARCH-055's rejection of unbounded
  spending. **Under Turnstile too.** Rejected: the person is signed in.

## References
Read on 2026-11-28:
- OpenIddict: https://documentation.openiddict.com/introduction;
  https://documentation.openiddict.com/guides/getting-started/implementing-token-validation-in-your-apis;
  https://documentation.openiddict.com/configuration/token-storage;
  https://documentation.openiddict.com/configuration/authorization-storage;
  https://documentation.openiddict.com/configuration/token-formats;
  https://documentation.openiddict.com/guides/migration/60-to-70;
  https://github.com/openiddict/openiddict-core/releases. Source, tag 7.9.0:
  `src/OpenIddict.Server/OpenIddictServerBuilder.cs`, `OpenIddictServerOptions.cs`,
  `OpenIddictServerHandlers.Exchange.cs`, `OpenIddictServerHandlers.Protection.cs`;
  `src/OpenIddict.Server.AspNetCore/OpenIddictServerAspNetCoreHandlers.Exchange.cs`,
  `OpenIddictServerAspNetCoreHandlerFilters.cs`, `OpenIddictServerAspNetCoreOptions.cs`;
  `src/OpenIddict.Validation/OpenIddictValidationBuilder.cs`,
  `OpenIddictValidationHandlers.Protection.cs`;
  `src/OpenIddict.Abstractions/Primitives/OpenIddictExtensions.cs`.
- Microsoft Learn:
  https://learn.microsoft.com/en-us/aspnet/core/security/authorization/authorize-with-a-specific-scheme;
  https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0;
  https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters.validaudience;
  https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters.issuersigningkeys.
- RFC 6749 §5.2; RFC 8693; RFC 8707; RFC 9110 (`Retry-After`).

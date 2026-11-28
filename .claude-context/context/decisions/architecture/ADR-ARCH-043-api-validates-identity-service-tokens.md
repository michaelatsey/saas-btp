# ADR-ARCH-043 — The API validates the identity service's tokens and checks their session; what of ADR-ARCH-038 holds; ADR-ARCH-041 retired

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope: "ADR-ARCH-038 confronted with BP-002"; Done when 3); BP-001
(`contract.md` §2, §4, §7); BP-002 `process.md` (TSK-6, TSK-9, TSK-10, BP002-D5); ADR-ARCH-034;
ADR-ARCH-038; ADR-ARCH-041; ADR-ARCH-042; ADR-ARCH-044; ADR-ARCH-053; ADR-ARCH-054.

## Context
ADR-ARCH-038 validates Supabase Auth's access tokens in the API and reads the confirmed-contact
level from a claim set by a custom access token hook; ADR-ARCH-041 checks the Supabase settings that
claim depends on. ADR-ARCH-042 makes an identity service of the platform the issuer, on OpenIddict.
#248 requires every decision of ADR-ARCH-038 to be stated to hold or superseded by name.

What the sources state, read on 2026-11-27:
- OpenIddict: an access token is a JWT of type `at+jwt`, encrypted by default, made signed-only by
  `DisableAccessTokenEncryption()` ("Token formats"); since 7.0, "the allowed audiences and resources
  must now be registered in the server options" ("Migrating to OpenIddict 7.0"); "access tokens are
  considered valid until they expire" when their entry is not checked, and "access tokens are
  considered valid even if the attached authorization was revoked" ("Token storage", "Authorization
  storage").
- Microsoft.IdentityModel, `TokenValidationParameters.ValidTypes`: "If this property is not set, the
  'typ' header claim will not be validated and all types will be accepted. ... The default is
  `null`." (Microsoft Learn API reference, v8.23.0); `Validators.ValidateTokenType` accepts any type
  when it is null or empty, and matches exactly once set (AzureAD
  azure-activedirectory-identitymodel-extensions-for-dotnet, 8.23.0, 8b16f41, `Validators.cs`).
- BP-002: every signed-in person meets the confirmed-contact level (BP002-D5); a session that ends
  "can no longer send anything under the person" (TSK-10); a recovery ends "every other session of
  the account" (TSK-6) or "every session of the account" (TSK-9).

## Decision
1. **ADR-ARCH-038, decision by decision.**
   1. *What the host accepts* — partly superseded. Superseded: the issuer is the identity service's
      URL for the environment, not `https://<project-ref>.supabase.co/auth/v1`; the audience is
      `saasbtp-api`, registered in OpenIddict, not `authenticated`. Added: `typ` must be `at+jwt`
      (`ValidTypes`). Holds: the JWT bearer handler, ES256 or RS256 only, no HS256 nor unsigned token,
      `exp` present with a 30-second skew, `iat` present and `exp` at most one hour after it, `sub` a
      UUID, `is_anonymous: true` refused (the identity service never issues it), authentication as the
      fallback policy, the anonymous endpoints, authentication before the body.
   2. *Keys and their rotation* — partly superseded. Superseded: the JWKS address is the identity
      service's (`/.well-known/jwks`); the timings that came from Supabase's edge cache (a standby key
      20 minutes before it signs, a revoked key leaving about 20 minutes after revocation) are
      replaced by ADR-ARCH-044's rotation. Holds: keys read from the JWKS directly through the
      handler's configuration manager with a retriever of our own, refreshed every 10 minutes and on
      an unknown key at most every 5 minutes, 500 before the first successful fetch, the last keys kept
      while a refresh fails.
   3. *The subject* — holds: `sub`, without claim mapping, the only value set in `app.user_id`. It is
      now the account's identifier in the identity service.
   4. *401* — holds: a bare `Bearer` challenge, `IncludeErrorDetails` off, the reason logged.
   5. *The confirmed-contact level* — partly superseded. Superseded: "Who sets it" (a custom access
      token hook of Supabase), "The settings it depends on" (the two Supabase confirmation settings and
      the hook), and "Which confirmations count" as it listed Supabase's paths (a link the provider
      sent, an upstream identity provider, an operator through the provider's administration). Now: the
      identity service sets `contact_confirmed` at every issuance, refreshes included, to `true` when
      the account holds at least one confirmed contact; a contact is confirmed only by a correct code
      sent to it (BP002-D1, TSK-2, TSK-3); no upstream identity provider exists, and the operator
      console sets no confirmation (an assisted recovery's new contact is confirmed by its own code,
      TSK-9). Holds: one boolean claim, the validated token as the only source, the API calling the
      provider for nothing, the port of the Features project read from `sub` and `contact_confirmed`
      alone.
   6. *Freshness* — holds: an operation requiring a level accepts a token issued at most 60 seconds
      before (90 with the skew), otherwise 401 with the bare challenge.
   7. *403* — holds.
   8. *What depends on the provider* — superseded: the issuer, the audience, the JWKS address, and the
      session check below; the claims read (`sub`, `contact_confirmed`, `sid`, `is_anonymous`) and the
      answers do not depend on the provider. The signing key, the hook and the three settings leave the
      list.
   9. *What the host checks for BP-001* — partly superseded. Superseded: the hook's checklist on the
      staging project, and the settings check's tests on recorded configurations. Holds: every host
      proof of BP-001 (BP001-AC-06 to AC-11, AC-23), the boundaries, the fallback policy, the key cases
      and the 401's form, run against a test issuer with an in-process JWKS.
2. **The session check, added.** Every access token carries `sid`, the identifier of the OpenIddict
   authorization that is its session (ADR-ARCH-044). After the token is validated and before
   authorization, the host reads `identity.session_is_active(sid)` as ADR-ARCH-053 decision 5 states,
   once per authenticated request, with no cache:
   - true: the request goes on;
   - false, the authorization ended, revoked, pruned or absent, or `sid` missing or not a UUID: 401,
     with the bare challenge of decision 4 of ADR-ARCH-038, nothing written;
   - the read fails (no connection, an error): 500, nothing written, as for a JWKS that cannot be
     fetched: the host could not establish that the token is valid, which does not make it invalid.

   This is how an access token already issued stops being accepted once its session ends: ending one
   session, every other session or every session of an account (TSK-6, TSK-9, TSK-10) takes effect
   from the API's next request. Every unit of work reads it again inside its transaction
   (ADR-ARCH-054 decision 4), so that a request already past this check whose unit of work starts
   after a recovery's effect has ended its session writes nothing.
3. **ADR-ARCH-041 is superseded wholly**, from the deployment of the identity service: no Supabase
   Auth setting remains part of a decision, so the settings check, its workflow, its Doppler role and
   its tokens are retired by the delivery; script `0004__access__create_access_token_hook.sql`'s hook is
   dropped by a script of the delivery. Until then ADR-ARCH-041 holds as it is. The identity service's
   own settings (lifetimes, keys, limits) are in the repository and its image, reviewed by pull
   request, so no drift check of that kind replaces it.
4. **When it applies.** With ADR-ARCH-042 decision 7: from the deployment of the identity service.

## Consequences
- The host tests of ADR-ARCH-038 decision 9 run against tokens with `typ` `at+jwt`, the audience
  `saasbtp-api` and a `sid`, signed by the test key; added cases: another `typ` refused 401; another
  audience refused 401; a `sid` whose session is inactive or absent refused 401 with nothing written;
  an active one accepted; a failed session read answered 500 with nothing written.
- The session check costs one database round trip per authenticated request (ADR-ARCH-053).
- The API holds no secret of the identity service: only its issuer, audience and JWKS address, which
  are configuration.
- `contract.md` of BP-001 is unchanged in its answers (401, 403, the order of checks); a 401 now also
  arises from an ended session.

## Alternatives considered
- **Short access tokens alone, no session check.** Rejected: an ended session would keep sending under
  the person until its last access token expires, against TSK-10's "can no longer send anything".
- **Introspection at the identity service on each request.** Rejected: a network call to another host
  on every request, and the identity service on every request's path (ADR-ARCH-053, Alternatives).
- **Keeping the audience `authenticated`.** Rejected: it is Supabase's value, and a token of a
  Supabase project's Auth, which cannot be disabled (ADR-ARCH-042), would carry it.
- **Leaving `typ` unchecked.** Rejected: the default accepts every type; requiring `at+jwt` refuses an
  identity token or another JWT signed by the same keys.

## References
Read on 2026-11-27:
- OpenIddict: https://documentation.openiddict.com/configuration/token-formats;
  https://documentation.openiddict.com/guides/migration/60-to-70;
  https://documentation.openiddict.com/configuration/token-storage;
  https://documentation.openiddict.com/configuration/authorization-storage.
- Microsoft Learn:
  https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters.validtypes;
  source https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet (8.23.0,
  8b16f41, `src/Microsoft.IdentityModel.Tokens/Validators.cs`); https://github.com/dotnet/aspnetcore
  (v10.0.0, 7387de9, `src/Security/Authentication/JwtBearer/src/JwtBearerOptions.cs`).
- ADR-ARCH-038, every decision; ADR-ARCH-041; BP-001 `contract.md` §2.

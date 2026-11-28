# ADR-ARCH-044 — The identity service's tokens, sessions and signing keys: 10-minute access tokens, rotated refresh tokens, a 30-day session, ES256 certificates rotated by NotBefore

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope item 5); BP-002 `process.md` (TSK-6, TSK-9, TSK-10, TSK-11, BP002-D4);
ADR-ARCH-025; ADR-ARCH-038; ADR-ARCH-042; ADR-ARCH-043; ADR-ARCH-051; ADR-ARCH-053.

## Context
#248 asks for the session lifetime, online and offline (item 5). The maintainer's direction of
2026-11-27 asks for the access-token lifetime, refresh-token rotation and revocation, how an access
token already issued stops being accepted when every session of an account ends, signing-key
rotation and offline behaviour. ADR-ARCH-038 decision 1 caps an access token at one hour after its
`iat`; ADR-ARCH-043 checks the session on every request.

What the sources state, read on 2026-11-27 (OpenIddict 7.9.0, `openiddict-core` tag 7.9.0, 31893c7;
`[SRC]` for code and its XML documentation):
- Default lifetimes: access token 1 hour, refresh token 14 days
  (`RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14)`, "can be set to null to issue
  refresh tokens that never expire") `[SRC]`; no minimum or maximum is enforced `[SRC]`. With
  `DisableSlidingRefreshTokenExpiration`, a refresh grant copies the expiry of the refresh token it
  redeems `[SRC]`.
- Rolling refresh tokens are on by default; `DisableRollingRefreshTokens()`: "Disabling rolling
  refresh tokens is NOT recommended, for security reasons." The reuse leeway defaults to 30 seconds.
  On a redeemed token used again: "revoke the entire chain of tokens associated with the
  authorization ... Note: the authorization itself is not revoked to allow the legitimate client to
  start a new flow." `[SRC]`
- Revocation: `IOpenIddictTokenManager.RevokeByAuthorizationIdAsync`, `RevokeBySubjectAsync`;
  `IOpenIddictAuthorizationManager.RevokeBySubjectAsync`, `TryRevokeAsync` (OpenIddict "Migrating to
  6.0", "New APIs"; `[SRC]`).
- Signing: "certificates that are not yet valid are not used by OpenIddict and certificates with the
  furthest expiration date are always preferred; X.509 certificates are always preferred to raw
  RSA/ECDSA keys" ("Encryption and signing credentials"). In code: the credentials are sorted once
  when the options are configured, a certificate whose NotBefore is in the future placed after the
  others; tokens are signed with the first (`SigningCredentials.First()`); the JWKS publishes every
  RSA or ECDSA signing key, valid or not yet valid; startup fails (ID0088) when every signing
  credential is a certificate and none is valid `[SRC]`. Development certificates are not for
  production.
- NIST SP 800-63B-4: at AAL1, "the reauthentication ... SHOULD be no more than 30 days"; session
  secrets from an approved random generator, "at least 64 bits".

## Decision
1. **Access tokens.** 10 minutes, ES256, signed and not encrypted (`DisableAccessTokenEncryption`),
   since the API validates them with JwtBearer (ADR-ARCH-043). Claims: `sub` (the account),
   `contact_confirmed`, `sid`, `aud` `saasbtp-api`, `iat`, `exp`. Within ADR-ARCH-038's one-hour cap.
2. **A session is one OpenIddict authorization.** The sign-in exchange (ADR-ARCH-042) creates one
   authorization per sign-in on a device and attaches every token it issues to it; `sid` is that
   authorization's identifier.
3. **Refresh tokens.** Rolling (OpenIddict's default, never disabled); reuse leeway 30 seconds. A
   refresh token reused beyond the leeway ends its session: OpenIddict revokes the authorization's
   tokens, and the identity service also revokes the authorization, so that the API's session check
   refuses its access tokens (ADR-ARCH-043). Lifetime: 30 days, with
   `DisableSlidingRefreshTokenExpiration`, so that every refresh token of a session expires 30 days
   after the sign-in, whatever the refreshes in between.
4. **Session lifetime, online and offline.** 30 days from sign-in, absolute; no inactivity timeout.
   Every refresh also checks that the account still exists and that the session is active.
   - Offline: the device keeps capturing whatever the session's state (BP002-D4, TSK-11). A device
     back online refreshes; a refused refresh (session ended or past its 30 days) means signing in
     again, and the captures wait for the same person (ADR-ARCH-051). A device offline longer than
     its access token's 10 minutes is not signed out by that: only the refresh decides.
5. **Ending sessions.** One session (TSK-10, this device): its authorization and its tokens are
   revoked. Every other session (TSK-10, TSK-6): the account's authorizations other than the
   current one, and their tokens. Every session (TSK-9): every authorization of the account
   (`IOpenIddictAuthorizationManager.RevokeBySubjectAsync`) and every token
   (`IOpenIddictTokenManager.RevokeBySubjectAsync`). The API refuses their access tokens from its next
   request (ADR-ARCH-043 decision 2); their refresh tokens are refused at once.
6. **Signing keys.**
   - ES256 (P-256) keys in X.509 certificates, generated by the maintainer, never development
     certificates, each valid 12 months, each new one expiring after the one it replaces (OpenIddict
     prefers the furthest expiry; startup fails, ID0088, when no certificate is valid).
   - Encryption credentials: OpenIddict encrypts the tokens it keeps for itself (refresh tokens, by its
     defaults) with its encryption credentials; they are an RSA certificate, kept, stored and rotated
     like the signing ones. Which credentials protect a refresh token is read in OpenIddict's source by
     the delivery; the rule below covers both kinds either way.
   - Stored as a secret of the identity host's Doppler config (`saas-btp-identity`, ADR-ARCH-042),
     never in the image or the repository, recorded in Bitwarden.
   - Rotation every 6 months. The next certificate is added with a NotBefore at least 30 minutes after
     its deployment: from that deployment the JWKS publishes it while the current one keeps signing.
     Once its NotBefore has passed, the identity host is restarted, which sorts the credentials again
     and makes it sign. The former certificate stays registered, for validation only, until the last
     token it protected has expired: 30 days and 15 minutes after that restart, the refresh tokens'
     absolute lifetime plus a margin. A device offline during a rotation therefore keeps its session.
     The same applies to the encryption certificate.
   - Compromise: the key is removed and the host redeployed with the next certificate valid at once;
     every authorization is revoked, so every person signs in again.
7. **Pruning.** Revoked and expired tokens and authorizations are pruned with OpenIddict's pruning,
   older than 14 days (ADR-ARCH-053 decision 6).

## Consequences
- A person signs in at least every 30 days, with a code: at most one SMS a month per active phone-only
  device, within ADR-ARCH-045's costs.
- The API's refusal of an ended session is immediate; a token of an active session is accepted for
  its whole 10 minutes.
- Rotation depends on a restart after NotBefore, because the order is fixed when the options are
  configured (in the code read; whether OpenIddict re-sorts on an options reload is not verified). The
  delivery tests the sequence: the next key is published before it signs; after the restart it signs;
  tokens of the former key stay accepted until it is removed.
- The rotation is the maintainer's operation (ADR-ORG-004 §4 for production), every 6 months.

## Alternatives considered
- **OpenIddict's default lifetimes (1 hour, 14 days, sliding).** Rejected: a 14-day sliding refresh
  makes a session endless while it is used, beyond NIST's 30 days at AAL1, and ends a session after
  14 days offline, shorter than the 30 days decided here.
- **An inactivity timeout.** Rejected: a field device can stay offline for days (BP002-R13); the
  absolute 30 days bounds the session.
- **Raw ECDSA keys instead of certificates.** Rejected: OpenIddict orders certificates by validity
  dates, which this rotation relies on; with raw keys, which one signs follows the registration order
  only.
- **Encrypted access tokens with the key shared with the API.** Rejected: the API would hold a key of
  the identity service.

## References
Read on 2026-11-27:
- OpenIddict: https://documentation.openiddict.com/configuration/encryption-and-signing-credentials;
  https://documentation.openiddict.com/configuration/token-formats;
  https://documentation.openiddict.com/guides/migration/50-to-60;
  https://documentation.openiddict.com/integrations/quartz; source
  https://github.com/openiddict/openiddict-core/tree/7.9.0 (`src/OpenIddict.Server/OpenIddictServerOptions.cs`,
  `OpenIddictServerBuilder.cs`, `OpenIddictServerConfiguration.cs`, `OpenIddictServerHandlers.cs`,
  `OpenIddictServerHandlers.Protection.cs`, `OpenIddictServerHandlers.Discovery.cs`).
- NIST SP 800-63B-4 (July 2025), https://pages.nist.gov/800-63-4/sp800-63b.html: AAL1 reauthentication;
  §5 session management.

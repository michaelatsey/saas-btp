# ADR-ARCH-042 — BP-002's identity provider is an identity service of the platform's own, on ASP.NET Core Identity and OpenIddict; Supabase Auth no longer signs anyone in

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope: "The identity provider for BP-002, and what in the domain stays independent
of it"); parent #163 (BP-002); `docs/business-processes/BP-002-access-platform/`
(`process.md`, `acceptance.md`); `docs/domain/access.md` (section Identity); ADR-ARCH-019;
ADR-ARCH-020; ADR-ARCH-022; ADR-ARCH-025; ADR-ARCH-026; ADR-ARCH-028; ADR-ARCH-030; ADR-ARCH-031;
ADR-ARCH-032; ADR-ARCH-033; ADR-ARCH-034; ADR-ARCH-038; ADR-ARCH-039; ADR-ARCH-041;
ADR-ARCH-043 to ADR-ARCH-053; ADR-ORG-004.

## Context
BP-002 is validated at the business level (#247, PR #250). It leaves the identity provider to this
work, and asks what in the domain stays independent of it (#248, Scope). The stack uses Supabase,
Auth included (ADR-ARCH-026), and ADR-ARCH-038 validates Supabase Auth's tokens; no ADR records
Supabase Auth as BP-002's provider.

BP-002 requires, among others:
- an account that holds several contacts, two phone numbers included, each usable to sign in
  (`acceptance.md`, BP002-AC-48; `process.md`, section "Recovery situations", "No e-mail
  address");
- identical answers to a code request, a wrong code, an expired code and a lock, whether or not an
  account holds the contact (BP002-R2; BP002-AC-06, AC-19);
- no account created before the person, having proved the contact, confirms its creation (TSK-2;
  BP002-AC-05);
- wrong codes counted and locked per contact (BP002-R3; BP002-AC-16, AC-18, AC-20);
- every session of an account ended at once (TSK-6, TSK-9, TSK-10);
- a recovery delay, the suspension of the means a recovery removes, and a stop from the notice
  without signing in (BP002-R8 to BP002-R13).

What the providers document, read on 2026-11-27 (official pages; `[SRC]` marks a fact read in
source code, which shows behaviour, not a documented commitment):

- **Supabase Auth.**
  - A user has one `email` ("The user's email address.") and one `phone` ("The user's phone
    number."); phone and e-mail are unique across users (`phone_exists`, `email_exists`). Identity
    linking covers OAuth identities: "Supabase Auth automatically links identities with the same
    email address to a single user". No page read documents two phone numbers, or two e-mail
    addresses, that each sign in to one user.
  - Code verification is limited per IP address only: "Verification requests | `/auth/v1/verify` |
    IP Address | Yes | Defaults to 30 requests per 5 minutes". No per-code or per-user attempt limit
    is documented; `[SRC]` `internal/api/verify.go` (supabase/auth `master` 0725a1d, release v2.199.0)
    has no attempt counter. No hook runs on a sign-in code's verification: the attempt hooks cover
    MFA and passwords. `[SRC]` the CAPTCHA check covers `/otp`, `/signup`, `/recover`, `/resend`,
    `/magiclink` and `/token`, not `/verify`.
  - Whether the answers reveal an account (BP002-R2) is not stated by the documentation; community
    reports are not relied on.
  - Sessions: sign-out has the scopes `global`, `local` and `others`, through the user's own token;
    "Access Tokens of revoked sessions remain valid until their expiry time". Signing out every
    session of a user by user identifier is not documented on the pages read. Time-boxed sessions,
    inactivity timeouts and a single session per user require the Pro plan or higher.
  - Third-party authentication: the Data API, Storage, Realtime and Functions can trust JWTs of a
    "Custom OIDC provider, for any other provider that supports OpenID Connect". Requirements: "It
    exposes an OIDC Issuer Discovery URL, served at `<issuer>/.well-known/openid-configuration`."
    "It signs JWTs with an asymmetric key and includes a `kid` header parameter." "Its JWTs contain
    a `role` claim." "The issuer URL must use HTTPS." "`auth.uid()` casts `sub` to `uuid`". And: "It
    is not possible to disable Supabase Auth at this time."
- **Ory Kratos** (open source, Apache-2.0, release v26.2.0). It sends codes by SMS through a
  generic HTTP courier and supports passkeys and lookup secrets. "It is possible to have multiple
  identifiers", but the identity-schema page shows arrays for e-mail identifiers only: two phone
  numbers each signing in by SMS code is not documented. Its wrong-code cap
  (`selfservice.methods.code.max_submissions`, "The default is `5`") is not documented as per
  identifier, and per-identifier throttling is stated for "Ory Network and Ory Enterprise License
  deployments"; "When self-hosting the open source Ory Kratos Identity Server, it's the
  responsibility of the administrator to implement and manage rate limiting". A recovery delay,
  suspension and notices are buildable only through web hooks and the admin API. It is one more
  service to deploy, upgrade and monitor.
- **Firebase Authentication / Identity Platform**: one `phoneNumber` per user ("The user's primary
  phone number"); no limit on wrong codes documented; data stored in the United States. **ZITADEL**:
  one e-mail and one phone field per user. **Keycloak**: no one-time code by SMS or e-mail without
  an extension.
- **ASP.NET Core Identity, .NET 10** (Microsoft Learn and `dotnet/aspnetcore` `release/10.0`,
  500a5c64):
  - `AddIdentityCore` registers the user manager, validators, password hasher, normalizer and
    claims factory, and no cookie scheme or sign-in manager `[SRC]`.
  - A user has one `PhoneNumber` and one `Email` `[SRC]`.
  - The phone and e-mail code providers derive a 6-digit code from the security stamp, with a
    3-minute time step accepted from two steps before to two after `[SRC]`: a code is valid 6 to 9
    minutes, may be used any number of times within that window, and a new request returns the same
    code within a step instead of replacing it `[SRC]`. The code is keyed on the user's single phone
    number or e-mail.
  - Lockout counts failures per user (`AccessFailedCount`) `[SRC]`; `VerifyUserTokenAsync` counts
    none `[SRC]`; "We don't protect against brute force attacks since codes are expected to be
    random" (recovery-code sign-in) `[SRC]`.
  - Recovery codes: `CreateTwoFactorRecoveryCode` is `protected virtual` `[SRC]`; the default EF
    store keeps the codes in plain text, joined by ';', in `AspNetUserTokens` `[SRC]`; a custom
    `IUserTwoFactorRecoveryCodeStore<TUser>` receives the plain codes and the candidate `[SRC]`.
  - Passkeys: "No built-in 2FA support: Passkeys are treated as a primary authentication factor,
    not as a second factor."
  - `MapIdentityApi`: "The tokens aren't standard JSON Web Tokens (JWTs)" and it "isn't intended to
    be a full-featured identity service provider or token server".
- **OpenIddict** 7.9.0 (Apache-2.0, published 2026-11-14, targets net10.0): OAuth 2.0 and OpenID
  Connect server, discovery (`/.well-known/openid-configuration`), JWKS (`/.well-known/jwks`),
  refresh-token rotation, revocation, EF Core stores. A custom grant is registered with
  `AllowCustomFlow` and its permission; the token endpoint can be handled by the application
  (`EnableTokenEndpointPassthrough`). The documentation gives no server-side guide for a custom
  grant beyond these. It supports only its latest stable version, for sponsors or contributors.
  7.9.0 fixes GHSA-cxgj-hwwh-855g (`EnableAuthorizationEntryValidation()` not enforced without
  token entry validation, OpenIddict.Validation before 7.9.0).

No provider read meets BP-002's account model as documented. Every one leaves the recovery delay,
the suspension and the stop to code of ours.

## Decision
1. **An Identity context of the platform.** BP-002's identity provider is an identity service the
   platform builds and operates:
   - the module `apps/api/src/modules/Identity/`, structured as ADR-ARCH-034 decides, with the
     single entry point `IdentityModule`;
   - its own host, `apps/api/src/hosts/SaasBtp.Identity` (ADR-ARCH-020). The API host never
     references the Identity module. A separate host keeps the signing keys (ADR-ARCH-044) and the
     delivery credentials (ADR-ARCH-045) out of the API process;
   - the schema `identity` in the shared database, under ADR-ARCH-053;
   - its own Doppler project, `saas-btp-identity` (`stg`, `prd`), under ADR-ARCH-025's rule of one
     project per role.

   It owns the account, its contacts, its stronger factor, its sessions, the codes, the notices,
   the recoveries and their records.
2. **Building blocks.** The service is built on ASP.NET Core Identity (.NET 10) and OpenIddict. No
   cryptographic primitive, hash scheme, token format or protocol of our own is written. Each block
   is used where it fits, and where it does not, the reason is stated:

   | Need | Block | Fit |
   |---|---|---|
   | The account | Identity user, through `AddIdentityCore`, with a UUID identifier that is the `sub` | Fits. No cookie sign-in for persons: OpenIddict issues their tokens. The operator console alone adds the sign-in manager and a cookie (ADR-ARCH-050). |
   | Hashing short secrets stored (sign-in codes, stop codes, the factor's codes) | Identity's `IPasswordHasher<TUser>` | Fits. |
   | Hashing the 128-bit stop-link secret | .NET's `SHA256`, unsalted, so that it can be looked up (ADR-ARCH-049) | Fits: a 128-bit random secret needs no password hashing. |
   | Random secrets | .NET's `RandomNumberGenerator` | Fits. |
   | E-mail comparison | Identity's `ILookupNormalizer` | Fits (ADR-ARCH-052). |
   | The stronger factor and its lock | Identity's recovery-code API, `CreateTwoFactorRecoveryCode` overridden, a store of ours that hashes; Identity's lockout for the factor's account-wide tier, beside a per-session count of ours | Fits with the override and the store (ADR-ARCH-048). Identity's default store keeps plain text. Its per-user lockout alone would let another session keep the factor locked, hence the per-session count (ADR-ARCH-047). |
   | Operator accounts | Identity passkeys, a primary factor | Fits (ADR-ARCH-050). |
   | Contacts | A table of ours: the account, the kind, the normalized value, when confirmed; unique on the normalized value (INV-ACCESS-10) | Identity does not fit: one `PhoneNumber` and one `Email` per user. |
   | The sign-in code | A challenge record of ours, keyed by the normalized contact: the code's hash by Identity's hasher, its expiry, whether it was used, the contact's failure count | Identity's code providers do not fit: a code valid 6 to 9 minutes, reusable, not replaced by a new one, bound to an existing user's single phone or e-mail, while BP002-R1 makes a code single-use and replaced by the next, and BP002-R2 sends a code before any account exists. Identity's lockout does not fit either: it is per user, BP002-R3 is per contact. This is the one store of a code that is not Identity's; it stores a hash only, made by Identity's hasher. |
   | Tokens, discovery, JWKS, refresh, revocation | OpenIddict 7.9.0 or later, its EF Core stores in schema `identity` | Fits (ADR-ARCH-044). |
   | The sign-in exchange | An OpenIddict custom grant (contact and verified code for tokens), registered with `AllowCustomFlow` and handled in the token endpoint's pass-through | Fits as registered. OpenIddict gives no server-side guide, so the delivery proves the exchange by tests. |

3. **How each requirement that ruled Supabase Auth out is implemented and verified.** No
   requirement is met by the choice of a block; each is code of ours, verified by the tests named
   by the scenario (`issues-convention.md` §12):

   | Requirement | Mechanism | Verification |
   |---|---|---|
   | Two phone numbers, each signing in (BP002-AC-48) | The contacts table; a sign-in resolves the account through the contact | BP002-AC-48 |
   | Identical answers before the contact is proved (BP002-R2) | One answer for a code request, a wrong code, an expiry and a lock, built without reading whether an account holds the contact; the account is looked up only after a correct code | BP002-AC-06, AC-19, AC-56, comparing whole responses with and without an account |
   | No account before the person confirms (TSK-2) | A correct code proves the contact and creates nothing; the account is created on the person's explicit confirmation, holding that contact, confirmed | BP002-AC-01, AC-02, AC-05 |
   | Lock per contact (BP002-R3) | The challenge's failure count (ADR-ARCH-047) | BP002-AC-16, AC-18, AC-20 |
   | Every session ended (TSK-6, TSK-9, TSK-10) | Revocation of the account's authorizations and the API's session check (ADR-ARCH-043, ADR-ARCH-044) | BP002-AC-36, AC-52, AC-61; the host tests of ADR-ARCH-043 |
   | Recovery delay, suspension, stop (BP002-R8 to R13) | ADR-ARCH-049, ADR-ARCH-056 | The scenarios listed there |

4. **What changes in ADR-ARCH-031 §2.** "The person is external to the product" and
   "authentication is not a bounded context of the product" are superseded: the account, its
   contacts and its stronger factor belong to the Identity context of the platform. The identity
   provider still establishes who a person is; Access still knows a person only through their
   profile, and owns no account. The rest of ADR-ARCH-031 holds.
5. **Supabase Auth no longer signs anyone in.** Supabase stays for PostgreSQL. Nothing relies on
   Supabase Auth's tokens or claims any more, Storage included; no policy calls `auth.uid()`
   (already ADR-ARCH-032 §3). Choosing Storage's access mode is outside BP-002. If a client ever
   needs direct access to Storage or the Data API, the documented route is Supabase's "Custom OIDC
   provider", with the requirements quoted in the Context; that would be a decision of its own.
6. **The domain stays independent.** Modules read the caller through the port of ADR-ARCH-038
   decision 5 (`ICaller`), from `sub` and `contact_confirmed` (ADR-ARCH-043). No module other than
   Identity references a type of OpenIddict or ASP.NET Core Identity. Access's concepts and
   invariants, INV-ACCESS-10 and INV-ACCESS-11 included, are stated in business terms and do not
   depend on the blocks.
7. **When it applies.** From the deployment of the identity service. Until then BP-001 runs on what
   ADR-ARCH-038 and ADR-ARCH-041 decided.

## Consequences
- The platform carries the security of an identity service:
  - signing-key custody and rotation (ADR-ARCH-044);
  - patching OpenIddict and ASP.NET Core Identity: their advisories are watched, and OpenIddict is
    kept on its latest stable version, the only one it supports; 7.9.0 is the minimum;
  - records of sign-ins, changes of means, recoveries and stops (ADR-ARCH-050);
  - limits and abuse protection (ADR-ARCH-047, ADR-ARCH-055, ADR-ARCH-056);
  - the delivery providers' credentials, in the identity host's Doppler config (ADR-ARCH-045);
  - backups of schema `identity`, with the database's;
  - an incident path for a key compromise: remove the key and revoke every authorization
    (ADR-ARCH-044).

  An external security review of the identity service before production is recommended; this ADR
  does not decide it.
- The identity host is a second runtime image and a second Coolify application. ADR-ARCH-022,
  ADR-ARCH-030 and ADR-ARCH-039 decide the build, attestation, publication, deployment and health
  check of the API image and the runner only; the delivery extends them to the identity host by new
  ADRs.
- Supabase Auth cannot be disabled. With the deployment, the maintainer turns off sign-ups and every
  provider of each project's Auth (ADR-ORG-004 §4 for production). That setting is not what
  protects the data, and no check watches it: what protects it is that `anon` and `authenticated`
  hold no privilege on `identity` nor on any application schema (ADR-ARCH-028 §5, extended to
  `identity` by ADR-ARCH-053), which the delivery tests.
- No environment holds real users (the maintainer, 2026-11-27, in the /fix-issue 248 session). The environments are
  therefore reset when the identity service is deployed; no import of Supabase Auth users is built.
  A profile that referenced a Supabase Auth user identifier does not survive the reset.
- `process.md` (Boundary; "Decision discovery (step 6)") and `docs/domain/access.md` (section
  Identity) cite ADR-ARCH-031 §2 for the sentence decision 4 supersedes. They are not changed here
  (#248, Out of scope), nor is BP-001's `contract.md`, which cites it too; their amendment is #251,
  which blocks #249, so that the contract does not consume a superseded reference
  (`issues-convention.md` §12).
- Architecture tests of ADR-ARCH-034 are extended by the delivery: no module other than Identity
  references OpenIddict or ASP.NET Core Identity; the API host does not reference the Identity module.

## Alternatives considered
The identity providers considered, each with what its own documentation states, read on 2026-11-28,
and the BP-002 requirement that rules it out. Four requirements decide: BP002-R2 (the same answers
whether or not an account holds the contact), BP002-R3 (a lock per means, the other means still
working), BP002-R10 (the means a recovery removes suspended until it takes effect or is stopped) and
BP002-AC-48 (an account holding two phone numbers, each signing in by SMS code). No provider's pages
show all four met together, out of the box or through a documented extension point; R10's delayed,
stoppable recovery is documented by none. "Unverified" marks a statement no official page was found
to state; community answers were not used.

| Provider | What its documentation states | Ruled out by |
|---|---|---|
| Microsoft Entra External ID | "SMS is available at an additional cost for second-factor verification and for self-service password reset in external tenants. It isn't currently supported for first-factor authentication."; "Phone number as first factor: Not supported" | BP002-AC-48: no sign-in by SMS code |
| Amazon Cognito | `phone_number` is one standard attribute, alias values unique; "Users can either have MFA or sign in with passwordless factors, with one exception: passkeys with user verification can satisfy MFA requirements"; failed attempts "lock out your user" | BP002-AC-48 (one phone per user); BP002-R3 (the lock is per user) |
| Firebase Authentication | "Each user must have a unique phoneNumber"; a provider already linked cannot be linked again; a failed-code limit for phone sign-in is not stated (unverified) | BP002-AC-48 (one phone per user) |
| Auth0 | "The brute force threshold is the number of incorrect login attempts allowed from a single IP address to a single user identifier", default 10; "Only three failed attempts to input any single one-time-use code are allowed. After this, a new code will need to be requested."; that nothing blocks beyond that is unverified, its brute-force page speaking of unblocking "an OTP account"; code requests limited per IP | BP002-R3 (no lock per contact with the other means kept, no limit of requests per contact); BP002-AC-48 not documented |
| Supabase Auth | One `phone` field per user, unique; code verification limited "By IP Address"; the attempt hooks cover MFA and passwords only | BP002-AC-48 (one phone); BP002-R3 (no limit per contact) |
| Keycloak (26.8.2) | Built-in second factors "passkey, recovery codes and TOTP/HOTP"; no SMS code; its authentication SPI "is an internal SPI ... any new release of Keycloak can change it without prior notice"; brute-force detection can "disable a user account" | BP002-R1 needs an extension on an internal SPI; BP002-R3 (the lock is per account) |
| ZITADEL | "OTP SMS will enable the user to verify an OTP with the latest verified phone number"; one `phone` per user; OTP SMS is a second factor only; "When the user has reached the maximum (T)OTP attempts the account will be locked" until "the administrator" unlocks it | BP002-AC-48 (one phone); BP002-R3 (an account lock with no end) |
| Logto | "primary_phone is the user's phone number, used for sign-in"; one phone; identifier lockout "for 60 minutes after 100 failed authentication attempts" | BP002-AC-48 (one phone) |
| Better Auth | One `phoneNumber` per user, unique; 3 attempts per code, then a new code; rate limits "per client IP address"; "authentication and authorization framework for TypeScript", no .NET host documented | BP002-AC-48 (one phone); BP002-R3 (no lock per contact); a JavaScript service of its own beside the .NET hosts |
| Ory Kratos (self-hosted) | "It is possible to have multiple identifiers", shown with e-mail addresses only; one-time codes "via email and SMS"; a wrong code "too many times" fails the flow (default 5); throttling "per submitted identifier" on Ory Network and Enterprise License only, rate limiting otherwise "the responsibility of the administrator"; recovery gives "a privileged session" at once | BP002-AC-48 and BP002-R10 not documented; BP002-R3 per contact not in the open-source edition; one more service to run, the delay, suspension and notices still ours |
| Clerk | "They can have more than one registered email address or phone number"; "lock user accounts after 10 failed sign-in attempts and require a one-hour cooldown period before anyone can attempt to sign in to that account again", phone codes included | BP002-R3: the whole account is locked, the other means with it |
| Stytch | `phone_numbers`, "An array of phone number objects linked to the User"; "their account will be temporarily locked and future authentication attempts will be denied", lockout "at the user level, regardless of whether they're logging in via email magic link, passkeys, or something else" | BP002-R3: the whole user is locked |
| Descope | The statement that SMS goes only through Twilio Verify or Descope is contradicted: a "Generic SMS Gateway connector allows you to send SMS messages using your own API", beside Twilio, AWS SNS, Vonage and others; its SMS sign-in endpoint "will return a failure code if the phone number is not yet registered" | BP002-R2: the answer tells a registered number from another |
| Frontegg | "SMS login offers users the option to log in with an SMS code"; one phone per user ("set or update a user's phone number"), more is unverified; brute-force lockout for passwords only, per user | BP002-AC-48 and BP002-R3 not documented |
| SuperTokens | Account linking: "This feature is only available to paid users"; backend SDKs "for Node.js, Python, and Golang"; 5 attempts per code, then a new code | No .NET backend SDK: a service of its own in another runtime; BP002-R3 not documented |
| FusionAuth | Since 1.59.0, "phone number as a first-class identity type"; `phoneNumber`, "The User's primary phone number", a single value; lockout: "All login paths will be locked"; the lock message "Your account has been locked" | BP002-AC-48 (one phone documented); BP002-R3 (all paths locked); BP002-R2 (the lock message reveals the account by default) |

Beside the providers:
- **ASP.NET Core Identity's `MapIdentityApi`.** Rejected: its tokens are not JWTs and it is not a
  token server.
- **Token issuance written by hand.** Rejected: the maintainer's direction of 2026-11-27 forbids
  hand-rolling cryptography, code storage or token protocols.

## References
Read on 2026-11-28 (the providers considered, Alternatives):
- Microsoft Entra External ID:
  https://learn.microsoft.com/en-us/entra/external-id/customers/concept-multifactor-authentication-customers;
  https://learn.microsoft.com/en-us/entra/external-id/customers/concept-supported-features-customers.
- Amazon Cognito: https://docs.aws.amazon.com/cognito/latest/developerguide/user-pool-settings-attributes.html;
  https://docs.aws.amazon.com/cognito/latest/developerguide/user-pool-settings-mfa.html.
- Firebase: https://firebase.google.com/docs/auth/admin/errors;
  https://firebase.google.com/docs/reference/android/com/google/firebase/auth/FirebaseUser;
  https://docs.cloud.google.com/identity-platform/quotas.
- Auth0: https://auth0.com/docs/secure/attack-protection/brute-force-protection;
  https://auth0.com/docs/authenticate/passwordless/best-practices.
- Supabase: https://supabase.com/docs/guides/auth/users; https://supabase.com/docs/guides/auth/rate-limits;
  https://supabase.com/docs/guides/auth/auth-hooks.
- Keycloak: https://www.keycloak.org/docs/latest/server_admin/index.html;
  https://www.keycloak.org/docs/latest/server_development/index.html.
- ZITADEL: https://zitadel.com/docs/reference/api/user/zitadel.user.v2.UserService.AddOTPSMS;
  https://zitadel.com/docs/guides/integrate/login-ui/login-app;
  https://zitadel.com/docs/guides/manage/console/default-settings.
- Logto: https://docs.logto.io/user-management/user-data; https://docs.logto.io/security/identifier-lockout.
- Better Auth: https://www.better-auth.com/docs/plugins/phone-number;
  https://www.better-auth.com/docs/concepts/rate-limit; https://www.better-auth.com/docs/introduction.
- Ory: https://www.ory.com/docs/kratos/manage-identities/customize-identity-schema;
  https://www.ory.com/docs/kratos/passwordless/one-time-code;
  https://www.ory.com/docs/identities/sign-in/code-submission-limit;
  https://www.ory.com/docs/kratos/concepts/security;
  https://www.ory.com/docs/kratos/self-service/flows/account-recovery-password-reset.
- Clerk: https://clerk.com/docs/nextjs/reference/objects/user;
  https://clerk.com/docs/guides/protect/user-lockout.
- Stytch: https://stytch.com/docs/api-reference/consumer/api/otp/via-sms/send;
  https://stytch.com/docs/resources/policies/platform/user-locks.
- Descope: https://docs.descope.com/connectors/connector-configuration-guides/messaging/sms-gateway;
  https://docs.descope.com/auth-methods/otp/settings; https://docs.descope.com/api/otp/sms/sign-in.
- Frontegg: https://developers.frontegg.com/ciam/guides/authentication/credentials/sms;
  https://developers.frontegg.com/ciam/guides/security-center/security-rules/brute-force.
- SuperTokens: https://supertokens.com/docs/post-authentication/account-linking/introduction;
  https://supertokens.com/docs/references/backend-sdks/reference.
- FusionAuth: https://fusionauth.io/docs/release-notes; https://fusionauth.io/docs/apis/users/create;
  https://fusionauth.io/docs/security/account-protection/account-lockout;
  https://fusionauth.io/docs/security/account-protection/account-enumeration.

Read on 2026-11-27:
- Supabase: https://supabase.com/docs/guides/auth/users;
  https://supabase.com/docs/guides/auth/identities;
  https://supabase.com/docs/guides/auth/auth-identity-linking;
  https://supabase.com/docs/guides/auth/debugging/error-codes;
  https://supabase.com/docs/guides/auth/rate-limits; https://supabase.com/docs/guides/auth/auth-hooks;
  https://supabase.com/docs/guides/auth/auth-captcha; https://supabase.com/docs/guides/auth/sessions;
  https://supabase.com/docs/guides/auth/signout;
  https://supabase.com/docs/reference/javascript/auth-admin-signout;
  https://supabase.com/docs/guides/auth/third-party/overview;
  https://supabase.com/docs/guides/auth/third-party/custom-oidc; https://supabase.com/pricing;
  https://github.com/supabase/auth (`master` 0725a1d, `internal/api/verify.go`, the CAPTCHA
  middleware).
- Ory: https://www.ory.com/docs/kratos/passwordless/one-time-code;
  https://www.ory.com/docs/kratos/emails-sms/sending-sms;
  https://www.ory.com/docs/kratos/manage-identities/customize-identity-schema;
  https://www.ory.com/docs/identities/sign-in/code-submission-limit;
  https://www.ory.com/docs/kratos/concepts/security;
  https://www.ory.com/docs/kratos/hooks/configure-hooks; https://www.ory.com/pricing;
  https://github.com/ory/kratos.
- Firebase: https://firebase.google.com/docs/reference/admin/node/firebase-admin.auth.userrecord;
  https://firebase.google.com/docs/auth/web/phone-auth; https://firebase.google.com/support/privacy.
  Keycloak: https://www.keycloak.org/docs/latest/server_admin/index.html.
- Microsoft Learn (aspnetcore-10.0): "Identity management solutions for .NET web apps"
  (https://learn.microsoft.com/aspnet/core/security/identity-management-solutions); "Use Identity
  to secure a Web API backend for SPAs"
  (https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0);
  "Enable passkeys"
  (https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0);
  `AddIdentityCore`; "Custom storage providers for ASP.NET Core Identity". Source:
  https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/ (500a5c64:
  `UserManager.cs`, `TotpSecurityStampBasedTokenProvider.cs`, `Rfc6238AuthenticationService.cs`,
  `UserStoreBase.cs`, `SignInManager.cs`).
- OpenIddict: https://documentation.openiddict.com/introduction;
  https://documentation.openiddict.com/guides/choosing-the-right-flow;
  https://documentation.openiddict.com/configuration/application-permissions;
  https://documentation.openiddict.com/guides/getting-started/creating-your-own-server-instance;
  https://www.nuget.org/packages/OpenIddict.AspNetCore;
  https://github.com/openiddict/openiddict-core/releases/tag/7.9.0;
  https://github.com/openiddict/openiddict-core/security/advisories (GHSA-cxgj-hwwh-855g).

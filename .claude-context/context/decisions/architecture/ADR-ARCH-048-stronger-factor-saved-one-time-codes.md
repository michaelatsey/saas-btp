# ADR-ARCH-048 — The stronger factor is a set of saved one-time codes; a proof serves one operation; an e-mail code is not the unrestricted alternative

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope items 6 and 7; "Whether a code sent by e-mail can be the unrestricted
alternative of item 7"); BP-002 `process.md` (TSK-5, BP002-R3, BP002-R7, BP002-R8, BP002-R11,
BP002-D1, BP002-D2, BP002-D5, section "Recovery situations"), `acceptance.md`;
`docs/domain/access.md` (Stronger factor, INV-ACCESS-11); ADR-ARCH-042; ADR-ARCH-047;
ADR-ARCH-049.

## Context
#248's item 7 requires a stronger factor that does not depend on a contact being reachable, works on
the field device with gloves, and works without a lost phone if it is to help recover one, and that is
the unrestricted alternative of NIST SP 800-63B-4 §3.2.9 (BP002-D2). Item 6 asks how recent a proof of
it must be for an operation (INV-ACCESS-11, level 2: "proved for one operation, never held for a
session"). The process does not rely on the factor for a lost phone with its SIM card in a thief's
hands (`process.md`, "Recovery situations"); it is the remedy when the new holder of a recycled number
stops every recovery that removes it (`process.md`, "Residual risks of the self-service recovery").

What the sources state, read on 2026-11-27:
- NIST SP 800-63B-4 (https://pages.nist.gov/800-63-4/sp800-63b.html):
  - §3.2.9: "there is one restricted authenticator: the use of the PSTN for out-of-band
    authentication"; the CSP SHALL "Offer subscribers at least one alternative authenticator that is
    not restricted and can be used to authenticate at the required AAL" and "Provide subscribers with
    meaningful notice regarding the restricted authenticator's security risks and the availability of
    unrestricted alternatives".
  - §3.1.3.1: "Email SHALL NOT be used for out-of-band authentication"; and "Confirmation codes that
    are sent to validate email addresses or are issued as recovery codes (see Sec. 4.2.1.2) are not
    authentication processes and not affected by the above prohibition." (§4.2.1.2 is issued recovery
    codes; §4.2.1.1 is saved recovery codes, which "SHALL include at least 64 bits".)
  - §3.1.2 (look-up secrets): "A typical application of look-up secrets is for one-time saved recovery
    codes"; "A look-up secret is "something you have.""; "Look-up secrets are not phishing-resistant."
    §3.1.2.1: an "approved random bit generator"; "If delivered via an online session, the session
    SHALL be authenticated by the subscriber at AAL2 or higher"; "Look-up secrets SHALL be at least six
    decimal digits (or equivalent) in length." §3.1.2.2: "used successfully only once"; "All look-up
    secrets SHALL be stored in a hashed form using an approved hashing function"; shorter than 112 bits,
    "stored in a salted and hashed form using a suitable password hashing scheme", "The salt value SHALL
    be at least 32 bits in length", with a rate-limiting mechanism (§3.2.2).
  - Appendix B: syncable authenticators: "Authentication at AAL2 may be supported"; §2.3.2:
    "syncable authenticators SHALL NOT be used at AAL3".
- Android (developer.android.com, developers.google.com): passkeys "are supported on devices that run
  Android 9 (API level 28) or higher"; on a new device, "The user needs to sign in to their Google
  Account and enter their Android device screen lock or Google Password Manager PIN to decrypt a synced
  passkey"; "If your app requires non-discoverable FIDO credentials or uses physical security keys, you
  should continue to use FIDO at this time" (FIDO2 migration guide, last updated 2026-10-30), while the
  legacy FIDO2 API "supports the use of BLE, NFC, and USB roaming authenticators (security keys)".
- ASP.NET Core Identity, .NET 10: "No built-in 2FA support: Passkeys are treated as a primary
  authentication factor, not as a second factor."; the recovery-code API generates codes through
  `CreateTwoFactorRecoveryCode`, `protected virtual`, and its default store keeps them in plain text
  (`[SRC]`, ADR-ARCH-042).
- RFC 6238 (TOTP): the secret is shared with the device that computes the codes.
- `architecture.md`: the field client platform (PWA or Expo) is an open decision.

## Decision
1. **The factor.** A set of 10 one-time codes, each of 12 decimal digits, shown in three groups of
   four, which the person writes down or prints and keeps apart from the phone.
   - Generated through ASP.NET Core Identity's recovery-code API, with `CreateTwoFactorRecoveryCode`
     overridden to draw 12 digits from `RandomNumberGenerator` (ADR-ARCH-042).
   - Stored each salted and hashed with Identity's password hasher, through a recovery-code store of
     ours; never in plain text. The delivery confirms from the hasher's source that its salt is at least
     32 bits (§3.1.2.2).
   - Each code is accepted once. Registering the factor issues a set; replacing it issues a new set
     and voids the former; removing it voids the set (TSK-5, BP002-R7). When three codes or fewer
     remain, the person is told to replace the set.
   - Shown once, in the session that registers it, and never again.
2. **Each requirement of item 7.**

   | Requirement | How it is met | Verification |
   |---|---|---|
   | Does not depend on a contact being reachable | It is sent nowhere: the person holds it | BP002-AC-31, AC-33 (a lost contact, the factor used) |
   | Works on the field device with gloves | 12 digits typed on a numeric keypad | The field client's input, with the client platform |
   | Works without the lost phone | On paper, typed on any device signed in, or on a notice's stop page | BP002-AC-55 (stop with the factor from the notice) |
   | The unrestricted alternative of §3.2.9 | A look-up secret (§3.1.2), not restricted: only the PSTN is; delivered with the deviation of decision 4 | BP002-D2's notice names it as the alternative |

3. **NIST category.** It is a look-up secret (§3.1.2): 12 digits, about 39.9 bits, above
   "at least six decimal digits", below 112 bits, so salted and hashed with a password hashing scheme
   and rate-limited (ADR-ARCH-047 decision 3). It is an authenticator, not the saved recovery code of
   §4.2.1.1.
4. **A deviation from §3.1.2.1, accepted by the maintainer.**
   - The requirement (NIST SP 800-63B-4 §3.1.2.1, read on 2026-11-28): "If delivered via an online
     session, the session SHALL be authenticated by the subscriber at AAL2 or higher". The codes are
     shown in an online session that is not AAL2, so this requirement is not met.
   - Its reason: BP-002 authenticates at AAL1 and has no AAL2 means. A session is the single-factor
     session of BP002-D1, which already states that the process is not presented as conforming to this
     publication, and BP-002 has neither a password nor a biometric to make a second factor.
   - Its compensating controls, each a rule of BP-002 (TSK-5):
     - registering the codes requires proving every contact of the account now;
     - replacing or removing them requires the codes, or proving every contact of the account now
       (BP002-R7);
     - every change is notified to every contact of the account (BP002-R15).
   - The maintainer accepted this deviation on 2026-11-28 (#248, decision on finding G8 of the
     diff review), to be confirmed on the pull request.
5. **An e-mail code cannot be the unrestricted alternative.** §3.1.3.1: "Email SHALL NOT be used for
   out-of-band authentication". An e-mail address stays a contact (BP002-D1); it is not the stronger
   factor.
6. **How recent a proof must be (item 6).** A proof of the factor serves one operation
   (INV-ACCESS-11, BP002-AC-35): a successful proof creates a grant bound to the session, to the named
   operation and to its target, valid 5 minutes, consumed when the operation runs, void otherwise. A
   second operation needs a second proof. No process outside BP-002 requires level 2 yet, so no token
   claim carries it; a process that does decides how in its own ADR.
7. **Verification cost.** A proof compares the candidate with up to 10 hashes; it is bounded by the
   factor's limits of ADR-ARCH-047 decision 3 (5 failures retire a session's attempts; 10 a day per
   contact; 100 consecutive per account) and, on a notice's stop page, by ADR-ARCH-049 decision 4.

## Consequences
- A person who loses the paper and every contact can only use the assisted recovery (BP002-D3).
- A thief holding the paper and a contact holds the factor too; the process accepts that a contact's
  holder can act, and the factor adds a second thing to hold.
- The person is told, when the factor is registered, to keep the codes apart from the phone; the
  wording is the contract's.
- Passkeys or security keys may be added later as further factors; that is a new decision.

## Alternatives considered
- **Passkeys (synced, on the phone).** Rejected as the factor: on a new phone a synced passkey needs
  the Google account and the former device's screen lock or Google Password Manager PIN, so it does not
  reliably work without the lost phone; ASP.NET Core Identity's passkeys are a primary factor only; and
  the client platform is open. They fit the operators' desktop accounts (ADR-ARCH-050).
- **Security keys (NFC or USB).** Rejected now: a device to buy and hand to each person, and Android's
  guidance points physical keys to the legacy FIDO API rather than Credential Manager.
- **TOTP on the phone.** Rejected: its secret is lost with the phone.
- **A code sent by e-mail.** Rejected by §3.1.3.1 (decision 5).
- **Identity's default recovery codes** (11 characters `XXXXX-XXXXX` from a 26-character alphabet of
  letters and digits). Rejected: a full keyboard with gloves, and stored in plain text by the default
  store.

## References
Read on 2026-11-28: NIST SP 800-63B-4 §3.1.2.1 (decision 4), same address as below.

Read on 2026-11-27:
- NIST SP 800-63B-4 (July 2025), https://pages.nist.gov/800-63-4/sp800-63b.html: §2.3.2, §3.1.2,
  §3.1.3.1, §3.2.9, §4.2.1.1, Appendix B.
- Android: https://developer.android.com/identity/passkeys;
  https://developers.google.com/identity/passkeys/supported-environments;
  https://developer.android.com/identity/sign-in/fido2-migration;
  https://developers.google.com/identity/fido/android/native-apps.
- Microsoft Learn: "Enable passkeys"
  (https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0);
  `UserManager<TUser>.GenerateNewTwoFactorRecoveryCodesAsync`
  (https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.generatenewtwofactorrecoverycodesasync).
  Source: https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/ (500a5c64,
  `UserManager.cs`, `UserStoreBase.cs`).
- RFC 6238, https://www.rfc-editor.org/rfc/rfc6238.txt.

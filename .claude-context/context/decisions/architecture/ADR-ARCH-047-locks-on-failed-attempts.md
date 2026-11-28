# ADR-ARCH-047 — The locks on failed attempts: the lock per contact and the stronger factor's lock, sized against the lock-out of a phone-only person

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope item 3); BP-002 `process.md` (BP002-R2, BP002-R3, BP002-R11, TSK-2),
`acceptance.md`; ADR-ARCH-042; ADR-ARCH-046; ADR-ARCH-048; ADR-ARCH-049; ADR-ARCH-055.

## Context
#248 asks how many wrong codes or failed stronger-factor proofs lock a means and for how long, sized
against the lock-out exposure of a phone-only account (BP002-R3, item 3). BP002-R3 accepts that
"anyone can lock a contact by entering wrong codes for it, so a person whose only contact is a phone
number can be kept from signing in while the lock lasts", and that "a lock never prevents stopping a
recovery (rule BP002-R11)". BP002-R11 requires that "a lock of the contact (rule BP002-R3), or wrong
attempts made by anyone without that notice, never prevent its holder from stopping the recovery".
The protection against automated requests for codes (item 4) is ADR-ARCH-055's; how often a recovery
may be requested (item 9) is ADR-ARCH-056's.

What the sources state, read on 2026-11-27:
- NIST SP 800-63B-4 §3.2.2: "SHALL limit consecutive failed authentication attempts using a specific
  authenticator on a single subscriber account to no more than 100 by disabling that authenticator";
  other techniques MAY be used, among them a "bot detection and mitigation challenge", waits that
  increase "(e.g., 30 seconds up to an hour)", and risk signals such as the IP address.
  §3.1.3.2: "Generating a new authentication secret SHALL NOT reset the failed authentication count."
- ASP.NET Core Identity's lockout counts failures per user, and its recovery-code redemption counts
  none ([SRC], ADR-ARCH-042, Context).

## Decision
1. **The lock per contact (BP002-R3).**
   - Each contact has a consecutive failure count: a wrong code adds one; only a correct code for that
     contact returns it to zero. A new code (ADR-ARCH-046) and the end of a lock leave it as it is.
   - Each time the count reaches a multiple of 10, the contact is locked for 15 minutes: no code is
     sent to it, and no code is accepted for it, even the right one (TSK-2). There is no longer tier.
   - The answers during a lock are the same whether or not an account holds the contact (BP002-R2).
   - Brute force: at most 10 guesses per 15 minutes, 960 a day, against 10^8 codes of ADR-ARCH-046:
     below one chance in 100,000 a day for one contact, about 0.35% a year of continuous attack.
   - NIST §3.2.2's disabling at 100 consecutive failures is not applied: BP002-R3 makes the lock
     temporary, and disabling the contact would lock a phone-only person out for good. This is a
     stated deviation, like BP002-D1's.
2. **The lock-out exposure of a phone-only person, every limit on one contact together.** One action
   of someone else keeps them from signing in for at most 15 minutes (10 wrong codes), or at most one
   hour (5 code requests in an hour, ADR-ARCH-046). A longer lock-out needs that person to keep acting,
   and lasts as long as they do. Neither stops a recovery (decision 4, ADR-ARCH-049). Accepted under
   BP002-R3; a second contact avoids it (BP002-D2). The exposure that the platform-wide spending
   ceiling adds is ADR-ARCH-055's.
3. **The stronger factor's lock (BP002-R3).**
   - Within a session, after 5 failed proofs of the factor, that session can never try it again; a
     new sign-in is needed.
   - Per contact: when the sessions signed in with one contact have failed 10 proofs of the factor
     within 24 hours, no session signed in with that contact may try it for 24 hours. The sessions of
     the account's other contacts are not affected.
   - Across the account, 100 consecutive failed proofs, all sessions together, lock the factor for 15
     minutes; a successful proof returns the count to zero. This tier uses ASP.NET Core Identity's
     lockout (`MaxFailedAccessAttempts` 100, `DefaultLockoutTimeSpan` 15 minutes), on the factor only;
     the sign-in path never reads it. Our code counts each failure (`AccessFailedAsync`), since
     Identity's recovery-code redemption counts none.
   - Exposure: someone signed in with a contact of the account (the new holder of a recycled number,
     for example) can make at most 10 failed proofs a day through that contact, whatever the number of
     sessions they keep, and so can only block that contact's own sessions. To reach the account tier
     they would need 100 failures with no success in between, that is ten contacts' daily allowance;
     the person's own successful proof returns the count to zero. The person who fails 10 proofs
     through one contact uses another contact, or waits 24 hours. The person can also end every other
     session first (TSK-10).
   - From a notice's stop page, a failed proof of the factor counts for that notice alone (ADR-ARCH-049),
     never towards these locks.
   - Guessing: 10 codes of 12 digits (ADR-ARCH-048), one chance in 10^11 per attempt.
4. **The stop is outside these locks.** The stop of a recovery is never refused by decision 1 or 3;
   its own limits are per notice (ADR-ARCH-049), so that nobody without the notice can affect them
   (BP002-R11, BP002-AC-51).
5. **Verification.**

   | Requirement | Verification |
   |---|---|
   | Lock per contact, its count and its answers | BP002-AC-16, AC-18, AC-19, AC-20; the count kept across a new code and across a lock's end |
   | Factor lock per session and per account | BP002-AC-21; another session's failures do not stop the person's session |
   | Factor limits per contact | Another contact's failures do not block the person's contact |
   | The stop outside the locks | BP002-AC-51 |

## Consequences
- The counts and locks are kept in schema `identity` (ADR-ARCH-053), so that they hold across
  restarts.

## Alternatives considered
- **A longer lock tier (24 hours after 100 failures).** Rejected: about 2.5 hours of failures would lock
  a phone-only person out for a day.
- **Identity's per-user lockout alone for the stronger factor.** Rejected: someone signed in with
  another contact could keep the factor locked against the person.
- **Identity's per-user lockout for wrong sign-in codes.** Rejected: it locks the account, and
  BP002-R3 keeps the account's other means working.

## References
Read on 2026-11-27:
- NIST SP 800-63B-4 (July 2025), §3.2.2 and §3.1.3.2, https://pages.nist.gov/800-63-4/sp800-63b.html.
- Microsoft Learn, "Configure ASP.NET Core Identity"
  (https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0),
  lockout options.
- `dotnet/aspnetcore` `release/10.0` 500a5c64, `SignInManager.cs` (recovery-code redemption).

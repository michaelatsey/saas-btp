# ADR-ARCH-046 — The sign-in code: 8 decimal digits, valid 10 minutes, used once, replaced by the next, renewed after 60 seconds

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope item 2); BP-002 `process.md` (TSK-1, TSK-2, BP002-R1, BP002-R3),
`acceptance.md` (BP002-AC-12 to AC-15, AC-17); ADR-ARCH-042; ADR-ARCH-045; ADR-ARCH-047.

## Context
#248 asks for the code's form ("length, characters that can be typed with gloves"), its lifetime,
and how often a new one may be asked for. BP002-R1: a code is sent to one contact, works once,
expires after a limited time, and asking for a new code replaces the former one. BP002-R3 limits the
requests for codes to one contact. The person types on a phone outdoors, with gloves, on a poor
network (`process.md`, Intent).

NIST SP 800-63B-4 §3.1.3.2 (read 2026-11-27, https://pages.nist.gov/800-63-4/sp800-63b.html): the
secret has "at least six decimal digits", from an "approved random bit generator"; "the
authentication SHALL be considered invalid unless completed within 10 minutes"; "Verifiers SHALL
accept a given authentication secret as valid only once during the validity period"; "If the
authentication secret is less than 64 bits long, the verifier SHALL implement a rate-limiting
mechanism"; "Generating a new authentication secret SHALL NOT reset the failed authentication
count." BP002-D1 states that the process is not presented as conforming to this publication; it is
cited for the practices adopted.

## Decision
1. **Form.** 8 decimal digits, drawn from `RandomNumberGenerator` (ADR-ARCH-042). Digits only: they
   are typed on a numeric keypad, whose keys are larger than a full keyboard's, and do not require a
   switch of keyboard. Android's one-time-code autofill may spare the typing; it is not relied on.
   Eight rather than six, for the brute-force bound of ADR-ARCH-047.
2. **Lifetime.** 10 minutes from its sending. A code entered later is answered as expired, the same
   whether or not an account holds the contact (BP002-R2).
3. **Once, and replaced.** A code is accepted once. A contact has at most one live code: a new code
   for it voids the former one at once. The code is stored only as a hash (ADR-ARCH-042, the
   challenge record).
4. **Renewal.** A new code may be asked for a contact 60 seconds after the previous one, and at most 5
   times for that contact in any rolling hour. No daily limit per contact (ADR-ARCH-047 decision 2 sizes the
   exposure). A request beyond those limits is refused and answered as BP002-R2 requires, with when to
   try again.
5. **Failures carry over.** A new code does not reset the contact's failure count (ADR-ARCH-047).
6. **Verification.**

   | Requirement | Mechanism | Verification |
   |---|---|---|
   | A wrong code (BP002-AC-12) | Hash compared; failure counted | BP002-AC-12 |
   | An expired code (BP002-AC-13) | Expiry 10 minutes | BP002-AC-13, at 9:59 and 10:01 |
   | A code works once (BP002-AC-14) | Marked used on success | BP002-AC-14 |
   | A new code replaces the former (BP002-AC-15) | One live code per contact | BP002-AC-15 |
   | Too many codes asked (BP002-AC-17) | 60 s interval, 5 per rolling hour | BP002-AC-17, at the 6th request in an hour |

## Consequences
- A code that arrives late (more than 10 minutes on a poor network) is useless; the person asks for a
  new one after 60 seconds.
- A person who asks for codes repeatedly can reach the hourly limit; they are told when to try again.
- Every code request costs a message (ADR-ARCH-045).

## Alternatives considered
- **6 digits.** Rejected: with the lock of ADR-ARCH-047, the bound on guessing is a hundred times
  weaker, for two fewer digits to type.
- **Letters and digits.** Rejected: a full keyboard with gloves, and letters easily confused.
- **A longer lifetime.** Rejected: §3.1.3.2's 10 minutes is the practice adopted.
- **ASP.NET Core Identity's phone and e-mail code providers.** Rejected in ADR-ARCH-042: 6 digits, valid
  6 to 9 minutes, reusable within the window, not replaced by a new request.

## References
- NIST SP 800-63B-4 (July 2025), §3.1.3.2, https://pages.nist.gov/800-63-4/sp800-63b.html, read
  2026-11-27.
- ADR-ARCH-042 (the challenge record; Identity's code providers).

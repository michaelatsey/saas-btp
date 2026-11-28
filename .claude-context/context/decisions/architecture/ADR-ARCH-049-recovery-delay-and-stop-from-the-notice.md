# ADR-ARCH-049 — The recovery delay is 7 days; each notice of a pending recovery carries the way to stop it that applies to its contact, proved by a link and a code sent to that contact

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope items 8 and 11); BP-002 `process.md` (TSK-6, TSK-7, TSK-9, BP002-R8,
BP002-R10, BP002-R11, BP002-R12, BP002-R13, BP002-R14, BP002-R15, BP002-D3), `acceptance.md`;
ADR-ARCH-042; ADR-ARCH-045; ADR-ARCH-046; ADR-ARCH-047; ADR-ARCH-048; ADR-ARCH-055; ADR-ARCH-056.

## Context
#248's item 8 asks for the value of the recovery delay, "at least several days of offline work"
(BP002-R13). Item 11 asks how the stop reaches a contact with its notice and is proved without
signing in, "so that a lock of the contact or attempts by others never prevent it" (BP002-R11).

BP002-R11: during the delay, from the notice and without signing in, a recovery is stopped by whoever
proves a contact it suspends; the stronger factor, if it removes it; any contact it keeps, if it removes
the stronger factor; the requester of a self-service recovery may also cancel it while signed in.
"What proves the stop reaches each contact with its notice. Failed attempts at it are limited for that
notice alone". BP002-R15: each notice of a pending recovery says "how to stop it without signing in".
BP002-R10: a code sent to a suspended contact serves only to stop the recovery. The scenarios describe
the stop as "entering a code sent to E1, without signing in" (BP002-AC-41, AC-43, AC-54) and "by
proving her stronger factor" (BP002-AC-55); BP002-AC-36 has each contact, the requester's kept E1
included, receive "a notice of the pending recovery, with the way to stop it".

The maintainer's direction of 2026-11-27 on #248: "A request for a new stop code stays limited per
contact by a minimum interval (BP002-R3), never by a lock (BP002-R11)."

## Decision
1. **The delay.** 7 days, for the self-service and the assisted recovery alike, counted from the
   request. A recovery takes effect at the first run of the identity host's scheduler after its delay
   has passed, and BP002-R14 is applied again at that moment.
2. **What each notice of a pending recovery carries.** Every contact the account holds is notified at
   once (BP002-R15); each notice gives the way to stop that applies to its contact:

   | Contact | Way to stop in its notice |
   |---|---|
   | A contact the recovery suspends (every contact the recovery removes; every former contact in an assisted recovery) | The stop link of decision 3 |
   | A kept contact, when the recovery removes the stronger factor | The stop link of decision 3 |
   | Any other kept contact (the requester's own, for example) | Cancelling while signed in with a contact the recovery keeps (TSK-7), with where to do it; that the holder of each contact it removes can stop it from their own notice, without signing in; the operator's contact information and the dispute (TSK-8) |

   When the recovery removes the stronger factor, the stop page also accepts a proof of the factor
   (decision 4). Every notice also gives the operator's contact information and how to dispute
   (BP002-R15). The requester's cancel (TSK-7) is open to any session of the account signed in with a
   contact the recovery keeps.
3. **The stop link and the stop code.**
   - The link carries a random secret of 128 bits, from `RandomNumberGenerator`, unique to that notice
     and contact, valid until the recovery ends. It is stored only as its SHA-256 hash, unsalted and
     indexed, so that the page finds the notice by it in one lookup: at 128 bits, a salted password hash
     is not needed against offline guessing, unlike the short codes (ADR-ARCH-042).
   - It opens a page of the identity service, reachable without signing in. The page sends a stop code
     (8 digits, valid 10 minutes, ADR-ARCH-046's form) to that notice's contact, and stops the recovery
     when that code is entered. This is "entering a code sent to E1, without signing in".
   - A new stop code for that notice may be asked after a minimum interval, which starts at 60 seconds
     and doubles with each stop code sent for that notice, up to one hour; it is never a lock (the
     maintainer's direction): the holder can always ask again once the interval has passed. Over the 7
     days this allows at most about 170 stop codes per notice. Only the link's holder can make the
     interval grow. A wrong stop code is counted for that notice alone; after 5, that stop code is void
     and a new one is asked.
   - Nothing else applies to the stop: neither the contact's sign-in lock (ADR-ARCH-047 decision 1), nor
     the Turnstile check and the spending ceiling (ADR-ARCH-055 decision 6), nor the recovery limits
     (ADR-ARCH-056). Nobody without the notice's secret can request or try a stop
     code for it, so nobody's attempts can prevent the holder's stop (BP002-R11, BP002-AC-51).
   - Opened with an unknown, used or ended secret, the page says only that there is nothing to stop.
4. **The stronger factor on the stop page** (BP002-AC-55, BP002-R11). Where the recovery removes the
   factor, the page reached from a notice also accepts one of the factor's codes (ADR-ARCH-048). A wrong
   code counts for that notice alone: 5 failures make the page wait 15 minutes before the next attempt
   by factor for that notice. The code stop of decision 3 stays available meanwhile.
5. **The offer to stop at sign-in** (BP002-R10). A correct sign-in code for a suspended contact signs
   nobody in; it is answered with the offer to stop, and taking the offer stops the recovery. That
   correct code is the proof. This path follows the contact's sign-in lock; the notice's path does not.
6. **Effect, stop and cancel.** A stop, a cancel and an effect each lift or apply the recovery once, in
   one transaction of the Identity module; a recovery stopped and taking effect at the same moment ends
   in whichever commits first, the other answered that it is over. Each is notified (BP002-R15).
7. **Verification.**

   | Requirement | Verification |
   |---|---|
   | Delay of 7 days, effect after it | BP002-AC-36, AC-38, AC-39, AC-52, AC-58, just before and just after 7 days |
   | Suspended contacts refused at sign-in, offered to stop | BP002-AC-37, AC-53 |
   | Stop from the notice with a code sent to the contact | BP002-AC-41, AC-43, AC-54 |
   | Stop with the factor from the notice | BP002-AC-55 |
   | A lock or others' attempts never prevent the stop | BP002-AC-51; wrong codes and wrong secrets by others leave the holder's stop working |
   | Each notice gives the way that applies to it | BP002-AC-36 (P1: stop link; E1: cancel while signed in) |
   | Cancel by the requester | BP002-AC-45 |
   | BP002-R14 at the effect | BP002-AC-49, AC-50, AC-57 |

## Consequences
- A person offline for more than 7 days on a site can miss a recovery that is not theirs; the delay is
  sized for "several days" (BP002-R13), not for any absence.
- The stop needs a connection (BP002-R16) and a browser or the app to open the link.
- Each stop costs one notice of the stop plus at least one stop code, sent outside the spending
  ceiling (ADR-ARCH-055 decision 6).
- The identity host runs a scheduler for the effects of recoveries; its form is the contract's.

## Alternatives considered
- **A delay of 3 days.** Rejected: "several days" offline on a site would leave too little margin.
- **A delay of 14 days or more.** Rejected: a person who lost a phone waits that long to get a number
  back on their account.
- **A stop code printed in the notice, typed without a link.** Rejected: to be typed with gloves it would
  be short, so it would need an attempt limit, and anyone could then use the attempts of a notice they
  do not hold. The link's secret keeps the attempts to the holder.
- **A link that stops with one tap, no code.** Rejected: a notice forwarded or previewed by another app
  would stop the recovery; the code proves the contact at the moment of the stop, as the scenarios state.
- **A stop on every notice, the requester's kept contact included.** Rejected: BP002-R11 does not let a
  kept contact stop a recovery that keeps the stronger factor; its holder is the requester, who cancels.

## References
- BP-002 `process.md` and `acceptance.md` (validated 2026-11-24).
- The maintainer's direction on #248, 2026-11-27, point 4.

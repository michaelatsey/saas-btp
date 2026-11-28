# ADR-ARCH-051 — Captures per person on a shared device: each capture records its author, the device sends only the current person's, and the API refuses a capture whose author is not the caller

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope item 12); BP-002 `process.md` (TSK-10, TSK-11, BP002-D4), `acceptance.md`
(BP002-AC-62 to AC-64); BP-001 (BP001-R6); `architecture.md` §5; ADR-ARCH-043; ADR-ARCH-044.

## Context
#248's item 12 asks how the device keeps captures per person, and refuses to send them under another
person (BP002-D4). BP002-D4: a person whose session expires offline keeps capturing; what they captured
is sent only once the same person has signed in again; nothing a person captured is ever sent under,
nor shown to, another person who signs in on the same device. TSK-10: a person who signs out with
unsent captures is told so. The field client's platform is an open decision (`architecture.md`:
"Platform of the increment-1 field client (offline web PWA vs Phase 2 Expo app)"), and so is the
offline conflict-resolution strategy.

## Decision
1. **Each capture records its author.** When it is captured, a capture records the `sub` of the person
   signed in on the device at that moment (ADR-ARCH-043 decision 1, ADR-ARCH-038 decision 3). The
   author never changes.
2. **The device keeps captures by author.** It sends only the captures whose author is the `sub` of the
   current session, and shows only those. Another person's captures stay on the device, neither sent
   nor shown, until their author signs in again on it. A person who signs out with unsent captures is
   told so (TSK-10).
3. **The API's check, against a faulty client.** Every write of a capture carries its recorded
   author; the API refuses one whose author is not the caller's `sub`, and writes nothing of it. This
   catches a faulty client that sends another person's captures under the current session. It does not
   stop a modified client, which could rewrite the author field it sends.
4. **What waits for the client platform, and carries the guarantee against a modified client.**
   Against a modified client, or another person reading the device, BP002-D4 rests on per-person
   protection of the captures at rest: what a person captured is readable only with something of that
   person's session, so that another person signed in on the device can neither read it nor send it.
   Its form (encryption keyed per person, the device storage), and how long another person's captures
   are kept on a shared device, are decided with the client platform, under this ADR's rules; until
   that decision, BP002-D4 is guaranteed against a faulty client only.
5. **Verification.**

   | Requirement | Verification |
   |---|---|
   | Captures kept and sent once the same person signs in again | BP002-AC-62 (client test) |
   | Neither sent under nor shown to another person | BP002-AC-63 (client test); an API test refusing a capture whose author is not the caller |
   | Told when signing out with unsent captures | BP002-AC-64 (client test) |

## Consequences
- Every capture-writing endpoint of later processes carries the author check of decision 3; their
  contracts reference this ADR.
- A capture made before its author's first sign-in is impossible: capturing requires having been signed
  in on the device (TSK-11's precondition).
- The author of a capture is the account; a recovery keeps the account (`docs/domain/access.md`), so a
  person keeps their unsent captures through a recovery.

## Alternatives considered
- **The device's filtering alone.** Rejected: nothing would catch a faulty client sending another
  person's captures under the current session. A modified client is the concern of decision 4.
- **Discarding another person's captures at sign-in.** Rejected: BP002-D4 keeps them for their author.
- **Choosing the device storage now.** Rejected: it depends on the client platform, an open decision.

## References
- BP-002 `process.md` (BP002-D4, TSK-10, TSK-11) and `acceptance.md` (BP002-AC-62 to AC-64).
- `.claude-context/context/architecture/architecture.md` §5 and the open-decision table.

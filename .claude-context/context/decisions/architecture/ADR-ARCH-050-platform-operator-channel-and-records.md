# ADR-ARCH-050 — The platform operator: reached by a phone number and an e-mail address, working in an operator console signed in with a passkey; assisted recoveries and disputes recorded for 2 years

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope item 10); BP-002 `process.md` (actor "The platform operator", TSK-8, TSK-9,
BP002-R15, BP002-D3), `acceptance.md` (BP002-AC-52 to AC-60); ADR-ARCH-031 §6; ADR-ARCH-042;
ADR-ARCH-053.

## Context
#248's item 10 asks how the operator is reached, and how the record of an assisted recovery and of a
dispute is kept and for how long. BP-002: the operator "takes an assisted recovery and records
disputes. Changes no account on their own judgement"; their contact information is given in every
notice (BP002-R15) and is "reachable without signing in" (TSK-9). BP002-D3's record holds "who asked,
through which new contact, the operator who took the request, when, the means replaced, and the
outcome". ADR-ARCH-031 §6: "No role or permission is modelled until a process produces one"; BP-002
produces the operator.

ASP.NET Core Identity, .NET 10 (Microsoft Learn, "Enable passkeys", aspnetcore-10.0, read
2026-11-27): passkeys support "passwordless sign-in", "No built-in 2FA support: Passkeys are treated
as a primary authentication factor"; "No default attestation validation". The sign-in manager's
passkey methods need HTTPS ([SRC], `dotnet/aspnetcore` `release/10.0` 500a5c64, `SignInManager.cs`).

## Decision
1. **How the operator is reached.** By a phone number and an e-mail address of the platform operator,
   answered by a person, given in every notice and on the sign-in screen, the same everywhere. Neither
   requires the person to be signed in. They are configuration of the identity service, changed by the
   maintainer.
2. **Where the operator works.** In an operator console served by the identity host, on a desktop
   browser over HTTPS, never by the field client. The operator signs in with a passkey (ASP.NET Core
   Identity passkeys, a primary factor); operator accounts are created by the maintainer, are distinct
   from any person's account, and hold the one role this process produces, operator. The console signs
   operators in with ASP.NET Core Identity's sign-in manager and a cookie of its own, used by the console
   alone (`__Host-` prefix, `Secure`, `HttpOnly`, `SameSite=Strict`), with a session of at most 12 hours
   and 30 minutes of inactivity; signing out ends it. The person's sign-in never uses that cookie.
   The console can:
   - record an assisted request: the operator enters the former contact named and the new contact;
     the identity service sends a code to the new contact, which the requester enters on their own
     device, on a page of the identity service reached from that message, never through the operator,
     who never sees the code; the console then shows the operator the same outcome whether or not an
     account holds the former contact (BP002-AC-56, TSK-9);
   - record a dispute, and read the notices and delivery statuses of the account a recorded dispute's
     notice names, and of no other account.

   It cannot add, remove or confirm a means, nor stop or speed up a recovery.
3. **The records.** Two kinds, append-only, in schema `identity` (ADR-ARCH-053):
   - the assisted recovery record of BP002-D3: who asked (as they named themselves), through which new
     contact, the operator who took the request, when, and the means it would replace; its outcome
     (stopped, took effect, did not take effect) and the outcome's date are a second row appended when
     the recovery ends;
   - the dispute record of TSK-8: the notice disputed, what the person reported, the operator, when,
     and the path they were directed to.

   No row is updated, and none is deleted except by the retention below. Each operator action is recorded with
   the operator and the time.
4. **Retention.** Each record is kept 2 years after the date of its outcome row (for a dispute, its own
   date), then deleted with that row by the identity host's scheduler; a recovery still pending has no
   outcome row and is kept. The legal conformity of this period is not assessed here.
5. **Verification.**

   | Requirement | Verification |
   |---|---|
   | The record of an assisted recovery (BP002-D3) | BP002-AC-52 (its content and outcome) |
   | A request reveals nothing to the requester | BP002-AC-56 |
   | A dispute recorded, the person directed, nothing changed | BP002-AC-60 |
   | The console changes no means | A test that each console operation leaves the means unchanged |
   | Records append-only, deleted after 2 years | Tests of the grants and of the scheduler's deletion |

## Consequences
- The maintainer provides the operator's phone number and e-mail address, and creates the operator
  accounts and their passkeys (ADR-ORG-004 §4 for production).
- ADR-ARCH-018 governs the identity host's HTTP API endpoints; the console's and the stop page's HTML
  pages are served by the same host, their technology decided by the delivery.
- Passkeys need schema version 3 of Identity, whose JSON column on Npgsql is established by the
  delivery's tests (ADR-ARCH-053, Consequences).
- Attestation is not validated (Identity's default), so any passkey authenticator the operator chooses
  is accepted.
- What a recorded person may ask about their records, and who may read them, is outside this ADR.

## Alternatives considered
- **A WhatsApp number for the operator.** Not decided now: a phone number and an e-mail address reach
  every person; WhatsApp would be an addition.
- **Operator actions through the database directly.** Rejected: nothing would record them, and nothing
  would stop an operator from changing a means.
- **The operator signing in with a code like a person.** Rejected: an operator's phone number is a
  restricted means, and the console holds every account's records.
- **Keeping records without limit.** Rejected: personal data (contacts) kept longer than the recoveries
  and disputes they serve.

## References
- Microsoft Learn, "Enable passkeys"
  (https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0),
  read 2026-11-27; source https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/
  (500a5c64, `SignInManager.cs`).
- BP-002 `process.md` (BP002-D3, TSK-8, TSK-9).

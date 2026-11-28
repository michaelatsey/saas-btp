# ADR-ARCH-056 — How often a recovery may be requested: one pending per account, a doubling wait per requesting contact, one assisted request per new contact in 30 days, and no budget others can use up

Status: accepted
Date: 2026-11-28
Refs: issue #248 (Scope item 9); BP-002 `process.md` (BP002-R8, BP002-R9, BP002-R11, BP002-R12,
BP002-R15, BP002-D3, TSK-6, TSK-7, TSK-9, section "Residual risks of the self-service recovery"),
`acceptance.md`; ADR-ARCH-047; ADR-ARCH-049; ADR-ARCH-050; ADR-ARCH-055.

## Context
#248 asks how often a recovery may be requested for one account (item 9). BP002-D3 and the section
"Residual risks of the self-service recovery" accept repeated recovery requests on the condition that
each can be stopped from its notice without signing in. BP002-R9: the recovery is the primary way back
to an account. BP002-R12: one pending recovery per account; a request on an account where a recovery
is pending has no effect. A self-service recovery is requested signed in (TSK-6); an assisted one
through the operator (TSK-9, ADR-ARCH-050). This decision was decision 6 of ADR-ARCH-047 before
ADR-ARCH-047 was split into one ADR per problem (the maintainer's decision of
2026-11-28 in the /fix-issue 248 session).

## Decision
1. **The limits.** No budget is kept per account, so that nobody else can use up the person's own way
   back (BP002-R9):
   - one pending recovery per account at a time (BP002-R12);
   - a self-service request is counted against the contact signed in to make it: after a request made
     through that contact is stopped or cancelled, the next one through it waits one hour, then two,
     four, and so on, doubling up to 24 hours at most; the wait returns to none once a request through
     that contact takes effect, or 30 days after the last stop or cancel. The holder who stopped it, and
     every other contact, are not affected;
   - an assisted request is counted against its new contact: at most one per new contact in 30 days,
     whatever former contact it names; beyond it, the answer is the same as any other's, and the request
     has no effect (TSK-9). No per-IP limit applies: the operator records the request, so the address
     seen is the operator's, and the operator's handling bounds the rate;
   - self-service and assisted requests never share a budget.
2. **Case by case, no limit here can be used up by someone else against the account's holder.**

   | Scenario | Who acts | Can someone else defeat it through a limit? |
   |---|---|---|
   | BP002-AC-36, AC-39 | Awa, signed in with E1 | No: her request is counted against E1 only, and nobody else can make a request through E1. |
   | BP002-AC-37, AC-53 | A sign-in with a suspended contact | No limit of this decision applies; the offer to stop is ADR-ARCH-049's. |
   | BP002-AC-38, AC-40, AC-42 | Awa through E1; the holder of P1 stops | Awa's next request through E1 waits 1 hour after the first stop, doubling while P1's holder keeps stopping: while they hold P1 they would stop it anyway, and once the carrier has blocked P1 the wait is at most 24 hours. Nobody else can increase it without stopping her request, which needs P1. |
   | BP002-AC-41, AC-43 | The holder of P1 requests; Awa stops from E1 | No: stopping sets no wait on Awa; the requester's own wait grows. Awa may then recover P1 through E1 at once. |
   | BP002-AC-45 | Awa cancels her own request | Her next request through the same contact waits one hour, doubling if she cancels again; nobody else can cancel it, so nobody else can set that wait. |
   | BP002-AC-52, AC-54, AC-55, AC-57, AC-58 | A requester through the operator; a former contact stops | No: the assisted budget is per new contact; Awa's own assisted request uses her new contact, which nobody else can use without its code. Repeated requests by others, each with a new contact, are the lever BP002-D3 accepts. |
   | BP002-AC-56 | Two requests | No: the answer over the limit is the same as any other's. |

3. **The stop is outside these limits.** The stop of a recovery is never refused by decision 1; its own
   limits are per notice (ADR-ARCH-049), so that nobody without the notice can affect them
   (BP002-R11, BP002-AC-51).
4. **What bounds the notices a recovery triggers.** Each request, its effect, its stop and its cancel
   send notices (BP002-R15), outside the spending ceiling (ADR-ARCH-055 decision 6); decision 1 bounds
   how often a request can be made, and so how many notices recoveries send.
5. **Verification.**

   | Requirement | Verification |
   |---|---|
   | One pending recovery per account | BP002-AC-56 |
   | The doubling wait per requesting contact, and its reset | BP002-AC-41 (a repeated request stopped again), AC-45, AC-46; the wait returns to none after an effect and after 30 days |
   | One assisted request per new contact in 30 days | A second assisted request with the same new contact within 30 days has no effect, with the same answer |
   | No limit usable by someone else | The case table above, one test per row |
   | The stop outside the limits | BP002-AC-51 |

## Consequences
- The waits and counts are kept in schema `identity` (ADR-ARCH-053), so that they hold across restarts.
- A harassed person may receive a notice each time a holder of another contact asks for a recovery
  again; the doubling wait bounds how often.

## Alternatives considered
- **A recovery budget per account.** Rejected: others could use it up and block the person's own
  recovery, against BP002-R9.
- **A per-IP limit on assisted requests.** Rejected: the address seen is the operator's (decision 1).
- **A fixed wait after each stop.** Rejected: a requester whose requests are stopped again and again
  would keep sending notices at the same rate; the doubling wait slows them.

## References
- BP-002 `process.md` (BP002-R8 to BP002-R12, BP002-D3, section "Residual risks of the self-service
  recovery") and `acceptance.md` (BP002-AC-36 to AC-46, AC-51 to AC-58).

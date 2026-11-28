---
id: BP-002-contract
status: validated
validated_by: michaelatsey
validated_on: 2026-11-28
issue: 249
applies: [INV-ACCESS-01, INV-ACCESS-10, INV-ACCESS-11, ADR-ARCH-018, ADR-ARCH-019, ADR-ARCH-020, ADR-ARCH-021, ADR-ARCH-025, ADR-ARCH-027, ADR-ARCH-028, ADR-ARCH-031, ADR-ARCH-032, ADR-ARCH-033, ADR-ARCH-034, ADR-ARCH-035, ADR-ARCH-037, ADR-ARCH-038, ADR-ARCH-040, ADR-ARCH-042, ADR-ARCH-043, ADR-ARCH-044, ADR-ARCH-045, ADR-ARCH-046, ADR-ARCH-047, ADR-ARCH-048, ADR-ARCH-049, ADR-ARCH-050, ADR-ARCH-051, ADR-ARCH-052, ADR-ARCH-053, ADR-ARCH-054, ADR-ARCH-055, ADR-ARCH-056, ADR-ARCH-057, naming.md#aggregate-identifiers, sql.md#rls, sql.md#audit--lifecycle]
---

# BP-002 — Contract

What accessing the platform introduces in software, and what guarantees each invariant and scenario
of `process.md` and `acceptance.md` in production. Rules are referenced by identifier and section,
never restated; each identifier resolves to the file listed under References, and where a sentence
here names a rule decided elsewhere, the cited section is authoritative. The ADRs decide the
mechanisms; this contract states BP-002's operations, their
answers, who guarantees what, and the points the ADRs leave to it (the messages, ADR-ARCH-045
decision 6 and ADR-ARCH-048, Consequences; the scheduler, ADR-ARCH-049, Consequences).

Parts of the applied ADRs that are history and not applied here:
- ADR-ARCH-031 §2: "the person is external to the product" and "authentication is not a bounded
  context of the product" (superseded by ADR-ARCH-042 decision 4).
- ADR-ARCH-032 §2's call path and §4's creation order (superseded by ADR-ARCH-033).
- ADR-ARCH-033 §4: the subject rule, for the identity host (superseded by ADR-ARCH-053 decision 4),
  and the sentence naming the readiness check as the one access outside a unit of work (superseded
  by ADR-ARCH-053 decision 5).
- ADR-ARCH-035 §2's copy condition, its Consequence on BP-001 waiting and its rejection of copying a
  library as it is (superseded by ADR-ARCH-037).
- ADR-ARCH-038: decisions 1, 2, 5 and 9 in the parts ADR-ARCH-043 decision 1 supersedes, and
  decision 8 (superseded by ADR-ARCH-043).
- ADR-ARCH-049 decision 3, last bullet, for a valid secret whose recovery has taken effect
  (superseded by ADR-ARCH-057 decision 10).

ADR-ARCH-041 is not applied: it is superseded wholly by ADR-ARCH-043 from the deployment of the
identity service, on which BP-002 runs (ADR-ARCH-042 decision 7).

ADR-ARCH-035 and ADR-ARCH-037 apply to the Identity module as to every module: its unit of work and
its interceptors through the copied MicroKit.Persistence, with no subject (ADR-ARCH-053 decision 4),
and the library's analyzer. Whether the ASP.NET Core Identity and OpenIddict store mappings fit
MicroKit.Persistence's context is established by the delivery; a misfit is reported as a decision
to take, never routed around.

## 1. Owner and boundary

- **Identity** owns every element BP-002 creates or changes: the account, its contacts, its
  stronger factor, its sessions, the code challenges, the factor proofs, the notices, the stop
  codes, the recoveries, the records and the operator accounts. Module
  `apps/api/src/modules/Identity/`, host `apps/api/src/hosts/SaasBtp.Identity`, schema `identity`,
  role `saasbtp_identity`, Doppler project `saas-btp-identity` (ADR-ARCH-042 decision 1,
  ADR-ARCH-053 decision 1). INV-ACCESS-10 and INV-ACCESS-11 are stated in `docs/domain/access.md`
  (section Identity) under their identifiers, and Identity is their owning context.
- **Access** owns the profile, which BP-002 neither creates nor changes (INV-ACCESS-01; process.md,
  Boundary). BP-002 adds to Access one published function, `access.account_has_profile`
  (ADR-ARCH-054 decision 1), and nothing else.
- **The API host** validates the identity service's `saasbtp-api` tokens and checks their session
  on every request (ADR-ARCH-043, ADR-ARCH-053 decision 5); every unit of work of the API takes the
  shared lock and re-checks the session (ADR-ARCH-054 decision 4); modules read the caller through
  the port of ADR-ARCH-038 decision 5, from `sub` and `contact_confirmed` (ADR-ARCH-042 decision 6).
  The API host never references the Identity module.
- **The client** holds what §8 lists.
- **Third parties**: Twilio Programmable SMS and Resend behind Identity's one delivery port
  (ADR-ARCH-045); Cloudflare Turnstile (ADR-ARCH-055 decision 1). None of them is a guarantor of a
  rule: each rule is held by the identity service (ADR-ARCH-042 decision 3).
- **Transactions.** Each operation of §3 and each scheduled effect of §5 is one unit of work of
  Identity; TSK-9 takes two (the console's request, then the requester's page), and a recovery's
  effect is the scheduler's, apart from its request. No transaction spans
  two modules (ADR-ARCH-031 §3); a recovery's effect is one transaction of Identity that reads one
  Access function (ADR-ARCH-054, Consequences).
- **Events.** BP-002 publishes no integration event: no consumer needs one (ADR-ARCH-032 §1). The
  events of `process.md` (Code sent, Signed in, Account created, Contact added, and the others) are
  facts of Identity: notices and records (§2), never integration events.

## 2. What Identity holds

The facts the identity service keeps in schema `identity` (ADR-ARCH-053), not their DDL:

| Fact | Holds | Decided by |
|---|---|---|
| Account | An ASP.NET Core Identity user whose identifier, a UUIDv7, is the `sub` | ADR-ARCH-042 decision 2, `naming.md` Aggregate identifiers |
| Contact | Its account, its kind (phone or e-mail), its value as recorded, its normalized value (unique), when it was confirmed | ADR-ARCH-042 decision 2, ADR-ARCH-052, INV-ACCESS-10 |
| Code challenge, one per normalized contact | The live code's hash, when it was sent and expires, whether it was used; the request times for the interval and the hourly limit; the consecutive failures and the lock's end | ADR-ARCH-042 decision 2, ADR-ARCH-046, ADR-ARCH-047 decision 1 |
| Stronger factor | The ten codes' salted hashes, which are used; the failure counts per session, per contact and account-wide | ADR-ARCH-048 decision 1, ADR-ARCH-047 decision 3 |
| Factor proof | The session, the operation, its target, when it expires (5 minutes), whether it was used | ADR-ARCH-048 decision 6 |
| Session | An OpenIddict authorization and its tokens; the contact the session was signed in with | ADR-ARCH-044 decision 2 |
| Recovery | Its kind (self-service or assisted), the means it removes, its new contact, its requester's session and contact (self-service), when it was requested and becomes due, its outcome | ADR-ARCH-049, ADR-ARCH-056 |
| Notice | Its account, contact, kind, the event and its time; for a pending recovery, its stop link's SHA-256, its stop codes (hashed), their interval and failure counts | ADR-ARCH-049 decisions 2 to 4, ADR-ARCH-055 decision 6 |
| Recovery request waits | Per requesting contact (self-service) and per new contact (assisted) | ADR-ARCH-056 decision 1 |
| Accepted Turnstile tokens | Their hashes, for 300 seconds | ADR-ARCH-055 decision 1 |
| Daily ceiling counts | Per route and day (UTC) | ADR-ARCH-055 decision 5, ADR-ARCH-057 decision 7 |
| Operator accounts | Identity users of their own, with passkeys | ADR-ARCH-050 decision 2 |
| Records | Assisted recoveries and disputes, append-only | ADR-ARCH-050 decision 3 |
| Delivery statuses | As each provider reports them | ADR-ARCH-045 decision 2 |

The contact a session was signed in with is what BP002-R9 (a recovery requested with a contact it
keeps), ADR-ARCH-047 decision 3 (factor limits per contact) and ADR-ARCH-049 decision 2 (who may
cancel) read. Audit columns follow `sql.md` Audit / lifecycle. No other secret is stored than those
the ADRs name; a contact proved "now" (BP002-R7) is proved by its code carried in the request of the
operation that needs it, and no proof of a contact is kept between requests.

## 3. Interfaces

The person endpoints of the identity host are Minimal APIs under `/api/v1` (ADR-ARCH-018), in its
document `apps/api/openapi/identity-v1.json` (ADR-ARCH-057 decision 9). Their answers follow
ADR-ARCH-057 decision 6; a refusal it does not list carries the fixed `title` given below. Every
input of a contact is read as ADR-ARCH-052 decision 4 states, before any lookup or count. Every
operation requires a connection (BP002-R16). Properties an operation does not define are ignored.

### 3.1 Signed out

**Ask for a sign-in code — `POST /api/v1/sign-in/codes`** (TSK-1). Body: `contact`, `turnstileToken`.

| Status | When |
|---|---|
| 202 | A code was sent (ADR-ARCH-046), whether or not an account holds the contact, and whether or not it is suspended by a recovery (BP002-R10). No body |
| 422 | `contact` is not a mobile phone number of the destination list nor an e-mail address (ADR-ARCH-052 decision 1): "The contact must be a mobile phone number or an e-mail address." |
| 403 | Turnstile refused, or Siteverify failing (ADR-ARCH-055 decision 1, ADR-ARCH-057 decision 6); nothing counted, nothing sent |
| 429 | The interval, the hourly limit, the contact's lock, the per-IP limit or the ceiling (ADR-ARCH-046 decision 4, ADR-ARCH-047 decision 1, ADR-ARCH-055 decisions 4 and 5); `Retry-After`; nothing sent |
| 400, 500 | Unreadable body; any other failure |

**Sign in, or create the account — the token endpoint, `POST /connect/token`** (TSK-2; ADR-ARCH-042
decision 2). The field client `saasbtp-field`, a public client (ADR-ARCH-057 decision 2). Grant type
`urn:saasbtp:grant-type:contact-code`, with `contact`, `code` and, optionally, `create_account=true`.
Answers in OAuth 2.0's format (RFC 6749 §5.2):

| Case | Answer | The code |
|---|---|---|
| Wrong code | `invalid_grant`, "The code is wrong." | Failure counted |
| Expired or replaced code | `invalid_grant`, "The code has expired." | — |
| The contact locked | `invalid_grant`, "Too many attempts.", with `Retry-After` | Not verified |
| Correct; an account holds the contact; not suspended | Tokens: `access_token` (audience `saasbtp-api`), `token_type` Bearer, `expires_in` 600, `refresh_token`, `scope` | Used |
| Correct; no account holds it; no `create_account` | `account_creation_required`, "No account holds this contact." | Not used |
| Correct; no account holds it; `create_account=true` | The account created with that contact, confirmed; tokens | Used |
| Correct; `create_account=true` but an account now holds it | As the sign-in case: tokens for that account | Used |
| Correct; the contact is suspended by a pending recovery | `contact_suspended`, "A recovery of this account is pending." | Not used |

The first three answers are the same whatever account holds the contact (BP002-R2, §4); the account
is looked up only after a correct code (ADR-ARCH-042 decision 3). A code is used by the one operation
it performs (a sign-in, a creation, a stop): an answer that performs nothing does not use it, and a
correct code returns the contact's failure count to zero (ADR-ARCH-047 decision 1). That is how
BP002-R1 and BP002-AC-14 are met: a code signs in, creates or stops once. A sign-in records the
contact on the session (§2). The answers that are not tokens use the pass-through `Forbid` with its
error properties (ADR-ARCH-057, Context); the delivery proves them on the token endpoint by tests.

**Refresh — `POST /connect/token`, `grant_type=refresh_token`.** Rolling, 30 days absolute
(ADR-ARCH-044 decisions 3 and 4). With `scope=identity`: an access token whose only audience is
`saasbtp-identity`; otherwise `saasbtp-api` (ADR-ARCH-057 decision 2). Refused (`invalid_grant`) for
an ended or expired session or an account that no longer exists.

**Discovery and keys** — `/.well-known/openid-configuration`, `/.well-known/jwks` (ADR-ARCH-043
decision 1.2).

**Stop at sign-in — `POST /api/v1/sign-in/stops`** (TSK-7 from the offer; ADR-ARCH-049 decision 5).
Body: `contact`, `code`, the correct code that was answered `contact_suspended`.

| Status | When |
|---|---|
| 204 | The recovery that suspends the contact is stopped; the code used; the stop notified (BP002-R15) |
| 422, 410, 429 | Wrong, expired or replaced code, lock: as ADR-ARCH-057 decision 6, the same for every contact |
| 409 | Correct code, and the recovery that suspended the contact has taken effect meanwhile: "The recovery has already taken effect."; the client shows « Récupération déjà effectuée » of §7 (TSK-7 Errors) |
| 409 | Correct code, and no pending recovery suspends the contact otherwise: "There is no recovery to stop." |

This path follows the contact's sign-in lock; the notice's stop page does not (ADR-ARCH-049
decision 5).

**The operator's contact information — `GET /api/v1/operator`.** 200: `phone`, `email`
(ADR-ARCH-050 decision 1). Anonymous, shown on the sign-in screen.

### 3.2 Signed in

Every operation below requires an access token whose audience is `saasbtp-identity`, validated and
its session checked as ADR-ARCH-057 decisions 3 and 4 state (401 with the bare challenge
otherwise), and runs in one unit of work taking the locks of ADR-ARCH-057 decision 5. A change of the
means is refused while a recovery of the account is pending (BP002-R12): 409, "A recovery of this
account is pending.". So are a code request and a factor proof, which serve only changes; reading
the means, cancelling and ending sessions are not. Every change that
takes effect is notified at once, as §7 states (BP002-R15). Codes in a body are `{ "contact", "code" }`
pairs; each is checked as at sign-in (422, 410, 429 for the first faulty one, the others not used)
and used by the operation when it succeeds.

| Operation | Endpoint | Body | Success | Refusals of its own |
|---|---|---|---|---|
| Read the means | `GET /api/v1/me/means` | — | 200: the contacts (kind, value, confirmed), whether a factor exists and how many of its codes remain, the pending recovery if any (kind, means it removes, when due) | — |
| Ask for a code for a contact | `POST /api/v1/me/codes` | `contact`; `turnstileToken` when the contact is not one the account holds | 202 | 403 (Turnstile, for a new contact only); 429 (limits; for a contact the account holds, ADR-ARCH-057 decision 7) |
| Prove the factor | `POST /api/v1/me/factor/proofs` | `operation` (`addContact`, `removeContact`, `changeContact`, `replaceFactor`, `removeFactor`, `requestRecovery`), `target` (the contact, when the operation has one), `code` (one of the factor's codes) | 204: a factor proof for that operation and target (ADR-ARCH-048 decision 6) | 409 "The account has no stronger factor."; 422 wrong code; 429 the per-contact or account-wide lock (ADR-ARCH-047 decision 3); 403 "Sign in again to use your backup codes." when retired for this session (ADR-ARCH-057 decision 6) |
| Add a contact | `POST /api/v1/me/contacts` | `code` for the new contact | 201: the contact | 403 "Stronger authentication is required." (the account has a factor and no proof); 409 "The contact belongs to another account." (after a correct code only; INV-ACCESS-10) |
| Remove a contact | `POST /api/v1/me/contacts/removals` | `contact`; its `code`, or a factor proof | 204 | 409 "The last contact cannot be removed." (BP002-R4); 403 "The contact must be proved." (neither its code nor a proof) |
| Change a contact | `POST /api/v1/me/contacts/changes` | `former`: the contact removed, and its code unless a proof is given; `new`: the new contact's code; with a factor, one `changeContact` proof whose target is the new contact, which serves both the addition and the removal | 200: the contacts | As adding and removing; one operation (TSK-4) |
| Register the factor | `POST /api/v1/me/factor` | `codes`: one for every contact of the account | 201: the ten codes, shown once (ADR-ARCH-048 decision 1) | 409 "The account already has a stronger factor."; 403 "Every contact must be proved." |
| Replace the factor | `PUT /api/v1/me/factor` | a factor proof, or `codes` for every contact | 200: ten new codes; the former voided | 403 "Stronger authentication or every contact is required." |
| Remove the factor | `DELETE /api/v1/me/factor` | as replacing | 204 | as replacing |
| Ask for a self-service recovery | `POST /api/v1/me/recoveries` | `removes`: contacts and/or `factor`; optionally `new`: a contact and its code | 202: when due (ADR-ARCH-049 decision 1) | 409 "A recovery of this account is pending." (with how to stop it, §7 screen texts); 409 "The recovery would leave no contact."; 403 "The recovery must be asked with a contact it keeps." (BP002-R9); 403 "Stronger authentication is required." (it adds a contact and the account keeps its factor, BP002-R8); 409 "The new contact cannot be used." (its account has a profile, BP002-R14, ADR-ARCH-054 decision 2); 429 the wait of ADR-ARCH-056 decision 1 |
| Cancel one's recovery | `DELETE /api/v1/me/recoveries/pending` | — | 204: stopped, notified | 409 "The recovery has already taken effect." when the requester's recovery took effect meanwhile, the client showing « Récupération déjà effectuée » of §7 (TSK-7 Errors); 404 "There is no recovery to cancel." otherwise; 403 "The recovery must be cancelled with a contact it keeps." (ADR-ARCH-049 decision 2) |
| Sign out of this device | `DELETE /api/v1/me/sessions/current` | — | 204 | — |
| End every other session | `DELETE /api/v1/me/sessions/others` | — | 204 | — |

A refusal writes nothing of the operation. It writes only what the ADRs count: a wrong code's
failure (ADR-ARCH-047 decision 1), a failed factor proof's counts (ADR-ARCH-047 decision 3) and a
code request's time (ADR-ARCH-046 decision 4), in the same unit of work, committed with the refusal.
The order of checks: authentication and session
(401), the body (400, 422 for a malformed field), the pending recovery (409), then the codes and
proofs, then the operation's own refusals. A code for a contact no account holds, and a code for
another account's contact, are answered the same until the code is entered (BP002-R2,
BP002-AC-24). Ending sessions follows ADR-ARCH-044 decision 5; the API refuses an ended session from
its next request (ADR-ARCH-043 decision 2). A recovery request ends every other session of the
account at once (TSK-6) and suspends the means it removes (BP002-R10).

### 3.3 The stop page

Served by the identity host from a notice's link, `/stop#<secret>`, without signing in, not in the
OpenAPI document (ADR-ARCH-049 decisions 3 and 4, ADR-ARCH-057 decisions 9 and 10). The page reads
the secret from the fragment and sends it in the body of each request, never in a path or a query:

- `POST /stop/notice` (`secret`): for a secret that names a live notice, the page offers to send a
  stop code to that notice's contact and, when the recovery removes the factor, to enter one of its
  codes. For a valid secret whose recovery has taken effect, it shows « Récupération déjà effectuée »
  of §7 (ADR-ARCH-057 decision 10). For any other secret, only « Il n'y a rien à arrêter. »
  (ADR-ARCH-049 decision 3).
- `POST /stop/code` (`secret`): sends a stop code, after the notice's growing interval; never a
  lock.
- `POST /stop` (`secret`, and the stop code or a factor code where allowed): stops the recovery
  (BP002-R11), notifies the stop, and shows « La récupération est arrêtée. Votre compte n'a pas
  changé. »; if the recovery has taken effect meanwhile (ADR-ARCH-049 decision 6), « Récupération déjà
  effectuée ».

Neither Turnstile, nor the ceiling, nor the contact's lock, nor ADR-ARCH-056's limits apply to it
(ADR-ARCH-049 decision 3, ADR-ARCH-047 decision 4).

### 3.4 The operator console and the requester's page

Served by the identity host on HTTPS, signed in with a passkey and the console's cookie
(ADR-ARCH-050 decision 2), not in the OpenAPI document:

- **Record an assisted request**: the former contact named, the new contact, who asked as they name
  themselves, with the operator's Turnstile token (ADR-ARCH-055 decision 1). The code to the new
  contact is under the ceiling (ADR-ARCH-055 decision 5); it is sent whether or not ADR-ARCH-056's
  assisted limit is reached, since beyond it "the answer is the same as any other's" (ADR-ARCH-056
  decision 1). The message carrying the code gives the address of the requester's page (`{page}`,
  §7).
- **The requester's page** (`/assisted`, anonymous): the requester enters the new contact and the
  code they received; wrong, expired or replaced codes and locks are answered as at sign-in (§4).
  After a correct code:
  - if the new contact belongs to an account that has a profile (BP002-R14, ADR-ARCH-054 decision 2),
    the page answers « Ce nouveau contact ne peut pas servir à une récupération. », whether or not an
    account holds the former contact, and nothing is created;
  - otherwise the page answers « Votre demande est enregistrée. Si elle concerne un compte, elle
    prendra effet dans 7 jours, sauf si elle est arrêtée. Connectez-vous alors avec ce nouveau
    contact. », in every case; and only if an account holds the former contact, no recovery is
    pending on it (BP002-R12) and the assisted limit is not reached does a pending recovery begin:
    every means of that account suspended, every session ended, every contact notified.
- The console then shows the operator one outcome, whatever account exists (BP002-AC-56).
- **Record a dispute** (TSK-8), and read the notices and delivery statuses of the account a
  recorded dispute's notice names, and of no other.

The console cannot add, remove or confirm a means, nor stop or speed up a recovery.

### 3.5 Delivery status callbacks

The identity host receives Twilio's and Resend's delivery statuses at endpoints of its own,
authenticated as each provider documents, read by the delivery before it wires them (CLAUDE.md,
Working rules). They are recorded, never relied on (ADR-ARCH-045 decision 2).

### 3.6 The API host

No new operation. Its answers gain the 401 of an ended session (ADR-ARCH-043, Consequences); BP-001's
answers are otherwise unchanged.

## 4. Answers that reveal nothing

BP002-R2 compares a contact that another account holds with one that no account holds; signed out,
every contact is in that comparison, since the caller has no account. For those two, the whole
response (status, headers other than date and tracing, body) is the same for: a code request (202, 403, 429); a wrong code, an
expired or replaced code and a lock, at the token endpoint and in every signed-in operation; a stop
code request on the stop page; the console's outcome of an assisted request; and the requester's
page's answer after a correct code, whether or not an account holds the former contact, a recovery
is pending on it, or the assisted limit is reached (TSK-9). A contact suspended
by a recovery is answered as any other until a correct code is entered. Signed in, a code for a
contact of the caller's own account is asked without a Turnstile token (ADR-ARCH-057 decision 7);
that answer can differ from the others, and reveals nothing: the caller's own contacts are theirs to
read (`GET /api/v1/me/means`). A contact of another account and a contact no account holds both
need the token, and are answered alike. The account is first looked
up after a correct code, never before (ADR-ARCH-042 decision 3); the answers are built without
reading it. The proof compares whole responses with and without an account (ADR-ARCH-042 decision 3,
§9).

## 5. Failure, retry and concurrency

- **Locks.** Every unit of work of the identity host takes its locks in the order of ADR-ARCH-057
  decision 5, the token endpoint's included.
- **A code requested twice, or at once.** The challenge row is locked first; one request sends the
  code; the other is answered 429 by the 60-second interval. A later request voids the former code
  (ADR-ARCH-046 decisions 3 and 4). Failures carry over (ADR-ARCH-046 decision 5).
- **A creation confirmed twice at once.** Both lock the challenge; the first creates the account and
  uses the code; the second finds the code used and is answered as an expired code. The contacts'
  unique key (INV-ACCESS-10) holds whatever the order.
- **A contact changed during a recovery.** Refused while the recovery is pending (BP002-R12). A
  change and a recovery request racing on one account: whichever locks the account first commits; the
  other then sees the result and is refused or answered as it reads.
- **A new contact taken during the delay.** ADR-ARCH-054 decision 3, at the effect.
- **An effect and a stop at once.** Whichever commits first; the other is told that the recovery is
  over (ADR-ARCH-049 decision 6).
- **A session expiring offline** (BP002-D4, TSK-11). The device keeps capturing; the next refresh,
  once connected, is refused if the session ended or passed its 30 days, and the person signs in
  again (ADR-ARCH-044 decision 4); the captures wait for their author (ADR-ARCH-051).
- **A request whose session ends in flight.** On the identity host, its unit of work holds the
  session `for share`, so the ending waits for it, and nothing of that session is written afterwards
  (ADR-ARCH-057 decision 4). On the API, a unit of work whose re-check runs after the session ended
  writes nothing (ADR-ARCH-054 decision 4); one that re-checked before may still commit.
- **A notice that fails to send.** Retried as ADR-ARCH-055 decision 6 states.
- **Siteverify failing.** Refused, fail closed, after one retry (ADR-ARCH-055 decision 1).
- **A lost answer.** A sign-in whose answer is lost asks for a new code. A signed-in operation whose
  answer is lost is read back with `GET /api/v1/me/means`; its codes were used, so it is not replayed
  with them.
- **The scheduler** (the form ADR-ARCH-049 leaves to the contract). One background service of the
  identity host, in its single instance (ADR-ARCH-055 decision 4), runs every minute. Each run takes
  each recovery past its delay in a transaction of its own, under the order of ADR-ARCH-057 decision
  5; an effect that already ended, or a lock timeout (ADR-ARCH-054 decision 3), leaves it to the next
  run, so a run is idempotent. The same service retries notices, deletes records past retention
  (ADR-ARCH-050 decision 4), prunes OpenIddict's entries (ADR-ARCH-053 decision 6), resets the daily
  counts at midnight UTC and raises the daily alerts (ADR-ARCH-055 decisions 5 and 6). A run that
  fails is logged and the next run takes the work again.

## 6. Security guarantees and trust boundaries

| Boundary | What holds it |
|---|---|
| Client and identity host | HTTPS; the person's tokens (ADR-ARCH-044); the identity host accepts only `saasbtp-identity` tokens and checks their session (ADR-ARCH-057 decisions 1, 3, 4); the console's cookie and passkey (ADR-ARCH-050 decision 2); Turnstile on code requests (ADR-ARCH-055 decision 1); the client's address from the known proxy only (ADR-ARCH-055 decision 7) |
| Client and API host | `saasbtp-api` tokens validated, their session checked (ADR-ARCH-043); the API never receives a refresh token, so no token it holds yields an identity token (ADR-ARCH-057 decision 2) |
| API and database | `app.user_id` set first from the validated token (ADR-ARCH-032 §3); `saasbtp_api` reads three columns of its own subject's authorizations through `identity.session_is_active` and nothing else of `identity` (ADR-ARCH-053 decision 5) |
| Identity host and database | `saasbtp_identity`, forced row level security, no `security definer` (ADR-ARCH-053 decisions 1 to 3); on `access`, only `access.account_has_profile` and `access.profiles(user_id)` (ADR-ARCH-054 decision 1); no grant to `anon`, `authenticated` or `service_role` (ADR-ARCH-053 decision 3) |
| Identity host and providers | One delivery port; the providers' credentials and the Turnstile secret in the identity host's Doppler config and in Bitwarden (ADR-ARCH-045, ADR-ARCH-055, Consequences); signing and encryption certificates there too (ADR-ARCH-044 decision 6) |

- Secrets are stored hashed only, as each ADR states: codes and stop codes (ADR-ARCH-046 decision 3,
  ADR-ARCH-049 decision 3), the factor's codes (ADR-ARCH-048 decision 1), the stop link (ADR-ARCH-049
  decision 3), the Turnstile tokens (ADR-ARCH-055 decision 1).
- No code, factor code, stop secret, token or Turnstile token is ever written to a log, an answer
  other than the one that issues it, or a notice other than the one that carries it. A contact in a
  log is masked as in §7.
- A refusal never reveals whether an account holds a contact before its code is proved (§4).

## 7. Messages

The French text of every message is normative (ADR-ARCH-045 decision 6, ADR-ARCH-048,
Consequences). Placeholders, in braces:

- `{nom}`: the platform's name, configuration of the identity service, at most 12 characters.
- `{code}`: the 8 digits of the code (ADR-ARCH-046 decision 1).
- `{date}`, `{échéance}`: `JJ/MM/AAAA à HH:MM GMT`.
- `{contact}`: masked. A phone number is its country code and its last four digits (`+225 ****8432`);
  an e-mail address is its first character, `***`, then `@` and its domain, cut to its last 20
  characters (`a***@exemple.com`); at most 25 characters.
- `{lien}`: the stop link (ADR-ARCH-049 decision 3), at most 50 characters.
- `{tél}`, `{e-mail}`: the operator's phone number in E.164, at most 16 characters, and e-mail
  address, at most 25 characters (ADR-ARCH-050 decision 1).
- `{retraits}`: what a recovery removes, as `{contact}` items and « codes de secours » joined by
  `, `; `{ajouts}`: the contact it adds. The texts `, ajoute {ajouts}` and `, ajouté {ajouts}` appear
  only when it adds one.
  Each is cut to 50 characters with `...`, in the SMS and the e-mail alike.
- `{page}`: the address of the requester's page of an assisted recovery (§3.4), at most 40
  characters.

**SMS.** The segments given are computed with every placeholder at its maximum above, the texts
written so that, as far as the French allows, every character is in GSM-7's basic set (no « ê », « ç »
or « â »; single segment 160 characters, 153 per segment beyond). A test renders each text the same
way and counts its segments with the encoding its characters require as Twilio's documentation
states it, read by the delivery; it asserts the count given here. A text whose count the test does
not confirm is corrected in this contract by a pull request of its own.

| Message | Text | Segments |
|---|---|---|
| Sign-in code, and any code proving a contact | `{nom} : votre code est {code}. Il expire dans 10 minutes. Ne le donnez à personne, opérateur compris.` | 1 |
| Stop code | `{nom} : code pour stopper la récupération de votre compte : {code}. Valable 10 minutes. Ne le donnez à personne.` | 1 |
| Contact added | `{nom} : {contact} a été ajouté à votre compte le {date}. Si ce n'est pas vous, contactez l'opérateur : {tél}, {e-mail}` | 2 |
| Contact removed | `{nom} : {contact} a été retiré de votre compte le {date}. Si ce n'est pas vous, contactez l'opérateur : {tél}, {e-mail}` | 2 |
| Contact changed | `{nom} : {contact} a été remplacé sur votre compte le {date}. Si ce n'est pas vous, contactez l'opérateur : {tél}, {e-mail}` | 2 |
| Factor registered | `{nom} : codes de secours créés pour votre compte le {date}. Si ce n'est pas vous, contactez l'opérateur : {tél}, {e-mail}` | 2 |
| Factor replaced | `{nom} : codes de secours de votre compte remplacés le {date}. Si ce n'est pas vous, contactez l'opérateur : {tél}, {e-mail}` | 2 |
| Factor removed | `{nom} : codes de secours de votre compte supprimés le {date}. Si ce n'est pas vous, contactez l'opérateur : {tél}, {e-mail}` | 2 |
| Recovery requested, with the stop link (a suspended contact; a kept contact when the factor is removed) | `{nom} : récupération de votre compte demandée le {date}, effet le {échéance} : retire {retraits}, ajoute {ajouts}. Si ce n'est pas vous, stoppez-la : {lien} Aide : {tél}, {e-mail}` | 3 |
| Recovery requested, cancel only (another kept contact) | `{nom} : récupération demandée le {date}, effet le {échéance} : retire {retraits}, ajoute {ajouts}. Pour l'annuler, connectez-vous puis Mon compte. Chaque contact retiré peut la stopper depuis son avis. Aide : {tél}, {e-mail}` | 3 |
| Assisted recovery requested (every former contact, with the stop link) | `{nom} : l'opérateur a enregistré le {date} une demande de récupération de votre compte avec un autre contact, effet le {échéance}. Si ce n'est pas vous, stoppez-la : {lien} Aide : {tél}, {e-mail}` | 2 |
| Recovery took effect | `{nom} : la récupération demandée le {date} a pris effet : retiré {retraits}, ajouté {ajouts}. Si ce n'est pas vous, contactez l'opérateur : {tél}, {e-mail}` | 2 |
| Recovery stopped | `{nom} : la récupération demandée le {date} a été stoppée. Votre compte n'a pas changé. Aide : {tél}, {e-mail}` | 1 |
| Recovery cancelled | `{nom} : la récupération demandée le {date} a été annulée par son demandeur. Votre compte n'a pas changé. Aide : {tél}, {e-mail}` | 2 |
| Recovery did not take effect (BP002-R14) | `{nom} : la récupération demandée le {date} n'a pas pris effet : le nouveau contact appartient à un autre compte. Votre compte n'a pas changé. Aide : {tél}, {e-mail}` | 2 |
| Account ended (BP002-R14) | `{nom} : le compte créé avec {contact} a été fermé le {date} : ce contact a rejoint un autre compte par une récupération. Si ce n'est pas vous, contactez l'opérateur : {tél}, {e-mail}` | 2 |
| Assisted request: the new contact's code | `{nom} : votre code est {code}. Saisissez-le sur {page} . Il expire dans 10 minutes. Ne le donnez à personne.` | 1 |

**E-mail.** Each message has its subject: sign-in code and any code proving a contact, « Votre
code »; stop code, « Code d'arrêt »; contact added, « Contact ajouté »; contact removed, « Contact
retiré »; contact changed, « Contact remplacé »; factor registered, « Codes de secours créés »;
factor replaced, « Codes de secours remplacés »; factor removed, « Codes de secours supprimés »;
recovery requested, with the stop link or cancel only, « Récupération demandée »; assisted recovery
requested, « Récupération assistée demandée »; took effect, « Récupération effectuée »; stopped,
« Récupération arrêtée »; cancelled, « Récupération annulée »; did not take effect, « Récupération
sans effet »; account ended, « Compte fermé »; the assisted request's code, « Code de récupération
assistée ». The body is the SMS text above, followed by:

> Vous recevez ce message parce que cette adresse est un contact de votre compte {nom}. Si vous ne
> reconnaissez pas cet événement, contactez l'opérateur de la plateforme au {tél} ou à {e-mail} : il
> enregistrera votre signalement et vous indiquera la démarche à suivre. L'opérateur ne vous
> demandera jamais un code.

That footer ends every notice. Every code message (sign-in code, a code proving a contact, the stop
code, the assisted request's code), which may reach a contact no account holds, ends instead with:

> Vous recevez ce message parce qu'un code a été demandé pour cette adresse sur {nom}. Si vous ne
> l'avez pas demandé, ignorez ce message. L'opérateur ne vous demandera jamais un code :
> {tél}, {e-mail}.

A notice of a pending recovery with the stop link adds: « Le lien ouvre une page qui envoie un code
d'arrêt à ce contact. Il fonctionne sans vous connecter, même si ce contact est bloqué. »

**Screen texts** shown by the client, normative:

| Where | Text |
|---|---|
| No connection (TSK-1, BP002-AC-07) | « La connexion à votre compte demande du réseau. Réessayez une fois connecté. » |
| Creation question (TSK-2) | « Aucun compte ne correspond à {contact}. Un nouveau compte sera créé avec ce contact. Si vous avez déjà un compte avec un autre contact, connectez-vous avec celui-ci. Si vous avez perdu tous vos contacts, demandez une récupération assistée à l'opérateur : {tél}, {e-mail}. » Buttons « Créer le compte » and « Ne pas créer ». |
| Contact suspended (BP002-R10) | « Une récupération de ce compte est en cours, et ce contact est suspendu jusqu'à sa fin. Si vous n'en êtes pas à l'origine, vous pouvez l'arrêter maintenant. » Button « Arrêter la récupération ». |
| Phone-only notice (BP002-D2), at creation and when the second contact is suggested | « Votre compte n'a qu'un numéro de téléphone. Quiconque détient ce numéro peut se connecter à votre compte, et si vous le perdez, seule la récupération assistée par l'opérateur vous y ramènera. Ajoutez une adresse e-mail ou un second numéro. Vous pouvez aussi créer des codes de secours, qui ne dépendent d'aucun numéro. » |
| Second contact offered (BP002-D2) | « Ajoutez un second contact : il vous permettra de récupérer votre compte si vous perdez le premier. » Buttons « Ajouter » and « Plus tard ». |
| Adding a contact (BP002-R6) | « N'ajoutez qu'un contact que vous seul contrôlez : quiconque le contrôle pourra se connecter à votre compte. N'ajoutez jamais le numéro d'un proche ou d'un collègue. » |
| Factor codes shown (ADR-ARCH-048 decision 1) | « Voici vos 10 codes de secours. Chacun ne sert qu'une fois. Notez-les ou imprimez-les, et gardez-les à part de votre téléphone : ils servent si vous perdez un contact ou votre téléphone. Ils ne seront plus affichés. » |
| Few factor codes left (ADR-ARCH-048 decision 1) | « Il vous reste {n} codes de secours. Remplacez-les. » |
| Removal not possible without proof (BP002-AC-28) | « Ce contact ne peut pas être retiré sans être prouvé. Si vous l'avez perdu, demandez une récupération : elle le retirera après 7 jours si personne ne l'arrête. » |
| A recovery pending (BP002-R12, BP002-AC-46) | « Une récupération de ce compte est en cours. Pour l'arrêter, utilisez le lien de l'avis reçu sur le contact concerné, ou annulez-la ici si vous l'avez demandée. » |
| Récupération déjà effectuée: a stop or a cancel of a recovery that has taken effect (TSK-7 Errors, ADR-ARCH-049 decision 6, ADR-ARCH-057 decision 10); the stop page, which the identity host renders, adds « le {date} » after « pris effet » | « Cette récupération a déjà pris effet. Si vous ne la reconnaissez pas, contactez l'opérateur au {tél} ou à {e-mail} pour la contester. Si vous n'avez plus aucun contact de ce compte, demandez-lui une récupération assistée. » |
| Signing out with unsent captures (TSK-10, BP002-AC-64) | « Vos saisies non envoyées restent sur ce téléphone. Elles seront envoyées quand vous vous reconnecterez. » |

## 8. The client's obligations

What TSK-1 to TSK-11, BP002-D2 and BP002-D4 require of the client, without naming a technology:

- Every operation of §3 needs a connection; without one the client sends nothing and shows the text
  of §7 (BP002-R16, BP002-AC-07).
- It sends `create_account=true` only after the person confirms the creation question, and shows its
  alternatives (TSK-2, BP002-AC-01, BP002-AC-05).
- It offers a second contact at creation and suggests it again later, never requiring it; how often
  is the open question of `process.md` (BP002-D2, BP002-AC-08). It shows the phone-only notice and the
  warning of BP002-R6 (§7).
- It refreshes one at a time on its one chain, and refreshes with `scope=identity` before an operation
  of §3.2 (ADR-ARCH-057 decision 2).
- It runs the Turnstile widget for each sign-in code request and each code for a contact the account
  does not hold, and not for a code for a contact the account holds (ADR-ARCH-055 decision 1,
  ADR-ARCH-057 decision 7).
- It keeps captures per author, sends and shows only the current person's, and tells a person who
  signs out with unsent captures (ADR-ARCH-051 decisions 1 and 2; BP002-D4).

**Not covered until the client platform is decided** (`architecture.md` §8): the client parts of
BP002-AC-01, 02, 03, 05, 07, 08, 27, 28, 57, 62, 63 and 64 (§9). The per-person protection of
captures at rest, which carries BP002-D4 against a modified client, waits for the same decision
(ADR-ARCH-051 decision 4).

## 9. Guarantors

Test names carry the identifier as `issues-convention.md` §12 states. Proof levels: "Domain",
"Features" and "Integration" are the Identity module's `SaasBtp.Identity.Domain.Tests`,
`SaasBtp.Identity.Features.Tests` and `SaasBtp.Identity.Adapters.IntegrationTests` (ADR-ARCH-034 §6);
"Host (identity)" is `apps/api/tests/SaasBtp.Identity.IntegrationTests` (ADR-ARCH-020); "Host (API)"
is `SaasBtp.Api.IntegrationTests`; "Access integration" is `SaasBtp.Access.Adapters.IntegrationTests`.
"Client" marks a part not covered until the client platform is decided (§8). Messages are delivered
through a fake of the delivery port; the test reads what was sent, and to whom.

### Invariants

| Invariant | Guarantor in production | Proof |
|---|---|---|
| INV-ACCESS-01 | Identity writes nothing of `access`: `saasbtp_identity` reads `access.profiles(user_id)` only (ADR-ARCH-054 decision 1); a recovery keeps the account and its `sub`, so the profile and its relations (process.md, TSK-9); BP-001's unique key on the profile | Access integration (grants); Host (identity): no profile after a creation, the same `sub` after a recovery |
| INV-ACCESS-10 | The contacts' unique key on the normalized value; one reading of every input (ADR-ARCH-052 decision 4); an addition refused after a correct code (§3.2); a contact passed under BP002-R14 in the effect's one transaction (ADR-ARCH-054 decision 3) | Integration; Domain (reading) |
| INV-ACCESS-11 | Level 1: `contact_confirmed` set at every issuance from a code-confirmed contact (ADR-ARCH-043 decision 1.5) and the last contact never removed (BP002-R4). Level 2: a factor proof serves one operation within 5 minutes (ADR-ARCH-048 decision 6); an operation that requires it is refused without one (§3.2) | Domain; Integration; Host (identity) |

### Scenarios

| Scenario | Guarantor in production | Proof |
|---|---|---|
| BP002-AC-01 | The grant: `account_creation_required` without `create_account`, the account with P1 confirmed and tokens with it; no profile (INV-ACCESS-01). The client: the question, the second contact offered, the phone-only notice | Host (identity); Client |
| BP002-AC-02 | As AC-01, with E1; no phone-only notice | Host (identity); Client |
| BP002-AC-03 | Adding a contact (§3.2) right after creation; the warning of BP002-R6 | Host (identity); Client |
| BP002-AC-04 | A sign-in resolves the account through the contact (ADR-ARCH-042 decision 3) | Host (identity) |
| BP002-AC-05 | Nothing created without `create_account=true`; the code not used. The client: the alternatives and the decline | Host (identity); Client |
| BP002-AC-06 | §4: whole responses compared for P1 and P2 (request, wrong code, expiry) | Host (identity) |
| BP002-AC-07 | The client sends nothing offline; no code sent | Client |
| BP002-AC-08 | A sign-in needs one contact only. The client: the suggestion and its notice | Host (identity); Client |
| BP002-AC-09 | `contact_confirmed` true at every issuance; the last contact never removed (INV-ACCESS-11) | Host (identity); Domain |
| BP002-AC-10 | The reading of ADR-ARCH-052 decision 1, with its forms | Domain; Host (identity) |
| BP002-AC-11 | The normalizer of ADR-ARCH-052 decision 3 | Domain; Host (identity) |
| BP002-AC-12 | ADR-ARCH-046 decision 6, wrong code then right one | Features; Host (identity) |
| BP002-AC-13 | Expiry at 10 minutes, at 9:59 and 10:01 (ADR-ARCH-046 decision 6) | Features; Host (identity) |
| BP002-AC-14 | The code used by its sign-in (§3.1) | Integration; Host (identity) |
| BP002-AC-15 | One live code per contact (ADR-ARCH-046 decision 3) | Integration |
| BP002-AC-16 | The lock per contact (ADR-ARCH-047 decision 1), the right code refused during it | Features; Integration; Host (identity) |
| BP002-AC-17 | The interval and hourly limit, the same answer with and without an account (ADR-ARCH-046 decision 4) | Host (identity) |
| BP002-AC-18 | One challenge per contact: E1 unaffected by P1's lock | Integration |
| BP002-AC-19 | §4 for the lock | Host (identity) |
| BP002-AC-20 | The lock ends after 15 minutes; a new code signs in; the account unchanged | Integration |
| BP002-AC-21 | The factor's locks (ADR-ARCH-047 decision 3); another session's failures do not stop hers | Features; Integration |
| BP002-AC-22 | Adding a contact; the notice to P1 with the operator's contact information (§7) | Host (identity) |
| BP002-AC-23 | 401 without an identity token (ADR-ARCH-057 decision 3) | Host (identity) |
| BP002-AC-24 | 202 identical before the code; 409 after a correct code; both accounts unchanged | Host (identity) |
| BP002-AC-25 | Changing a contact with both codes; the notice to P1 | Host (identity) |
| BP002-AC-26 | Removing with the removed contact's code; notices to P1 and E1 | Host (identity) |
| BP002-AC-27 | 409, the last contact (BP002-R4). The client: offers to change it | Domain; Host (identity); Client |
| BP002-AC-28 | 403 without the contact's code or a proof. The client: the self-service recovery offered | Host (identity); Client |
| BP002-AC-29 | Registering requires a code for every contact; notices to P1 and E1 | Host (identity) |
| BP002-AC-30 | Adding refused without a factor proof, accepted with one | Host (identity) |
| BP002-AC-31 | Removing P1 with a factor proof, at once; notices | Host (identity) |
| BP002-AC-32 | Removing the factor with a proof; notices to every contact | Host (identity) |
| BP002-AC-33 | Removing the factor with every contact's code; refused with one | Host (identity) |
| BP002-AC-34 | Replacing with a proof; refused without a proof or every contact's code; the former codes voided | Host (identity) |
| BP002-AC-35 | A proof bound to its operation and target, used once (ADR-ARCH-048 decision 6) | Features; Integration |
| BP002-AC-36 | Ending other sessions; the API refuses the ended session (ADR-ARCH-043 decision 2); the request; each notice's way to stop (ADR-ARCH-049 decision 2: P1 the link, E1 the cancel); the effect after 7 days by the scheduler; notices | Host (identity); Host (API); Integration |
| BP002-AC-37 | `contact_suspended` for a correct code; the offer; the stop at sign-in | Host (identity) |
| BP002-AC-38 | The request ends every other session and suspends P1; the effect; P1 then creates an account; or P1's holder stops from the notice | Host (identity); Integration |
| BP002-AC-39 | A recovery that removes P1 and adds P2; the effect | Integration |
| BP002-AC-40 | As AC-38; the stop by P1's holder; the second request after ADR-ARCH-056's wait; the effect with nobody stopping | Integration |
| BP002-AC-41 | The request by P1's holder suspends E1; Awa's stop from the notice; she then recovers P1; the requester's wait grows (ADR-ARCH-056) | Host (identity); Integration |
| BP002-AC-42 | The stop with a code to P1; after the carrier's block no code to P1 is entered, so the effect takes place | Integration |
| BP002-AC-43 | A recovery removing the factor: E1's notice carries the link; the stop keeps the factor | Host (identity) |
| BP002-AC-44 | 403 without a factor proof when the recovery adds a contact | Host (identity) |
| BP002-AC-45 | The cancel; notices to P1 and E1; P1 signs in again | Host (identity) |
| BP002-AC-46 | 409 for a second recovery and for every change while one is pending | Host (identity) |
| BP002-AC-47 | 401 not signed in; 403 signed in with E1 for a recovery removing E1 | Host (identity) |
| BP002-AC-48 | Two phone contacts on one account; the effect leaves P2 | Integration |
| BP002-AC-49 | ADR-ARCH-054 decision 3: the other account ends, P2 passes, its notice | Integration; Access integration |
| BP002-AC-50 | ADR-ARCH-054 decisions 3 and 4: a profile created during the delay seen at the effect; no effect; the other account's sessions unchanged | Integration; Host (API) |
| BP002-AC-51 | The stop page outside the locks (ADR-ARCH-047 decision 4, ADR-ARCH-049 decision 3) | Host (identity) |
| BP002-AC-52 | The console request and the requester's page; every session ended and every means suspended; the effect; the record of ADR-ARCH-050 decision 3 with its outcome row | Host (identity); Integration |
| BP002-AC-53 | `contact_suspended` during an assisted recovery | Host (identity) |
| BP002-AC-54 | The stop from E1's notice; the new contact not added | Host (identity) |
| BP002-AC-55 | The stop page accepting a factor code (ADR-ARCH-049 decision 4) | Host (identity) |
| BP002-AC-56 | The console's one outcome and the requester's page's one answer after a correct code, for P1 and P3 (§3.4, §4) | Host (identity) |
| BP002-AC-57 | The grant's `account_creation_required` and the operator's contact; the assisted effect with P2 holding an account without a profile (ADR-ARCH-054 decision 3). The client: the warning and the offer | Integration; Client |
| BP002-AC-58 | P1's holder receives the notice and is refused at sign-in; the effect; or their stop | Integration |
| BP002-AC-59 | At the request, when the new contact's account has a profile (ADR-ARCH-054 decision 2): the self-service request's 409 "The new contact cannot be used." (§3.2); the requester's page's refusal, the same whether or not an account holds the former contact (§3.4) | Host (identity); Access integration |
| BP002-AC-60 | The console records the dispute; no means changes (ADR-ARCH-050 decision 2) | Host (identity) |
| BP002-AC-61 | Ending other sessions; the API's 401 at the next request; the lost phone's refresh refused | Host (identity); Host (API) |
| BP002-AC-62 | The refresh refused after the session ends; the captures kept and sent after signing in again | Host (identity); Client |
| BP002-AC-63 | The device sends and shows only the current person's captures. The API's author check (ADR-ARCH-051 decision 3) needs a capture-writing endpoint, which no process has yet (§10) | Client |
| BP002-AC-64 | The session ended on sign-out. The client: the text of §7, the captures kept | Host (identity); Client |

The verifications each ADR lists apply as written: ADR-ARCH-043 (Consequences), ADR-ARCH-044
(Consequences), ADR-ARCH-046 decision 6, ADR-ARCH-047 decision 5, ADR-ARCH-049 decision 7,
ADR-ARCH-050 decision 5, ADR-ARCH-052 decision 5, ADR-ARCH-053 (Consequences), ADR-ARCH-054
decision 9, ADR-ARCH-055 decision 8, ADR-ARCH-056 decision 5, ADR-ARCH-057 decision 11. The tests of
ADR-ARCH-032 §6 that concern a subject do not apply to `identity`, whose policies admit a role
(ADR-ARCH-053 decision 3); `MappingMatchesMigratedSchema` applies to the Identity module's context
(ADR-ARCH-053 decision 2). The message texts of §7 are checked by a test that renders each one with
its placeholders at their longest and counts its SMS segments.

## 10. Open points

Cited, not decided here:

- The open questions of `process.md`: how often the second contact is suggested; whether a factor is
  asked at every sign-in; whether a sign-in from a new device is notified; more than two contacts;
  closing an account; adding outside a recovery a contact held by an account without a profile; how
  the new holder of a recycled number says the account is not theirs.
- The field client's platform (`architecture.md` §8), on which §8's parts wait.
- The legal conformity of the records' retention (ADR-ARCH-050 decision 4).
- The build, publication, deployment and health check of the identity host (ADR-ARCH-042,
  Consequences).
- The API's author check on captures (ADR-ARCH-051 decision 3), with the first capturing process.

## 11. Verification

From the repository root, with Docker running, the commands of `apps/api/CLAUDE.md`:

```bash
dotnet build SaasBtp.slnx -c Release
scripts/ci/test-api.sh \
  --solution SaasBtp.slnx \
  -c Release \
  --no-build
```

One scenario, on the project that holds its test:

```bash
scripts/ci/test-api.sh \
  --project apps/api/src/modules/Identity/tests/SaasBtp.Identity.Adapters.IntegrationTests \
  -c Release \
  --no-build \
  --filter-method "*BP002_AC_49*"
```

The identity host's proofs, and the API host's:

```bash
scripts/ci/test-api.sh \
  --project apps/api/tests/SaasBtp.Identity.IntegrationTests \
  -c Release \
  --no-build
scripts/ci/test-api.sh \
  --project apps/api/tests/SaasBtp.Api.IntegrationTests \
  -c Release \
  --no-build
```

Every identifier of §9 names at least one test, except the parts marked "Client".

## References

Every decision this contract cites, and where it is:

- ADR-ARCH-018: `.claude-context/context/decisions/architecture/ADR-ARCH-018-http-api-surface.md`
- ADR-ARCH-019: `.claude-context/context/decisions/architecture/ADR-ARCH-019-schema-migrations-dbup.md`
- ADR-ARCH-020: `.claude-context/context/decisions/architecture/ADR-ARCH-020-dotnet-solution-layout-hosts-and-casing.md`
- ADR-ARCH-021: `.claude-context/context/decisions/architecture/ADR-ARCH-021-test-project-conventions.md`
- ADR-ARCH-025: `.claude-context/context/decisions/architecture/ADR-ARCH-025-runtime-secrets-doppler-per-role.md`
- ADR-ARCH-027: `.claude-context/context/decisions/architecture/ADR-ARCH-027-migration-runner-role-and-connection.md`
- ADR-ARCH-028: `.claude-context/context/decisions/architecture/ADR-ARCH-028-api-runtime-role.md`
- ADR-ARCH-031: `.claude-context/context/decisions/architecture/ADR-ARCH-031-access-context-and-workspace-tenant.md`
- ADR-ARCH-032: `.claude-context/context/decisions/architecture/ADR-ARCH-032-cross-module-communication-and-authorization.md`
- ADR-ARCH-033: `.claude-context/context/decisions/architecture/ADR-ARCH-033-module-persistence.md`
- ADR-ARCH-034: `.claude-context/context/decisions/architecture/ADR-ARCH-034-module-structure.md`
- ADR-ARCH-035: `.claude-context/context/decisions/architecture/ADR-ARCH-035-microkit-in-libraries.md`
- ADR-ARCH-037: `.claude-context/context/decisions/architecture/ADR-ARCH-037-microkit-copy-from-latest-main.md`
- ADR-ARCH-038: `.claude-context/context/decisions/architecture/ADR-ARCH-038-identity-token-validation-and-level.md`
- ADR-ARCH-040: `.claude-context/context/decisions/architecture/ADR-ARCH-040-api-error-bodies.md`
- ADR-ARCH-041: `.claude-context/context/decisions/architecture/ADR-ARCH-041-supabase-settings-check-credential.md`
- ADR-ARCH-042: `.claude-context/context/decisions/architecture/ADR-ARCH-042-bp-002-identity-service-of-the-platform.md`
- ADR-ARCH-043: `.claude-context/context/decisions/architecture/ADR-ARCH-043-api-validates-identity-service-tokens.md`
- ADR-ARCH-044: `.claude-context/context/decisions/architecture/ADR-ARCH-044-identity-tokens-sessions-and-signing-keys.md`
- ADR-ARCH-045: `.claude-context/context/decisions/architecture/ADR-ARCH-045-delivery-of-codes-and-notices.md`
- ADR-ARCH-046: `.claude-context/context/decisions/architecture/ADR-ARCH-046-sign-in-code-form-lifetime-renewal.md`
- ADR-ARCH-047: `.claude-context/context/decisions/architecture/ADR-ARCH-047-locks-on-failed-attempts.md`
- ADR-ARCH-048: `.claude-context/context/decisions/architecture/ADR-ARCH-048-stronger-factor-saved-one-time-codes.md`
- ADR-ARCH-049: `.claude-context/context/decisions/architecture/ADR-ARCH-049-recovery-delay-and-stop-from-the-notice.md`
- ADR-ARCH-050: `.claude-context/context/decisions/architecture/ADR-ARCH-050-platform-operator-channel-and-records.md`
- ADR-ARCH-051: `.claude-context/context/decisions/architecture/ADR-ARCH-051-captures-per-person-on-a-shared-device.md`
- ADR-ARCH-052: `.claude-context/context/decisions/architecture/ADR-ARCH-052-reading-and-recording-a-contact.md`
- ADR-ARCH-053: `.claude-context/context/decisions/architecture/ADR-ARCH-053-identity-persistence-and-session-check.md`
- ADR-ARCH-054: `.claude-context/context/decisions/architecture/ADR-ARCH-054-identity-reads-whether-an-account-has-a-profile.md`
- ADR-ARCH-055: `.claude-context/context/decisions/architecture/ADR-ARCH-055-protection-against-automated-code-requests.md`
- ADR-ARCH-056: `.claude-context/context/decisions/architecture/ADR-ARCH-056-how-often-a-recovery-may-be-requested.md`
- ADR-ARCH-057: `.claude-context/context/decisions/architecture/ADR-ARCH-057-identity-host-person-api.md`
- `naming.md`, `sql.md`: `.claude-context/context/architecture/conventions/`
- `issues-convention.md`: `.claude-context/context/product/issues-convention.md`
- `architecture.md`: `.claude-context/context/architecture/architecture.md`
- `process.md`, `acceptance.md`: `docs/business-processes/BP-002-access-platform/`; `docs/domain/access.md`

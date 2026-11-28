# ADR-ARCH-055 — Protection against automated requests for codes: a Cloudflare Turnstile check on every code request, a destination list, per-contact and per-IP limits, and a daily spending ceiling per route

Status: accepted
Date: 2026-11-28
Refs: issue #248 (Scope item 4); BP-002 `process.md` (TSK-1, TSK-3, TSK-6, TSK-9, BP002-R2,
BP002-R3, BP002-R11, BP002-R15), `acceptance.md`; ADR-PROD-001; ADR-ARCH-042; ADR-ARCH-045;
ADR-ARCH-046; ADR-ARCH-047; ADR-ARCH-049; ADR-ARCH-050; ADR-ARCH-052; ADR-ARCH-056.

## Context
#248's item 4 asks for the protection against automated requests for codes. Every code costs a
message (ADR-ARCH-045), and BP002-R2 requires that "The answers to asking for a code, to a wrong code,
to an expired code and to a lock are the same whether or not an account holds the contact". An
earlier draft relied on a hard daily cap of 200 SMS codes; with the per-contact and per-IP limits
alone, about 40 numbers from one IP address emptied it in about two hours, and every phone-only person
was then kept from signing in until the next day. The maintainer's direction of 2026-11-28, in the /fix-issue
248 session, rejects that: a cap anyone can drain cheaply "turns a spending limit into a cheap denial of sign-in";
it directs a Cloudflare Turnstile check on every code request, a destination list set in a setting and
at the provider, and the cap kept as a spending ceiling only. This decision was decision 4 of
ADR-ARCH-047 before ADR-ARCH-047 was split into one ADR per problem (the maintainer's decision of
2026-11-28 in the /fix-issue 248 session).

What the sources state, read on 2026-11-28:
- Cloudflare Turnstile, plans (https://developers.cloudflare.com/turnstile/plans/): Free, "Up to 20
  widgets", "10 hostnames per widget", "Unlimited challenges (traffic or verification requests)", no
  "Any hostname widget", pre-clearance included; Enterprise: "Contact Sales". Hostname management:
  "You cannot create a widget without specifying at least one authorized hostname." No rate limit or
  service level for Siteverify is published on the pages read.
- Server-side validation (https://developers.cloudflare.com/turnstile/get-started/server-side-validation/):
  "You must call the Siteverify API to complete your Turnstile implementation. The client-side widget
  alone does not protect your forms."; `POST https://challenges.cloudflare.com/turnstile/v0/siteverify`
  with `secret`, `response`, and optionally `remoteip` and `idempotency_key` ("A UUID you generate to
  safely retry validation requests"); the answer carries `success`, `challenge_ts`, `hostname`,
  `error-codes`, `action` and `cdata`; "Validate the action and hostname when specified"; "Each token
  is valid for 300 seconds (5 minutes) after generation"; "Tokens are intended for single use", with
  reuse detection that "is eventually consistent": "Multiple validations submitted close together may
  succeed before redemption state propagates". A replayed or expired token answers
  `timeout-or-duplicate`. The page advises "Have fallback behavior for API failures", "Set reasonable
  timeouts" and "Implement retry logic"; it states no fail-open or fail-closed policy, and its samples
  refuse when the call fails.
- Mobile (https://developers.cloudflare.com/turnstile/get-started/mobile-implementation/): "For native
  mobile applications, Turnstile does not run natively. Instead, you use a WebView ... to load a
  webpage that contains the Turnstile widget", with JavaScript and DOM storage enabled, access to
  `challenges.cloudflare.com`, `about:blank` and `about:srcdoc`, and a "Consistent User Agent
  throughout the session". Supported browsers
  (https://developers.cloudflare.com/cloudflare-challenges/reference/supported-browsers/): "WebViews
  in mobile applications may have limited functionality compared to full browsers". No native SDK and
  no list of supported WebViews is published. Whether the WebView page may be a local file is not
  stated.
- Widget modes (https://developers.cloudflare.com/turnstile/concepts/widget/): Managed "will only
  require interaction if a further check is necessary ... the visitor will be prompted to select a box.
  There will be no images or text to decipher"; Non-Interactive: visitors "will never be required or
  prompted to interact"; Invisible requires citing Cloudflare's Turnstile Privacy Addendum. The
  overview states "Turnstile is WCAG 2.2 AA compliant".
- Twilio SMS Geo Permissions (https://www.twilio.com/docs/messaging/guides/sms-geo-permissions): "SMS
  Geo Permissions control which countries you can send SMS messages to"; set in "Console > Messaging >
  Settings > Geo Permissions" by an Account Owner or Admin; changes "take effect immediately"; "SMS
  Geo-Permissions can *not* be changed programmatically via the API for security reasons" (also error
  30649, https://www.twilio.com/docs/api/errors/30649); a send to a blocked country fails with error
  21408; "By default a subaccount inherits the SMS Geo Permissions settings of its parent account".
- Read on 2026-11-27: ASP.NET Core rate limiting keeps "its counters in memory in each app instance
  ... The effective limit is the configured limit multiplied by the number of instances"; the
  Forwarded Headers Middleware (decision 7).
- ADR-PROD-001: the market is Côte d'Ivoire.

## Decision
1. **A Turnstile check on every code request.**
   - Every request for a sign-in code (TSK-1) or for a code proving a new contact (TSK-3, TSK-6, and
     TSK-9 from the operator console, ADR-ARCH-050) carries a Turnstile token, for every contact
     alike. The identity host validates it with Siteverify before anything else is counted or sent,
     passing the client's address (decision 7) as `remoteip` and a UUID as `idempotency_key`, and
     requires `success`, the widget's `hostname` and the request's `action`.
   - A token is accepted once by the identity host itself: the hash of each accepted token is kept
     for its 300 seconds of validity and a repeat is refused, since Cloudflare's own reuse detection
     is eventually consistent.
   - A missing, invalid, expired or repeated token is refused with nothing sent and nothing counted
     against the contact, and with the same answer for every contact, whether or not an account holds
     it (BP002-R2). The person is asked to try again.
   - Siteverify unreachable or failing, after a timeout and one retry with the same
     `idempotency_key`: the request is refused, nothing is sent (fail closed). Notices, stop codes and
     sessions already open do not depend on Turnstile.
   - The widget runs in Managed mode: most people are not asked anything, and when a check is needed
     it is a box to tick, workable with gloves. One widget per environment, bound to the identity
     service's hostname; the free plan.
   - The field client: on a web client, the widget is on the identity service's sign-in page; in a
     native client, that page is loaded in a WebView meeting Cloudflare's requirements above. Since
     Cloudflare gives WebViews limited support, the client platform's decision verifies the widget on
     the field devices before relying on it.
   - Not on the stop page, the stop code or the notices: the stop needs only the notice
     (BP002-R11, ADR-ARCH-049).
2. **Destinations.** The identity service sends SMS only to the country codes listed in a setting:
   `+225` in production (ADR-PROD-001), `+225` and `+33` in staging. A phone number of another country
   is refused when it is read (ADR-ARCH-052), before any code is requested. Twilio's SMS Geo
   Permissions of each environment's account are restricted to the same list, as a second layer the
   identity service cannot widen; they are set by the maintainer in Twilio's Console, the only place
   they can be changed. A staging subaccount has its inheritance disabled so that its list can differ.
   This is a product limit: a person whose only phone number is outside the list cannot use it, and
   signs in with an e-mail address; opening a country changes the setting and the Console together,
   with its delivery cost read again (ADR-ARCH-045).
3. **Per contact.** ADR-ARCH-046's interval (60 s) and hourly limit (5), and ADR-ARCH-047's lock.
4. **Per IP address.** At most 100 code requests an hour, a limit high enough for many people behind
   one carrier-grade NAT address; above it, the answer is the same as any refusal (BP002-R2). It runs
   in-process, in the identity host's single instance; with more instances it would need a shared
   store.
5. **A daily spending ceiling per route, not a protection against automation.**
   - SMS: at most 200 codes a day, the same maximum daily spend as before, $98.50 at Twilio's $0.4925
     per segment (ADR-ARCH-045 decision 7). E-mail: at most 1,000 codes a day, so that codes use at
     most 30,000 of Resend Pro's 50,000 e-mails a month and leave the rest to notices and stop codes,
     with no spending beyond the plan (ADR-ARCH-045). Both are settings; a change of either is a
     spending decision of the maintainer.
   - They count sign-in codes and codes proving a new contact, once the Turnstile check has passed;
     they reset at midnight UTC.
   - The operator is alerted when a route reaches 50 % of its ceiling and when it reaches 100 %.
   - Above a ceiling, a code request on that route is refused, nothing is sent, and the person is
     told when to try again (at the reset), as TSK-1's error "told when to try again, nothing sent"
     states; the answer is the same for every contact (BP002-R2), since the ceiling depends on no
     account.
   - The exposure that remains: someone who obtains a valid Turnstile token for each request, from
     real browsers or a solving service, and uses about 40 numbers within the per-contact and per-IP
     limits, can still reach the SMS ceiling, in about an hour from two addresses. Then, until the
     reset, nobody can sign in by SMS code: a phone-only person cannot sign in on a new device; a
     person with an e-mail address signs in with it; sessions already open keep refreshing up to their
     30 days (ADR-ARCH-044); notices and stop codes are still sent (decision 6), so a recovery can
     still be stopped. The 50 % alert lets the operator see the volume grow and the maintainer decide
     on the ceiling the same day. Reaching it costs the attacker 200 solved challenges and the numbers,
     where the earlier draft cost them nothing but the numbers.
6. **Notices and stop codes are outside the ceiling and the Turnstile check.** A notice (BP002-R15) and
   a stop code (ADR-ARCH-049) are never refused by the ceiling or by a provider quota of this platform.
   A notice of a recovery that fails to send is retried until the provider accepts it or the recovery
   has ended; any other notice is retried for 24 hours, then the operator is alerted. Their volume is
   bounded because every path that triggers one is signed in or goes through the operator:

   | Notice or stop code | Triggered by | Bounded by |
   |---|---|---|
   | A change of the means (TSK-3, TSK-4, TSK-5) | A signed-in person | Being signed in: a sign-in code, under decisions 1 to 5 |
   | A self-service recovery requested, taking effect, stopped or cancelled (TSK-6, TSK-7) | A signed-in person, then the delay or the stop | ADR-ARCH-056 |
   | An assisted recovery requested, taking effect or stopped (TSK-9, TSK-7) | The operator, who takes the request | The operator's handling and ADR-ARCH-056 |
   | A stop code (ADR-ARCH-049) | The holder of a pending recovery's notice, on its stop page, without signing in | The recovery behind the notice was requested signed in or through the operator; one pending recovery per account (BP002-R12); the stop code's growing interval (ADR-ARCH-049 decision 3): at most about 170 stop codes per notice over the 7 days |
   | An account ended by a recovery (BP002-R14) | A recovery taking effect | ADR-ARCH-056 |

   An alert to the operator is raised when the notices and stop codes sent in a day exceed a set value
   (initial value 200 SMS), so that abuse of these uncapped paths is seen the same day; it never stops
   a notice or a stop code.
7. **The client address behind the proxy.** The identity host runs behind Coolify's reverse proxy.
   The per-IP limit and Siteverify's `remoteip` use the address ASP.NET Core's Forwarded Headers
   Middleware sets from `X-Forwarded-For`, with the proxy's address as the only known proxy and one
   forwarded hop, the middleware first in the pipeline; a header from any other source is ignored, so
   a client cannot choose its address. Microsoft Learn: "The request's original remote IP must match an
   entry in the KnownProxies or KnownNetworks lists before forwarded headers are processed", by
   default "Only loopback addresses", and "If the default value (`1`) is used, only the rightmost value
   from the headers is processed".
8. **Verification.**

   | Requirement | Verification |
   |---|---|
   | The Turnstile check | A code request without a token, with an invalid one (Cloudflare's test secret keys), with a repeated one, and with Siteverify unreachable: refused, nothing sent, nothing counted, the same answer for a contact an account holds and for one none holds |
   | Destinations | A number of a country off the setting's list is refused when read; the staging and production lists; the Console setting checked by the maintainer at deployment |
   | Per contact and per IP | BP002-AC-17; the 101st request in an hour from one address is refused with the same answer |
   | The spending ceiling | Alerts at 50 % and 100 %; above it, the same refusal for every contact with when to try again, nothing sent; the count resets at midnight UTC |
   | Notices and stop codes outside the ceiling and the check | Sent above the ceiling and without a token; the daily alert |
   | The client address | A request through the proxy is partitioned by the client's address; a forged header is ignored |

## Consequences
- Cloudflare becomes a third party of every code request: it sees the browser of each person who asks
  for a code. Its secret key goes to the identity host's Doppler config and to Bitwarden.
- A Cloudflare outage stops sign-in by code on new devices; existing sessions, notices and stop codes
  are not affected.
- The maintainer creates one Turnstile widget per environment and sets Twilio's Geo Permissions in the
  Console; neither can be set by the identity service.
- The counts and the ceilings are kept in schema `identity` (ADR-ARCH-053), so that they hold across
  restarts; only the per-IP limiter is in memory.
- No spending: Turnstile's free plan. The ceiling's spending is ADR-ARCH-045's.

## Alternatives considered
- **A hard daily cap as the protection (the earlier draft).** Rejected by the maintainer: about 40
  numbers from one address drained it in two hours, for a whole day, for every phone-only person.
- **No ceiling.** Rejected: a stream of solved challenges would then turn into unbounded spending.
- **A daily limit of codes per contact.** Rejected: anyone knowing the number could use it up.
- **A ceiling reserved for contacts an account holds.** Rejected: refusing only unknown contacts would
  tell them apart, against BP002-R2.
- **Device attestation.** Not decided here: it depends on the client platform, an open decision.
- **Other challenge providers.** Not compared; Turnstile is the maintainer's direction.

## References
Read on 2026-11-28:
- Cloudflare: https://developers.cloudflare.com/turnstile/;
  https://developers.cloudflare.com/turnstile/plans/;
  https://developers.cloudflare.com/turnstile/get-started/server-side-validation/;
  https://developers.cloudflare.com/turnstile/get-started/mobile-implementation/;
  https://developers.cloudflare.com/turnstile/concepts/widget/;
  https://developers.cloudflare.com/turnstile/additional-configuration/hostname-management/;
  https://developers.cloudflare.com/turnstile/troubleshooting/testing/;
  https://developers.cloudflare.com/cloudflare-challenges/reference/supported-browsers/.
- Twilio: https://www.twilio.com/docs/messaging/guides/sms-geo-permissions;
  https://www.twilio.com/docs/api/errors/30649; https://www.twilio.com/docs/api/errors/21408.

Read on 2026-11-27:
- Microsoft Learn, "Configure ASP.NET Core to work with proxy servers and load balancers"
  (https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0);
  "Rate limiting middleware in ASP.NET Core"
  (https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0).

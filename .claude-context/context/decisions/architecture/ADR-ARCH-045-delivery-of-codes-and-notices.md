# ADR-ARCH-045 — How a code and a notice reach a phone number and an e-mail address: SMS through Twilio, e-mail through Resend, each behind a port

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope item 1; "For each channel retained"); BP-002 `process.md` (BP002-R1,
BP002-R11, BP002-R15; actor "The delivery of a code to a contact"); ADR-ARCH-025; ADR-ARCH-042;
ADR-ARCH-046; ADR-ARCH-049; ADR-ARCH-052; ADR-ARCH-055.

## Context
BP-002 sends a code to a phone number or an e-mail address (TSK-1), a notice to every contact on
every change of the means and every recovery (BP002-R15), and a stop code to a contact that stops a
recovery (ADR-ARCH-049). Its users are in Côte d'Ivoire (ADR-PROD-001), on Android phones, not all
with an e-mail address (`docs/product/scope-phase-1.md`). #248 asks, for each channel retained, its
availability in Côte d'Ivoire, its cost per message and per failed or resent code, and its dependence
on a third party, each read on the provider's official page and dated.

What the official pages state, read on 2026-11-27:

- **Twilio Programmable SMS** (https://www.twilio.com/en-us/sms/pricing/ci, "Pricing current as of
  November 2026"):
  - "International Numbers $0.4925" and "Alphanumeric Sender IDs $0.4925" per outbound message;
    "Text messages are charged per segment. ... Prices may change from time to time without notice
    and additional carrier fees may apply."
  - "A failed message processing fee of $0.001 per message will be applied only to messages that
    terminate in a "Failed" status." Twilio's changelog of 2024-08-20 adds: "This does not affect
    messages that end in any other status."
  - Twilio's price list (https://assets.cdn.prod.twilio.com/pricing-csv/SMSPricing.csv) has a row at
    0.49250 for each of "Ivory Coast Outbound Sms - Moov", "- Mtn", "- Orange", "- Greenn", "- Koz"
    and "- Other".
  - "SMS pumping protection Detect and block SMS pumping fraud $0.025", on the same page, with no unit
    stated.
- **Twilio's guidelines for Côte d'Ivoire** (https://www.twilio.com/en-us/guidelines/ci/sms):
  alphanumeric sender ID pre-registration "Required", "Supported" by Twilio, provisioning "3 weeks",
  "UCS-2 support ... Supported"; "Sender ID Registration is Required. Submission with unregistered
  Sender IDs will be attempted on a best effort basis."; "Please refrain from using requesting generic
  sender IDs like InfoSMS, INFO, Verify, Notify, etc."; "Two-way SMS supported ... No"; "You cannot
  send SMS to a landline destination number ... the account will not be charged". It names the
  operators Moov (01), MTN (05) and Orange (07) in its numbering note.
- **Not stated** on the Twilio pages read (the pricing page, the price list, the US pricing page its
  "carrier fees section" link points to, the changelog, the guidelines): a carrier fee for Côte
  d'Ivoire; the cost of a message that ends "Undelivered"; whether delivery receipts are returned for
  Côte d'Ivoire. Two help articles that may state the second were unreadable (one renders nothing
  without JavaScript, one answers 403).
- **Orange "SMS Côte d'Ivoire"** (https://developer.orange.com/apis/sms-ci/, overview, pricing, FAQ
  and "Orange Specific Terms for SMS Cote d'Ivoire", "Version number: 2.0 Date: Jan 25th 2016", at
  https://developer.orange.com/apis/sms-ci/terms-and-conditions):
  - availability: "Available Only in and to Cote d'Ivoire any operator", and the terms send SMS "to
    subscribers of anyoperators located in Côte d'Ivoire";
  - the FAQ: "Delivery issues to MTN users We are aware that there are some delivery issues towards
    MTN users. This is due to SMS management rules on MTN.";
  - bundles bought with "Airtime & Orange Money": 20 SMS for 145 FCFA (7 days), 100 for 725 FCFA (30
    days), 1,000 for 7,260 FCFA (45 days), 10,000 for 72,600 FCFA (60 days); "100 000 FCFA purchase
    per day per SIM card"; 5 transactions per second; "Orange will not refund any SMS even if you
    receive a Delivery Impossible status";
  - a sender name is approved by Orange: "the processing time is within 5 working days (for MTN 15
    working days)";
  - who may subscribe: "any clients of Orange ... to buy, with their airtime credit, bundles of SMS";
  - its terms of use (§4.9, "Usage and content of SMS"): "You shall only make a personal and private use of the acquired SMS." and
    "You will not send any SMS to Subscribers between ten(10) PM and six (06)AM, unless it is a
    Receiver's demand. In addition, the Client cannot send more than three (03) SMS messages per day."
- **Bird** (https://bird.com/pricing/sms/ci): "Alphanumeric | $0.34 | /segment", "No registration
  required". Its pages read state no cost for a failed message and nothing on delivery receipts.
- **Twilio Verify** (https://www.twilio.com/en-us/verify/pricing and its API docs): "$0.05 per
  successful verification plus standard channel fees"; "SMS: Message attempts are always charged and
  not subject to delivery"; a verification's validity "10 minutes", which "We are unable to change";
  its own limits of 5 checks (error 60202) and 5 sends (error 60203).
- **WhatsApp** (Meta, https://developers.facebook.com/docs/whatsapp/pricing and its pricing endpoint,
  and https://www.twilio.com/en-us/whatsapp/pricing): "Effective July 1, 2025 – Meta charges on a
  per-message basis", for a delivered message; Côte d'Ivoire is in "Rest of Africa", authentication
  at $0.004 per delivered message for the first tier; Twilio adds $0.005. An authentication template's
  text is fixed ("<VERIFICATION_CODE> is your verification code."), and approved by Meta.
- **Vonage** and **Africa's Talking**: their pricing pages answered HTTP 403; nothing is verified.
- **Resend** (https://resend.com/pricing and its documentation):
  - Free: "$0 / mo | 3,000 emails / mo | 100 emails a day | 3 domains"; Pro: "$20 / mo | 50,000 emails
    / mo | Extra emails: | $0.90 | / 1,000 | ... | No daily email limit";
  - "Both sent emails and received emails (inbound) count towards your account's email quota"; "When a
    quota stops a send, the API returns a `429` error ... Resend doesn't queue or retry that email";
    "once emails have been sent, refunds will not be issued for partial quota usage"; "All accounts
    must maintain a bounce rate of under 4%";
  - "All account data, including email metadata, logs, and API records, is stored in the United
    States regardless of the sending region you select"; sending regions are us-east-1, eu-west-1,
    sa-east-1 and ap-northeast-1;
  - not stated on its pricing page, documentation, terms of service or acceptable use policy: any
    restriction on recipient countries, availability in Côte d'Ivoire, and whether a bounced or
    suppressed e-mail counts towards the quota.
- **Amazon SES** (https://aws.amazon.com/ses/pricing/): "$0.10 per 1,000 emails" outbound, à la carte;
  charged "per recipient".

## Decision
1. **One delivery port.** The Identity module sends every code, notice and stop code through one
   port of its own, with one adapter per channel. The adapters are the only code that knows a
   provider; switching a provider changes an adapter and its configuration.
2. **Phone: SMS through Twilio Programmable SMS.** One route, with a pre-registered alphanumeric
   sender ID, not a generic one. A message is sent only to a number of a country on the destination
   list, a setting (ADR-ARCH-055 decision 2, ADR-ARCH-052); Twilio's SMS Geo Permissions are
   restricted to the same list by the maintainer in Twilio's Console, the only place they can be
   changed, as a second layer. Delivery status callbacks are recorded, for the operator's investigation, not
   relied on: delivery receipts for Côte d'Ivoire are not stated by Twilio.
3. **E-mail: Resend.** A verified sending domain of the platform. Production on a plan with no daily
   limit (Pro), so that code requests cannot exhaust a provider quota that notices and stop codes
   share (ADR-ARCH-055's spending ceiling keeps e-mail codes below the monthly quota). Staging may use the Free
   plan.
4. **Twilio's "SMS pumping protection" is not taken now.** Its price is shown without a unit, and the
   platform's own measures already bound pumping: the destination list, mobile numbers only
   (ADR-ARCH-052), Twilio's Geo Permissions, the Turnstile check, the per-contact and per-IP limits
   and the daily spending ceiling (ADR-ARCH-055). It is reconsidered if pumping is observed on the invoices or the delivery statuses.
5. **No other channel now.** WhatsApp is not retained: it needs data and WhatsApp on the phone, which
   field users on a poor network often lack, Meta's approval of the template, and a second third
   party. Its cost is recorded for a later decision.
6. **What each code, notice and stop code costs, as the pages state.**

   | Channel | A message | A resent code | A failed message | Not stated |
   |---|---|---|---|---|
   | SMS (Twilio) | $0.4925 per segment | Another message, at $0.4925 per segment (per-segment billing) | $0.001 if it ends "Failed" | A carrier fee for Côte d'Ivoire; the cost of an "Undelivered" message |
   | E-mail (Resend Pro) | Within 50,000 a month for $20; $0.90 per extra 1,000 | Another e-mail against the quota | Counted as sent (no refund once sent) | Whether a bounced or suppressed e-mail counts |

   A notice and a stop code are written to fit one SMS segment where the French text allows, since a
   message beyond it is charged per segment; their wording is the contract's.
7. **Spending this route implies, for the maintainer's approval on the pull request.**
   - SMS: $0.4925 per segment. Codes are held under ADR-ARCH-055's daily spending ceiling (200 codes,
     so at most $98.50 a day for codes, plus any carrier fee Twilio does not state), with alerts at 50 %
     and 100 %. Notices and stop codes are outside it (ADR-ARCH-055 decision 6): a notice is triggered only by a signed-in change or
     recovery, or by a request the operator takes, and a stop code only by the holder of such a notice,
     at most about 170 per notice over the delay (ADR-ARCH-049 decision 3); an alert is raised above a
     daily volume, without stopping them.
   - Why not Orange, whose price would buy far more codes within the same ceiling (7.26 FCFA per SMS in
     its 1,000 and 10,000 bundles): "Orange Specific Terms for SMS Cote d'Ivoire" ("Version number: 2.0
     Date: Jan 25th 2016"), §4.9 "Usage and content of SMS", state: "You shall only make a personal
     and private use of the acquired SMS." and "In addition, the Client cannot send more than three
     (03) SMS messages per day." (https://developer.orange.com/apis/sms-ci/terms-and-conditions, read
     on 2026-11-27 from the page's own HTML; the same address answered HTTP 403 on 2026-11-28). See
     Alternatives.
   - The sender ID registration (no fee is stated on the pages read).
   - E-mail: Resend Pro, $20 a month for production, and $0.90 per extra 1,000 beyond 50,000; the
     ceiling of 1,000 codes a day keeps codes within the plan (ADR-ARCH-055).
   - Cloudflare Turnstile: its free plan, no spending (ADR-ARCH-055).

## Consequences
- The maintainer registers the sender ID with Twilio (about 3 weeks), verifies the sending domain with
  Resend, and creates both accounts; their credentials go to the identity host's Doppler config
  (ADR-ARCH-042) and to Bitwarden.
- E-mail addresses and the e-mail metadata are stored by Resend in the United States.
- Twilio and the Ivorian operators carry every SMS. An operator that filters the sender drops codes
  and notices alike; the operator console shows the recorded delivery statuses.
- Whether a code reached a person cannot be known from Côte d'Ivoire's receipts; a person who does not
  receive a code asks for a new one after the interval of ADR-ARCH-046.
- The unstated costs (a carrier fee, an "Undelivered" message, a bounced e-mail) are observed on the
  first invoices; a change of route or plan is a new decision.

## Alternatives considered
- **Orange "SMS Côte d'Ivoire" as the primary route.** The maintainer's direction of 2026-11-27:
  retain it if its official pages confirm delivery to subscribers of every operator in Côte d'Ivoire, a
  sender name we can register, and a subscription open to us; otherwise retain Twilio and record why.
  Condition by condition:
  - delivery to every operator: stated ("Available Only in and to Cote d'Ivoire any operator"), and
    contradicted for one operator by Orange's own FAQ: "We are aware that there are some delivery
    issues towards MTN users". Not confirmed;
  - a sender name we can register: confirmed, after Orange's approval ("within 5 working days (for MTN
    15 working days)");
  - a subscription open to us: not met. The bundles are offered to "any clients of Orange", but the
    terms that bind the subscription allow only "a personal and private use of the acquired SMS",
    "not more than three (03) SMS messages per day", and no SMS "between ten(10) PM and six (06)AM,
    unless it is a Receiver's demand". A platform sending codes and notices to many people, at any
    hour, cannot subscribe under these terms.

  Two of the three conditions are not met, so Twilio is retained.
- **Bird.** Cheaper per segment ($0.34), but its pages read state no failed-message cost and nothing on
  delivery receipts, and its "No registration required" contradicts Twilio's statement that sender ID
  registration is required in Côte d'Ivoire. Kept as the alternative to check before a change.
- **Twilio Verify.** Rejected: it generates, checks and expires the code itself, with a 10-minute
  validity it is "unable to change" and its own attempt limits, which would take BP002-R1 and BP002-R3
  out of the platform's hands; and SMS attempts are "always charged".
- **WhatsApp.** See decision 5.
- **Amazon SES for e-mail.** Cheaper per e-mail; its pages read do not state whether a bounce is
  charged either. Kept as the alternative.
- **Resend's Free plan in production.** Rejected: its 100 e-mails a day could be spent by code
  requests, and would then stop notices and stop codes (BP002-R11, BP002-R15).

## References
Read on 2026-11-28: Twilio, https://www.twilio.com/docs/messaging/guides/sms-geo-permissions
(decision 2).

Read on 2026-11-27:
- Twilio: https://www.twilio.com/en-us/sms/pricing/ci;
  https://assets.cdn.prod.twilio.com/pricing-csv/SMSPricing.csv;
  https://www.twilio.com/en-us/sms/pricing/us;
  https://www.twilio.com/en-us/changelog/twilio-will-bill-for-failed-messages-starting-september-30--2024;
  https://www.twilio.com/en-us/guidelines/ci/sms; https://www.twilio.com/en-us/verify/pricing;
  https://www.twilio.com/docs/verify/api/service; https://www.twilio.com/docs/api/errors/60202;
  https://www.twilio.com/docs/api/errors/60203; https://www.twilio.com/en-us/whatsapp/pricing.
  Unreadable: https://help.twilio.com/articles/27502337507483-Failed-Message-Processing-Fees;
  https://support.twilio.com/hc/en-us/articles/223181728.
- Orange: https://developer.orange.com/apis/sms-ci/overview;
  https://developer.orange.com/apis/sms-ci/pricing; https://developer.orange.com/apis/sms-ci/faq;
  https://developer.orange.com/apis/sms-ci/terms-and-conditions;
  https://developer.orange.com/apis/sms/getting-started;
  https://developer.orange.com/orange-apis-general-terms.
- Bird: https://bird.com/pricing/sms/ci. Meta:
  https://developers.facebook.com/docs/whatsapp/pricing;
  https://developers.facebook.com/documentation/business-messaging/whatsapp/templates/authentication-templates/authentication-templates.md.
  Vonage (https://www.vonage.com/communications-apis/sms/pricing/) and Africa's Talking
  (https://africastalking.com/pricing): HTTP 403.
- Resend: https://resend.com/pricing;
  https://resend.com/docs/knowledge-base/account-quotas-and-limits.md;
  https://resend.com/docs/dashboard/domains/regions.md; https://resend.com/legal/terms-of-service;
  https://resend.com/legal/acceptable-use. Amazon SES: https://aws.amazon.com/ses/pricing/.

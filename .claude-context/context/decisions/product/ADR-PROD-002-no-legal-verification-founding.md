# ADR-PROD-002 — Founding creation does not verify the requester-organization legal link

Status: accepted
Date: 2026-07-15
Refs: ADR-ARCH-012 (person / account / organizations / ownership model).
Revealed by: BP-001 (Créer son espace entreprise), task TSK-2 and rule RULE-3.

## Context

In BP-001, a director declares the organization they want to represent (TSK-2) and
becomes its Initial Owner (TSK-3). Nothing in the process proves that the person is
actually the legal representative of the declared organization: the requester
DECLARES an organization; the process does not verify the legal or administrative
tie between them.

This is a product-scope choice and must be recorded, so that it is a deliberate
position rather than a silent gap discovered later.

## Decision

BP-001 does NOT verify the legal or administrative link between the requester and the
declared organization. The organization context is taken as declared.

Founding creation establishes an ownership relationship based on the requester's
declaration of the organization. It does not establish legal proof of representation.
These are two different levels: the platform ownership relation is fully created; its
correspondence to an external legal legitimacy is simply not verified within BP-001.

Requiring the process to prove legal representation would turn founding creation into
a business-verification process — a different problem, with different actors
(registries, legal authorities), a different intent, and a different failure model.
That is out of BP-001's boundary.

## Consequences

- Founding creation stays self-service and immediate: no external verification step,
  no waiting on a third party, no human gate (consistent with BP-001's actor model —
  no platform-side human in the boundary).
- The organization representation is only as trustworthy as the declaration. The
  product accepts this for the founding path. If a later product need requires proving
  the link (fraud control, regulated features, billing identity), it will be a
  SEPARATE process with its own trigger and rules — never retrofitted into BP-001.
- A declared organization that is later found illegitimate is a matter for other
  processes (dispute, suspension, ownership challenge), not for founding creation.

## Alternatives considered

- Verify the legal link at creation (e.g. against a company registry). Rejected for
  BP-001: it couples founding onboarding to an external authority, breaks the
  self-service immediacy that is the point of the process, and answers a different
  business question. It may become its own process if a real need appears.
- Defer creation until manual review. Rejected: reintroduces a platform-side human
  actor that BP-001 explicitly excluded at step 0; turns onboarding into sales-led
  qualification, which is a separate, out-of-frontier concern.

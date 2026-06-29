# ADR-PROD-001 — Market entry through Safety (not Quality)

Status: accepted
Date: 2026-06-28

## Context

The product (vertical construction-operations SaaS, Ivory Coast market) must pick a
first vertical slice. Two natural entry points exist: Safety (constats / incidents)
and Quality (non-conformities). The slice must be both the strongest technical proof
(offline + corrective-action lifecycle) and the fastest commercial entry.

## Decision

Increment 1 enters the market through Safety: a field Safety constat (incident /
accident / dangerous situation) drives the corrective-action lifecycle. Quality
(non-conformity) follows in increment 2, reusing the same CorrectiveActions contract.

## Consequences

- Easier: aligns with the strongest regulatory and commercial pull in the CI market;
  Safety constat already carries a full lifecycle, so the technical skeleton is proven
  on the highest-value path.
- The technical skeleton (offline capture -> lifecycle -> sync -> dashboard) is
  identical for Quality, so increment 2 is cheap; resequencing stays possible if the
  field partner signals otherwise.
- Product positioning leads with Safety/QHSE, not Quality.

## Rationale (Ivorian market, verified 2026-06)

- Regulatory pull is on the safety/insurance side: a 2026 bill makes site insurance
  mandatory from site opening for risk prevention; law 2024-239 mandates an SPS
  coordinator, a general coordination plan, and per-company safety plans (PPSPS),
  with penal liability and possible administrative site closure on non-compliance.
- Quantified pain: falls/crushing/electrocution account for ~75% of fatal BTP
  accidents in CI; a safe site is ~30% more productive; large/public clients now
  require strict safety guarantees before awarding contracts.
- Current tooling to replace is framed in safety terms: QHSE roles are defined around
  observation sheets, incident reports, safety registers, action plans, dashboards,
  and Excel/Word/PowerPoint reporting — exactly the paper/Excel loop the product
  digitizes, expressed as Safety, not Quality.

## Alternatives considered

- Quality (non-conformity) first: rejected as the entry point — weaker immediate
  regulatory/commercial pull than Safety in the CI market, though technically
  equivalent. Becomes increment 2.

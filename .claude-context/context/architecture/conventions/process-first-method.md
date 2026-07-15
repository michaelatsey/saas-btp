# Process-First Design Method (project-wide standard)

Status: accepted (2026-07-15). Reusable across all features and projects.
Principle: a feature is a business process first, software second. Never start
from infrastructure, framework, or code. The conceptual model is validated by a
human before any technology name appears.

---

## Why

Features designed infrastructure-first drift into incoherence within months:
the data model dictates the domain instead of the reverse. Modelling the business
process first — actor, boundaries, tasks, rules, states — produces a design that
survives a change of database, auth provider, or framework. Infrastructure becomes
an implementation detail chosen against a settled model, not a constraint that
shapes it.

---

## The method — 11 steps, in order

**0. Actor + Trigger**
   - Who initiates the process?
   - What is their intention (the business outcome they want)?
   - What concrete user/system action starts it?
   A process does not exist in the abstract; it exists because an actor seeks a
   result. Name the actor and the trigger before naming the process. This prevents
   inventing technical pseudo-processes ("create user", "create JWT") in place of
   the real business process ("a director wants to start using the platform").

**1. Process Boundary**
   - Where does the process start?
   - Where does it end?

**2. Business Actors**
   - Human actors, external systems, internal boundaries.

**3. Process Tasks**
   - Break the process into independent tasks.

**4. Task Contract** — for each task:
   - Trigger · Owner · Preconditions · Inputs · Rules · Outputs · Errors · Events produced.
   A precondition is a state that must already hold for the task to be legitimate
   (e.g. "tenant exists", "invitation is pending and live"). It is neither an input
   nor a rule; omitting it lets impossible flows be modelled.

**5. Business Rules**
   - Invariants, constraints, policies.

**6. Architectural Decision Discovery**
   Identify every decision that warrants an ADR, and classify each:
   - **Category A — conceptual** (independent of infrastructure): aggregate
     boundary, tenant isolation model, identity ownership, event naming. These are
     stable across reasonable technology choices.
     -> DECIDED AND WRITTEN NOW. A conceptual decision taken early is stable, not
        debt. Leaving it only in a conversation is the debt.
   - **Category B — technical** (dependent on infrastructure constraints): choice
     of auth provider, index strategy, migration tooling, ORM vs raw driver.
     -> IDENTIFIED now, DECIDED at step 10, WRITTEN at step 11.

**7. Process State Model**
   - States, transitions, failure paths.

**8. Conceptual Mermaid Diagram**
   - The business process only. No framework, no database, no infrastructure.
   - Not a class diagram, not an entity model, not API endpoints. A process
     diagram is Actor -> Action -> Decision -> Outcome, never a list of domain
     objects (User, Tenant, Membership).

**9. Validation Gate**
   - Human approval. No implementation before this passes.

**10. Implementation Design**
   - Only now: API, database, infrastructure, code. Confront the validated model
     with the real constraints and resolve Category B decisions here.

**11. ADR Finalization**
   - Write the Category B ADRs decided at step 10. (Category A ADRs were already
     written at step 6.)

---

## Non-negotiables

- Infrastructure names (Supabase, PostgreSQL, .NET, React, JWT, Npgsql, EF Core)
  appear only from step 10 onward. If one surfaces earlier, the model is not yet
  conceptual.
- One problem at a time. An ADR is written when its problem is settled — never a
  speculative ADR per open question.
- The business model (grain, authorization edges, invariants) is settled input.
  Infrastructure constraints may expose a feasibility limit and thereby affect
  implementation choices — but they cannot redefine the business intent or the
  domain invariants. The infra constrains the HOW, never the WHAT.

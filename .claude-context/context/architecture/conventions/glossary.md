# Glossary — Analysis Vocabulary (project-wide standard)

Status: accepted (2026-07-15). Vocabulary only — NOT a file-naming convention.
Principle: a short, stable set of terms used across specs, ADRs, sessions, Mermaid
diagrams and discussions. Aligned with DDD / BPMN vocabulary.

Scope note: these acronyms are a CONCEPTUAL language, not a documentary structure.
File-naming families (BP-*, SUB-*, EVT-*, RULE-*, ...) are deliberately NOT created
yet. A naming convention is introduced only once a real, repeated need appears after
a first complete process has been modelled — never in anticipation.

---

## Terms

| Acronym | Term | Definition | Answers |
|---------|------|-----------|---------|
| BP | Business Process | A set of activities turning a business intent into an observable business outcome. Has a start and an end. | What complete business goal do we pursue? |
| ACT | Actor | A person, organization or system that triggers, participates in, or receives a result from the process. | Who acts or is concerned? |
| INT | Intent | The actor's business motivation, independent of any technical solution. | Why does the actor want this? |
| TRG | Trigger | The event or action that starts the process. | Why now? What launches it? |
| OUT | Outcome | The final value the actor obtains when the process succeeds. | What result proves the need is met? |
| BDY | Boundary | The explicit limit of the process: what is included and excluded. | Where does the process start and end? |
| TSK | Task | A necessary step, done by an actor or system, that advances toward the outcome. | What action moves the process forward? |
| SUB | Sub-process | A set of tasks complex enough to be detailed separately, but belonging to a parent process. | Does this part deserve its own flow? |
| CMD | Command | An explicit request to perform an action. Usually imperative. | What action is requested? |
| EVT | Event | An accomplished, immutable business fact resulting from an action. | What has happened? |
| RULE | Business Rule | A condition or policy that guides business behaviour. | What rule must hold? |
| INV | Invariant | A rule that must ALWAYS be true over a business object's whole lifetime. | What must never be false? |
| STATE | State | A possible situation of a process or object, with defined transitions. | What situation are we in? |
| CTX | Context (DDD Bounded Context) | A zone of business responsibility with its own model and rules. | Which domain owns this capability? |
| CAP | Capability | What the system can provide from a business standpoint. | What business capability does the product offer? |
| VO | Value Object | A business object defined by its value rather than its identity. | Is this data a value? |
| AGG | Aggregate | A cluster of business objects governed by a root that guarantees invariants. | Which object protects its own rules? |
| POL | Policy | A decision rule that can evolve independently of the model. | Which decision applies given the context? |

---

## Design reading order

When modelling a feature, terms are reached in this order:

| Level | Elements |
|-------|----------|
| Business vision | CAP |
| User need | ACT + INT |
| Process | BP |
| Delimitation | BDY + TRG + OUT |
| Organization | TSK / SUB |
| Business model | CTX / AGG / VO |
| Behaviour | RULE / INV / STATE |
| Communication | CMD / EVT |
| Decisions | ADR |

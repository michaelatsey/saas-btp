# AI OS — Vision (North Star)

> This file holds the long-term cap. Every step on the project is situated against it.
> It does not change often. When it does, record an ADR-ORG.

---

## 1. Target

An AI Operating System for a one-person augmented company: a set of specialized
AI agents that collaborate — through Notion, Claude Code, n8n, Git and Figma — to
take a feature from idea to deployment with minimal manual intervention.

Target agent roster (the destination, NOT a day-1 build list):

```
Product Owner · Business Analyst · UX Researcher · UX Designer · UI Designer ·
Design System Manager · Solution Architect · Backend Engineer · Database Engineer ·
Frontend Engineer · QA / Test Engineer · Security Reviewer · DevOps Engineer ·
Technical Writer
```

---

## 2. Double finality

This system is built for two outcomes of equal weight:

1. **Deliver the SaaS BTP** — the construction operations product.
2. **Build sellable expertise** — the documented, proven method of architecting and
   operating an AI-augmented company, to be offered as consulting/training to firms
   that want to augment themselves.

Consequence: the SaaS BTP is the practice ground; the AI OS method is the real
career deliverable. Both are first-class. Neither is sacrificed for the other.

---

## 3. Method — non-negotiable principles

- **Progressive, never big-bang.** The 15-agent org is reached by accretion, not by
  wiring it all up front. Each agent and each tool connection is born from a real,
  felt need on the actual project.
- **Cas pratique only.** No agent, no automation, no tool integration is built in the
  void. It must serve a concrete BTP task the moment it is created.
- **One new connection at a time.** Each increment adds exactly one debuggable link
  (Notion->Claude, then Figma, then n8n...). This is how the agencement is learned.
- **Extract roles when they hurt, don't pre-split.** An agent splits into two only
  when one cerveau visibly overloads it — same logic as "extract patterns after real
  usage" applied to the org chart.
- **Automate only what was done by hand 5 times.** n8n encodes a workflow already
  proven manually — never a supposition.
- **Document as you go.** Every agent, connection and automation is captured (why +
  how) in `ai-os-method.md` and `ADR-ORG-*`. The capture IS the sellable asset.

---

## 4. Anti-patterns (explicitly rejected)

- Wiring 15 agents + n8n before a single feature ships.
- Treating a 1-person team like a 15-person org (handoffs where a 30-second decision
  suffices = ceremony, not leverage).
- Copying a mature company's final org chart instead of growing one's own.
- Learning Notion + n8n + Figma + agent orchestration simultaneously in the blind.

---

## 5. Definition of done (for the AI OS itself)

The AI OS is "achieved" when:

- a new BTP feature flows through a near-complete pipeline (idea -> spec -> design ->
  code -> test -> deploy) with the founder mostly validating, not executing; AND
- the method behind it is documented well enough to teach to another company.

Until both hold, the AI OS is in construction — and that is the expected state.

# Decisions Index (ADRs)

> Every non-trivial decision is recorded as an ADR. An ADR is short, immutable once
> accepted, and superseded (never edited) when reversed.

## Domains

| Prefix | Domain | Folder |
|--------|--------|--------|
| `ADR-PROD` | Product / business scope | `decisions/product/` |
| `ADR-ARCH` | Technical architecture | `decisions/architecture/` |
| `ADR-ORG`  | AI OS / organization | `decisions/ai-os/` |

## Numbering

`ADR-<PREFIX>-NNN-short-kebab-title.md` — NNN is a zero-padded sequence per domain
(e.g. `ADR-ORG-001-progressive-not-bigbang.md`).

### Removed architecture ADRs

ADR-ARCH-001 to ADR-ARCH-016 were removed with the API reset; their text stays reachable
under the tag `archive/pre-reset`. New architecture ADRs continue at ADR-ARCH-017: a number
is never reused, so a citation of a removed ADR can never resolve to a new one.

## Template

```
# ADR-XXX-NNN — Title

Status: proposed | accepted | superseded by ADR-...
Date: YYYY-MM-DD

## Context
What forces this decision. The problem, the constraints.

## Decision
What we chose, stated plainly.

## Consequences
What becomes easier, what becomes harder, what is now forbidden.

## Alternatives considered
Options rejected and why.
```

## Index

| ADR | Title | Status |
|-----|-------|--------|
| ADR-PROD-001 | Market entry through Safety (not Quality) | accepted |
| ADR-PROD-002 | Founding creation (BP-001) does not verify the legal link between the requester and the declared organization; organization is taken as declared | accepted |
| ADR-ARCH-017 | Solution layout for the .NET side of the monorepo | superseded by ADR-ARCH-020 |
| ADR-ARCH-018 | HTTP API surface: Minimal APIs, major version in the URL, committed OpenAPI | accepted |
| ADR-ARCH-019 | Schema migrations with DbUp, run before the application | accepted |
| ADR-ARCH-020 | Solution layout for the .NET side of the monorepo: hosts, modules and folder casing | accepted |
| ADR-ARCH-021 | Test project conventions: xUnit v3, native assertions, XML documentation | accepted |
| ADR-ARCH-022 | Container images built in CI from multi-stage Dockerfiles for linux/amd64; base images pinned by digest; a digest bump that moves the SDK to another feature band moves the `global.json` pin in the same pull request | accepted |
| ADR-ARCH-023 | Image registry, tags and promotion: private GHCR, one immutable `sha-<full SHA>` tag per commit on `main`, deployment and promotion by digest, no environment tags | superseded by ADR-ARCH-029 |
| ADR-ARCH-024 | The migration runner runs in a GitHub Actions job before the API, a Coolify Docker Image application, is deployed by digest; expand/contract mandatory | accepted |
| ADR-ARCH-025 | Runtime secrets in Doppler, one project per role and a config per environment; the API through Coolify's integration, the runner through a GitHub Environment; no CLI in any image | accepted |
| ADR-ARCH-026 | The staging database is a separate Supabase project, `saas-btp-staging`, sharing no credential with production and never receiving production data; Free-plan pausing accepted | accepted |
| ADR-ARCH-027 | The migration runner connects as `saasbtp_migrator` through Supavisor in session mode, owns the schema, forces RLS on every application table; role `statement_timeout` 110 s, runner command timeout 120 s | accepted |
| ADR-ARCH-028 | The API connects as `saasbtp_api` through Supavisor in session mode, owns nothing and is subject to RLS; `temporary` and `usage` on `public` revoked from `PUBLIC`, `connect` kept | accepted |
| ADR-ARCH-029 | Image registry, tags, promotion and attestation: private GHCR, one `sha-<full SHA>` tag per commit on `main` created only on a digest attested by the run that pushed it, attestations in GitHub's attestations API only, verified for `publish-api-images.yml` on `refs/heads/main`; deployment and promotion by digest; supersedes ADR-ARCH-023 | superseded by ADR-ARCH-030 |
| ADR-ARCH-030 | Image registry, tags, promotion and attestation as in ADR-ARCH-029, with publication and staging deployment chained after a successful `CI API` run on a push to `main` (`workflow_run`, reusable workflows); every deployment verifies the attestation of both digests before the runner; automatic deployments never move staging to an older commit, manual ones (rollback) may; supersedes ADR-ARCH-029 | accepted |
| ADR-ARCH-031 | The Access context owns profile, organization, workspace, ownership, membership and workspace access in one module and schema; the person is external; an indivisible business step lives in one module, never a transaction across modules; the workspace is the tenant (`workspace_id`), the organization is not an isolation key; site records carry their `workspace_id` | accepted |
| ADR-ARCH-032 | Communication and authorization across modules: integration events through a transactional outbox, at least once, idempotent consumers, outbox introduced with its first consumer; one published workspace check `access.may_act_in(workspace_id)` called by every policy on workspace data; the subject of each transaction set by the API in `app.user_id` with a transaction-local `set_config`, no grant on `auth`, policies fail closed; a relation is created only when the subject already holds the relation its process requires on the target (for a founding, ownership of the root record); exactly one current owner per root record enforced by constraints deferred to commit; the workspace check marks participation, not an action permission; `app.user_id` trusted only because the API alone sets it from a validated token, no SQL concatenation; finer process rules stay in the domain, no `security definer`; hidden rows answered as not found, identifiers in use as naming.md states; answers ADR-ARCH-028 §6 | accepted; §2 call path and §4 creation order superseded by ADR-ARCH-033 |
| ADR-ARCH-033 | Module persistence: one database shared today, every module extractable; no foreign key across schemas, Access included, references by identifier; each module's policies call its own `<module>.may_act_in`, delegating to `access.may_act_in`; one DbUp journal; each module chooses its storage, EF Core at run time by default with `MappingMatchesMigratedSchema` against a database migrated by the runner; one explicit transaction per unit of work, EF automatic transactions disabled, the subject set first by a transaction interceptor, commands without a transaction refused; rows inserted only once the subject may see them (`RETURNING`), replacing ADR-ARCH-032's order (profile, ownership, organization, membership, access, workspace), a workspace visible only through an access of its own organization; one current owner by deferred composite foreign keys | accepted |
| ADR-ORG-001 | The repository stays public: Claude.ai needs read access; secret scanning and push protection become a priority; README and licence statement to correct | accepted |
| ADR-ORG-002 | GitHub flow: short-lived branches → PR → `main` (default), squash, branch deleted after merge; `dev` retired after a final promotion | accepted |
| ADR-ORG-003 | Claude Code may commit, push and open PRs, each gated by an `ask` permission rule; merge stays human; GitHub through `gh` only; supersedes "Claude never runs git" (provisional, revisit after pilot #75) | superseded by ADR-ORG-004 |
| ADR-ORG-004 | Autonomous delivery with a second reviewer from another provider: agreement replaces approval within a two-cycle cap; commit, push and PR creation run without a prompt; merge stays human; auto-merge not used; run-01 agent blueprint retired for the `solution-design` plugin | accepted |
| ADR-ORG-005 | Merge commands (`gh pr merge`, the REST merge endpoint and the GraphQL merge mutations through `gh api`) denied in `.claude/settings.json` as a second layer next to the auto-mode classifier, not a boundary; separate GitHub identity for Claude Code deferred to after the BP-001 pilot (#138) | accepted |



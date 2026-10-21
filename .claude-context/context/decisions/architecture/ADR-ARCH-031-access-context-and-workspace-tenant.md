# ADR-ARCH-031 — The Access context owns the access domain, an indivisible step lives in one module, and the workspace is the tenant

Status: accepted
Date: 2026-10-21
Refs: issue #173; parent #125; BP-001 (`docs/business-processes/BP-001-found-company/`);
`docs/domain/access.md`; ADR-ARCH-020; ADR-ARCH-027; ADR-ARCH-028.

## Context
BP-001 creates, in one indivisible step, a person's profile, an organization, its first
workspace, the ownership, the membership and the workspace access (`process.md`, TSK-2). The
access domain defines workspace access as the only authorization relation, and states that a
workspace's team does not necessarily see the data of the organization's other workspaces
(`docs/domain/access.md`, Concepts: Workspace, Workspace access; INV-ACCESS-09).

The module rules leave no room for a step split across modules: no project of one module
references a project of another (ADR-ARCH-020), contexts communicate through integration events
(`architecture.md` §2), and each bounded context has its own schema (`sql.md`, Casing & naming).

`architecture.md` still described Site as the organisation root and Access as "authentication,
tenancy, RBAC" without naming the tenant, and `sql.md` keyed isolation on a `tenant_id` that no
concept defined.

## Decision
1. The Access bounded context owns the profile, the organization, the workspace, the ownership,
   the organization membership and the workspace access. It is one module,
   `apps/api/src/modules/Access/`, with one schema, `access`.
2. The person is external to the product: the identity provider establishes who a person is, and
   authentication is not a bounded context of the product. Access knows a person only through
   their profile.
3. An indivisible business step lives in one module. When a step needs facts owned by two
   contexts in one transaction, the context boundary is revisited by a new ADR; no transaction
   spans two modules. What another context derives from the step follows the communication rule
   of `architecture.md` §2.
4. The workspace is the tenant. Every record of another module that belongs to a workspace
   carries that workspace's identifier, `workspace_id`, and its data is isolated by workspace.
   How a request is admitted to a workspace (the check in the API, the row level security
   predicate, how the user reaches the database) is decided by #175 and #176. The organization
   is not an isolation key: the workspaces of one organization do not share data unless a process
   says so.
5. Site records carry the `workspace_id` of the workspace they belong to; the Site context owns
   sites and what is scoped to a site, and is not the root of the organization. Whether a site can
   ever change workspace is a business rule of BP-006 (#167); until it says otherwise, it cannot.
6. No role or permission is modelled until a process produces one.

## Consequences
- BP-001's founding step is one transaction inside Access, with no cross-module coordination.
- Every module that stores workspace data depends on Access for scoping, never the reverse.
  How such a module applies "may act in this workspace" without referencing Access is decided
  with communication and authorization across modules (#175), and whether its tables may
  reference the `access` schema with persistence (#176).
- `architecture.md`, `sql.md` and the Safety constat specification name the workspace instead of
  an undefined tenant.
- An organization-wide capability (billing, settings, member management) is produced by the
  process that needs it, as a relation of Access, never by reading every workspace.
- Splitting Access later (for example an Organization context) requires an ADR that also says
  where BP-001's step goes.

## Alternatives considered
- A separate Organization context owning organization, workspace and ownership, with Access
  keeping membership and workspace access: BP-001's step would span two modules, which needs a
  shared transaction (against the module rules) or eventual consistency (against TSK-2).
- The organization as tenant: simplest isolation, but every workspace's team would see the data
  of the others, against the access domain.
- The site as tenant: founding produces no site, and workspace-level data (its people, its
  sites) would have no isolation key.
- Keeping a generic `tenant_id`: the name hides which concept isolates the data.

## References
- `docs/business-processes/BP-001-found-company/process.md` (TSK-2), `docs/domain/access.md`.
- ADR-ARCH-020 (module rules), ADR-ARCH-027 and ADR-ARCH-028 (row level security applies to the
  API).

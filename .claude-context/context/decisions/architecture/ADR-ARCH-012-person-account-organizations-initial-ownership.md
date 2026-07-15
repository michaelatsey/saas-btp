# ADR-ARCH-012 — Person, Account, Organizations and Initial Ownership Model

Status: accepted
Date: 2026-07-15
Refs: ADR-ARCH-008 (identity model: profiles / tenants / memberships — see Divergence),
ADR-ARCH-009 (relationship-based authorization: two independent edges).
Revealed by: BP-001 (Créer son espace entreprise), first process to require this model.

## Context

BP-001 modelled a company director, external to the platform, creating their first
company workspace self-service. Modelling it surfaced a concept the existing ADRs do
not carry: how a person becomes the OWNER of an organization they create.

ADR-ARCH-008 split one conflated "identity" into three homes — authentication
(`auth.users`), business identity (`access.profiles`), and authorization
(`access.memberships`). That model is sound and stands. But it frames organizational
attachment exclusively as MEMBERSHIP (a person is granted access to an organization),
and it assumed onboarding always flows through an invitation
(`InviteMemberCommand`). It has no concept of a person who CREATES an organization
and thereby OWNS it — a founder is not someone who was granted access; there is no
one above them to grant it.

This decision records the product's structural model, of which BP-001 is only the
first exercise. It will govern later processes (adding an organization, transferring
ownership, joining an existing organization), not just this one.

## Decision

The product's structural model is:

    Physical person
      -> User account            (a person is represented through an account identity
                                   used to interact with the platform; the exact
                                   cardinality person <-> account is not decided here)
        -> N organizations       (a person relates to several organizations over time)
          -> one company workspace per organization

The product distinguishes **Ownership** and **Membership** as separate organizational
relationships, which must never be conflated:

- **Ownership** — the relation "this person owns this organization". Established when
  the organization is created. It is not identity, and it is not access.
- **Membership** — the relation "this person may act within this organization"
  (ADR-ARCH-008). Access, evaluated per request, revocable.

Other organizational relationships may exist in the future (delegated administrator,
legal representative, external partner, and so on). This ADR does not enumerate a
closed set; it fixes only that Ownership and Membership are distinct and never merged.

At creation, exactly one person holds the ownership relation: the **Initial Owner** —
the person who created the organization. "Initial" is deliberate: ownership is a
relation that MAY evolve through other processes (transfer, change of owner); this ADR
governs only its establishment at creation, not its lifetime.

An organization owns exactly one active company workspace. Creating the organization
and establishing its initial ownership are a single indivisible business unit: a
workspace never exists without an owner (business invariants, BP-001 registry).

## Consequences

- The product now has a founding entry path — a person creating their first
  organization — that is autonomous, NOT a special case of invitation. A founder is
  not an invited member: an invited member joins an existing organization; a founder
  creates the first one they own. These are two different business intents and belong
  to two different processes.
- Ownership and Membership are separately modelled. A person may be the Initial Owner
  of one organization and merely a member of another; the same person may own several
  organizations over time. Neither relation implies the other.
- The concept vocabulary is fixed for downstream code and ADRs: **Ownership** (property
  relation), **Membership** (access relation), **Initial Owner** (owner assigned at
  creation). This ADR stays in business vocabulary; the technical correspondence
  Organization <-> tenant (multi-tenancy) is a HOW, decided later at implementation.
- Ownership and Membership are revoked independently: a person who loses all
  memberships in an organization remains its Initial Owner, and vice versa. Owning an
  organization does not by itself grant the ability to act within it (consistent with
  ADR-ARCH-009's independent edges).

### Divergence with ADR-ARCH-008 (identified, not resolved here)

ADR-ARCH-008 stated onboarding is an explicit application operation carried by
`InviteMemberCommand`, "which can distinguish create-a-company from join-one". BP-001
establishes instead that founding creation is an AUTONOMOUS process, with no
invitation and no inviter.

ADR-ARCH-008 is NOT wrong: it remains valid for authentication, business identity, and
membership. It simply did not fully cover the founding-creation scenario — it folded
"create a company" into the invitation mechanism for convenience, before that process
had been modelled. Whether ADR-ARCH-008 should be amended to move founding creation out
of `InviteMemberCommand` is a separate decision, taken deliberately later — not by this
ADR.

## Alternatives considered

- Treat founding creation as a variant of invitation (the ADR-ARCH-008 framing).
  Rejected: an invitation presupposes an inviter with authority over the invitee. A
  founder has no one above them; modelling their entry as a self-issued invitation is a
  fiction that would reintroduce the "guess the intent" problem ADR-ARCH-008 removed.
- Model organizational attachment with a single relation (membership only), the owner
  being "a member with an owner role". Rejected: ownership and access answer different
  questions ("whose organization is this" vs "who may act in it"); collapsing them makes
  ownership a privileged role on the access table — the same conflation ADR-ARCH-008
  broke apart for identity.

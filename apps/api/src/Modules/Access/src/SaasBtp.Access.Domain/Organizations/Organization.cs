using MicroKit.Domain.Aggregates;
using SaasBtp.Access.Domain.Identity;
using SaasBtp.Access.Domain.Organizations.Rules;
using SaasBtp.Access.Domain.Time;

namespace SaasBtp.Access.Domain.Organizations;

/// <summary>
/// The client company — an aggregate root, and the consistency boundary of the PROPERTY and
/// BELONGING axes. It holds its ownership and its memberships as internal components, and
/// guarantees both invariants itself: exactly one initial owner, and binary membership.
/// </summary>
/// <remarks>
/// Organization and <c>Workspace</c> are two aggregate roots, not one (ADR-ARCH-014): a transaction
/// boundary is not an aggregate boundary. Which relations each root CONTAINS is the separate
/// question ADR-ARCH-016 answers — an invariant spanning an organization and its relations puts
/// those relations inside the organization.
/// <para>
/// Absorbing the two facts is a STRUCTURAL decision only. The three axes stay semantically
/// independent: ownership answers "to whom does it belong", membership "who is part of the
/// collective", access "where may they act", and none is ever derived from another
/// (ADR-ARCH-013 — <c>owns → access</c> and <c>member_of → access</c> remain forbidden). This
/// aggregate carries no access edge at all: that axis lives at the Workspace grain.
/// </para>
/// <para>
/// State is encapsulated in private fields. The read surface is deliberately narrow and answers
/// questions in the model's own language — it is not an opening of the aggregate for persistence,
/// which maps the fields directly.
/// </para>
/// </remarks>
public sealed class Organization : AggregateRoot<OrganizationId>
{
    private readonly List<OrganizationMembership> _memberships = [];
    private string _displayName = null!;
    private OrganizationStatus _status = null!;
    private Ownership _ownership = null!;
    private DateTime _createdAtUtc;

    private Organization(
        OrganizationId id,
        string displayName,
        PersonId founder,
        DateTime foundedAtUtc)
        : base(id)
    {
        CheckRule(new OrganizationNameMustBeProvidedRule(displayName));
        CheckRule(new OrganizationMustHaveAnIdentifiedOwnerRule(founder));

        _displayName = displayName;
        _status = OrganizationStatus.Declared;
        _createdAtUtc = foundedAtUtc;

        // Owner and first member are established HERE, not by a later call: there is no instant at
        // which a constructed organization has no owner, so the invariant does not depend on any
        // caller remembering a sequence.
        _ownership = Ownership.Establish(founder, foundedAtUtc);
        Admit(founder, foundedAtUtc);
    }

    // Only for EF: materialisation writes the fields directly.
    private Organization()
        : base(default)
    {
    }

    /// <summary>The organization's identity-maturity status.</summary>
    public OrganizationStatus Status => _status;

    /// <summary>The UTC founding instant, as supplied by the caller.</summary>
    public DateTime CreatedAtUtc => _createdAtUtc;

    /// <summary>
    /// The property axis: to whom this organization belongs, and since when. Exactly one, always —
    /// the type admits no other shape.
    /// </summary>
    public Ownership Ownership => _ownership;

    /// <summary>
    /// The belonging axis: the organization's human collective. Read-only — a membership is
    /// established through the aggregate, which is what keeps belonging binary.
    /// </summary>
    public IReadOnlyCollection<OrganizationMembership> Memberships => _memberships.AsReadOnly();

    /// <summary>Whether a person is part of this organization's collective.</summary>
    /// <param name="person">The person to look for.</param>
    /// <returns><see langword="true"/> when the person already belongs.</returns>
    public bool IsMember(PersonId person) =>
        _memberships.Exists(membership => membership.Member == person);

    /// <summary>
    /// Founds an organization: the business act by which a founder declares their company. The
    /// founder becomes its single owner and its first member BY CONSTRUCTION — there is no
    /// intermediate state in which the organization exists without them.
    /// </summary>
    /// <remarks>
    /// Named for the act, not for the mechanism: <c>Create</c> would describe allocating an object,
    /// <c>Found</c> describes what the business does. The status is deliberately not a parameter —
    /// a founded organization is <see cref="OrganizationStatus.Declared"/> by construction, so no
    /// caller can express another one.
    /// </remarks>
    /// <param name="displayName">The organization's declared name.</param>
    /// <param name="founder">The founder — its initial owner and first member.</param>
    /// <param name="foundedAtUtc">
    /// The UTC founding instant, supplied by the caller and never by a database default or an
    /// ambient clock (ADR-ARCH-005). It stamps the organization and both facts it establishes.
    /// </param>
    /// <returns>The founded organization, with a freshly generated identity.</returns>
    /// <exception cref="ArgumentException">The instant is not UTC (a caller contract, not an invariant).</exception>
    public static Organization Found(string displayName, PersonId founder, DateTime foundedAtUtc)
    {
        DomainTime.MustBeUtc(foundedAtUtc);

        return new Organization(OrganizationId.New(), displayName, founder, foundedAtUtc);
    }

    /// <summary>
    /// Admits a person into the collective, refusing a duplicate. Private because nothing in the
    /// current model admits anyone after founding — the process that does (joining by invitation)
    /// will publish it, and will inherit this guard by using this one path.
    /// </summary>
    /// <param name="person">The person to admit.</param>
    /// <param name="joinedAtUtc">The UTC instant supplied by the caller.</param>
    private void Admit(PersonId person, DateTime joinedAtUtc)
    {
        CheckRule(new MembershipMustBeUniquePerPersonRule(
            _memberships.Select(membership => membership.Member), person));

        _memberships.Add(OrganizationMembership.Join(person, joinedAtUtc));
    }
}

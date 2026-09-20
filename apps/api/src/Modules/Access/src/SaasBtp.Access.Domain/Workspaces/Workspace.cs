using MicroKit.Domain.Aggregates;
using SaasBtp.Access.Domain.Identity;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Time;
using SaasBtp.Access.Domain.Workspaces.Rules;

namespace SaasBtp.Access.Domain.Workspaces;

/// <summary>
/// An autonomous operational space in which a company runs its activities — an aggregate root, and
/// the consistency boundary of the AUTHORIZATION axis. It holds its access edges as internal
/// components and guarantees the invariant itself: one access per person.
/// </summary>
/// <remarks>
/// An organization holds 1:N workspaces (agencies, regions, activities), so a workspace references
/// its organization BY IDENTITY and is never contained in it (ADR-ARCH-014). Creating an ADDITIONAL
/// workspace in an existing organization is a different business process, outside BP-001.
/// <para>
/// The workspace is the grain at which BP-001 authorizes (ADR-ARCH-013), which is exactly why the
/// access edges belong here and not to the organization: the boundary that owns the invariant is the
/// one the invariant is expressed at. Containing the edges is structural only — the axis stays
/// non-derivable, and nothing in this aggregate reads ownership or membership to decide access
/// (ADR-ARCH-016).
/// </para>
/// <para>
/// No uniqueness rule is modelled on the name: the FORM of workspace resolution is explicitly left
/// open by the business model, and the schema carries no such constraint either.
/// </para>
/// </remarks>
public sealed class Workspace : AggregateRoot<WorkspaceId>
{
    private readonly List<WorkspaceAccess> _accesses = [];
    private OrganizationId _organizationId;
    private string _displayName = null!;
    private DateTime _createdAtUtc;

    private Workspace(
        WorkspaceId id,
        OrganizationId organizationId,
        string displayName,
        PersonId founder,
        DateTime foundedAtUtc)
        : base(id)
    {
        CheckRule(new WorkspaceMustBelongToAnOrganizationRule(organizationId));
        CheckRule(new WorkspaceNameMustBeProvidedRule(displayName));
        CheckRule(new WorkspaceAccessMustIdentifyAPersonRule(founder));

        _organizationId = organizationId;
        _displayName = displayName;
        _createdAtUtc = foundedAtUtc;

        // The founder's access is established HERE: there is no instant at which a founded workspace
        // exists with nobody able to act in it.
        Grant(founder, foundedAtUtc);
    }

    // Only for EF: materialisation writes the fields directly.
    private Workspace()
        : base(default)
    {
    }

    /// <summary>The organization this workspace belongs to, referenced by identity.</summary>
    public OrganizationId OrganizationId => _organizationId;

    /// <summary>The UTC creation instant, as supplied by the caller.</summary>
    public DateTime CreatedAtUtc => _createdAtUtc;

    /// <summary>
    /// The authorization axis: the access edges this workspace holds. Read-only — an edge is granted
    /// through the aggregate, which is what keeps "can act here" binary.
    /// </summary>
    public IReadOnlyCollection<WorkspaceAccess> Accesses => _accesses.AsReadOnly();

    /// <summary>Whether a person may act in this workspace's scope.</summary>
    /// <remarks>
    /// A read of the authorization axis, never a computation over another one: this answers from the
    /// edges the workspace holds, and consults neither ownership nor membership (ADR-ARCH-013).
    /// </remarks>
    /// <param name="person">The person to look for.</param>
    /// <returns><see langword="true"/> when the person holds an access edge here.</returns>
    public bool GrantsAccessTo(PersonId person) =>
        _accesses.Exists(access => access.Person == person);

    /// <summary>
    /// Founds a workspace within an organization. The founder is authorized in it BY CONSTRUCTION —
    /// there is no intermediate state in which the workspace exists without its access edge.
    /// </summary>
    /// <remarks>
    /// Named for the act, not for the mechanism. This is BP-001's FIRST workspace; a later workspace
    /// in an existing organization is a different business process, which will state its own rule
    /// for who is authorized in it.
    /// </remarks>
    /// <param name="organizationId">The organization the workspace belongs to.</param>
    /// <param name="displayName">The workspace's name.</param>
    /// <param name="founder">The founder — the first person authorized to act here.</param>
    /// <param name="foundedAtUtc">
    /// The UTC creation instant, supplied by the caller and never by a database default or an
    /// ambient clock (ADR-ARCH-005). It stamps the workspace and the access edge it establishes.
    /// </param>
    /// <returns>The founded workspace, with a freshly generated identity.</returns>
    /// <exception cref="ArgumentException">The instant is not UTC (a caller contract, not an invariant).</exception>
    public static Workspace Found(
        OrganizationId organizationId,
        string displayName,
        PersonId founder,
        DateTime foundedAtUtc)
    {
        DomainTime.MustBeUtc(foundedAtUtc);

        return new Workspace(WorkspaceId.New(), organizationId, displayName, founder, foundedAtUtc);
    }

    /// <summary>
    /// Grants an access edge, refusing a duplicate. Private because nothing in the current model
    /// authorizes anyone after founding — the process that does will publish it, and will inherit
    /// this guard by using this one path. Revocation is deliberately absent for the same reason:
    /// its FORM is deferred (Implementation Design §3.5), and inventing one here would decide it.
    /// </summary>
    /// <param name="person">The person to authorize.</param>
    /// <param name="grantedAtUtc">The UTC instant supplied by the caller.</param>
    private void Grant(PersonId person, DateTime grantedAtUtc)
    {
        CheckRule(new WorkspaceAccessMustBeUniquePerPersonRule(
            _accesses.Select(access => access.Person), person));

        _accesses.Add(WorkspaceAccess.Grant(person, grantedAtUtc));
    }
}

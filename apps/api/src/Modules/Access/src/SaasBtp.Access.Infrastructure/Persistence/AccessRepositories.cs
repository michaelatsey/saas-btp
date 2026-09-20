using SaasBtp.Access.Application.Features.FoundCompanySpace;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Infrastructure.Persistence;

/// <summary>
/// The persistence adapters for the Access aggregates: one per aggregate root, each implementing the
/// contract its own Domain folder declares.
/// </summary>
/// <remarks>
/// Each adapter only STAGES the aggregate in EF's change tracker — no I/O happens here, which is why
/// the Domain contracts are synchronous. The write happens once, when the handler commits the unit of
/// work (<c>EfUnitOfWork.CommitAsync</c> → a single <c>SaveChangesAsync</c> → one transaction).
/// <para>
/// They are co-located in one file deliberately: a handful of adapters of three lines each, sharing
/// one rationale. Splitting them into separate files would spread that rationale thin without adding
/// a single unit of information. The CONTRACTS are what must live apart, and they do — in the
/// Domain, beside the aggregates they serve.
/// </para>
/// </remarks>
internal sealed class EfOrganizationRepository(AccessDbContext context) : IOrganizationRepository
{
    /// <inheritdoc/>
    public void Add(Organization organization) => context.Organizations.Add(organization);
}

/// <inheritdoc cref="EfOrganizationRepository"/>
internal sealed class EfWorkspaceRepository(AccessDbContext context) : IWorkspaceRepository
{
    /// <inheritdoc/>
    public void Add(Workspace workspace) => context.Workspaces.Add(workspace);
}

// DISABLED (ADR-ARCH-016): there is no repository per relation any more, because a repository is
// the collection of an AGGREGATE and the three relations are no longer aggregates. Their Domain
// contracts are deleted; these adapters go with them once the mapping is re-derived — the relations
// are then persisted as part of the root that contains them, through the two remaining repositories.
#if ACCESS_PERSISTENCE_PENDING_REDERIVATION
/// <inheritdoc cref="EfOrganizationRepository"/>
internal sealed class EfOwnershipRepository(AccessDbContext context) : IOwnershipRepository
{
    /// <inheritdoc/>
    public void Add(Ownership ownership) => context.Ownerships.Add(ownership);
}

/// <inheritdoc cref="EfOrganizationRepository"/>
internal sealed class EfOrganizationMembershipRepository(AccessDbContext context)
    : IOrganizationMembershipRepository
{
    /// <inheritdoc/>
    public void Add(OrganizationMembership membership) => context.OrganizationMemberships.Add(membership);
}

/// <inheritdoc cref="EfOrganizationRepository"/>
internal sealed class EfWorkspaceAccessRepository(AccessDbContext context) : IWorkspaceAccessRepository
{
    /// <inheritdoc/>
    public void Add(WorkspaceAccess access) => context.WorkspaceAccesses.Add(access);
}
#endif

/// <summary>
/// Writes the identity projection. NOT a repository — <c>access.profiles</c> is infrastructure, not
/// an aggregate — which is exactly why its contract is an Application port and not a Domain one.
/// </summary>
/// <param name="context">The Access context.</param>
internal sealed class EfProfileProjection(AccessDbContext context) : IProfileProjection
{
    /// <inheritdoc/>
    public void Add(FounderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        context.Profiles.Add(new ProfileRecord(
            profile.UserId,
            // The value object guarantees the stored form is normalized (ADR-ARCH-011).
            profile.Email.Value,
            profile.FullName,
            profile.CreatedAtUtc));
    }
}

using Microsoft.EntityFrameworkCore;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Adapters.Persistence.Organizations;

/// <summary>The organizations, on the Access context; written by the unit of work's next commit.</summary>
internal sealed class OrganizationRepository(AccessDbContext context) : IOrganizationRepository
{
    public ValueTask AddAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        context.Add(organization);
        return ValueTask.CompletedTask;
    }

    /// <remarks>
    /// The organization, its current ownership through the pair (<c>id</c>,
    /// <c>current_ownership_id</c>) and that ownership's profile, joined explicitly: a select policy
    /// that later lets members see their organization (BP-004) does not make them its owner. Not
    /// tracked: nothing read here is changed.
    /// </remarks>
    public async ValueTask<Organization?> FindCurrentlyOwnedByAsync(
        OrganizationId id, UserId owner, CancellationToken cancellationToken = default) =>
        await (
            from organization in context.Set<Organization>().AsNoTracking()
            join ownership in context.Set<Ownership>()
                on new { Organization = organization.Id, Ownership = organization.CurrentOwnershipId }
                equals new { Organization = ownership.OrganizationId, Ownership = ownership.Id }
            join profile in context.Set<Profile>() on ownership.ProfileId equals profile.Id
            where organization.Id == id && profile.UserId == owner
            select organization)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
}

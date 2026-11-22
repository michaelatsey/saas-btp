using SaasBtp.Access.Domain.Organizations;

namespace SaasBtp.Access.Adapters.Persistence.Organizations;

/// <summary>The organizations, on the Access context; written by the unit of work's next commit.</summary>
internal sealed class OrganizationRepository(AccessDbContext context) : IOrganizationRepository
{
    public ValueTask AddAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        context.Add(organization);
        return ValueTask.CompletedTask;
    }
}

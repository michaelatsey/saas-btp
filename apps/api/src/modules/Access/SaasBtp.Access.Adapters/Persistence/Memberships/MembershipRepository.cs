using SaasBtp.Access.Domain.Memberships;

namespace SaasBtp.Access.Adapters.Persistence.Memberships;

/// <summary>The memberships, on the Access context; written by the unit of work's next commit.</summary>
internal sealed class MembershipRepository(AccessDbContext context) : IMembershipRepository
{
    public ValueTask AddAsync(Membership membership, CancellationToken cancellationToken = default)
    {
        context.Add(membership);
        return ValueTask.CompletedTask;
    }
}

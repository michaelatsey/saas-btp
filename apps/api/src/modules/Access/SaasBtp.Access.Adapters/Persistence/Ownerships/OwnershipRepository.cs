using SaasBtp.Access.Domain.Ownerships;

namespace SaasBtp.Access.Adapters.Persistence.Ownerships;

/// <summary>The ownerships, on the Access context; written by the unit of work's next commit.</summary>
internal sealed class OwnershipRepository(AccessDbContext context) : IOwnershipRepository
{
    public ValueTask AddAsync(Ownership ownership, CancellationToken cancellationToken = default)
    {
        context.Add(ownership);
        return ValueTask.CompletedTask;
    }
}

using SaasBtp.Access.Domain.WorkspaceAccesses;

namespace SaasBtp.Access.Adapters.Persistence.WorkspaceAccesses;

/// <summary>The workspace accesses, on the Access context; written by the unit of work's next commit.</summary>
internal sealed class WorkspaceAccessRepository(AccessDbContext context) : IWorkspaceAccessRepository
{
    public ValueTask AddAsync(WorkspaceAccess access, CancellationToken cancellationToken = default)
    {
        context.Add(access);
        return ValueTask.CompletedTask;
    }
}

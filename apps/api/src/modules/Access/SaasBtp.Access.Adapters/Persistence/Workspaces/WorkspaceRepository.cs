using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters.Persistence.Workspaces;

/// <summary>The workspaces, on the Access context; written by the unit of work's next commit.</summary>
internal sealed class WorkspaceRepository(AccessDbContext context) : IWorkspaceRepository
{
    public ValueTask AddAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        context.Add(workspace);
        return ValueTask.CompletedTask;
    }
}

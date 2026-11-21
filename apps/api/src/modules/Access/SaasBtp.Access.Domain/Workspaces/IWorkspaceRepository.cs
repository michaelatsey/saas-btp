namespace SaasBtp.Access.Domain.Workspaces;

/// <summary>The workspaces (ADR-ARCH-034 §1: a repository port of the domain).</summary>
public interface IWorkspaceRepository
{
    /// <summary>Adds a new workspace to the unit of work.</summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="cancellationToken">Cancels the addition.</param>
    /// <returns>A task completed once it is added.</returns>
    ValueTask AddAsync(Workspace workspace, CancellationToken cancellationToken = default);
}

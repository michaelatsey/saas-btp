namespace SaasBtp.Access.Domain.WorkspaceAccesses;

/// <summary>The workspace accesses (ADR-ARCH-034 §1: a repository port of the domain).</summary>
public interface IWorkspaceAccessRepository
{
    /// <summary>Adds a new workspace access to the unit of work.</summary>
    /// <param name="access">The workspace access.</param>
    /// <param name="cancellationToken">Cancels the addition.</param>
    /// <returns>A task completed once it is added.</returns>
    ValueTask AddAsync(WorkspaceAccess access, CancellationToken cancellationToken = default);
}

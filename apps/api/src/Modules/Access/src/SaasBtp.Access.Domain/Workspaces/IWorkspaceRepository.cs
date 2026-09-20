namespace SaasBtp.Access.Domain.Workspaces;

/// <summary>
/// The collection of <see cref="Workspace"/> aggregates. Domain-owned contract, Infrastructure
/// adapter.
/// </summary>
/// <remarks>
/// A separate repository from the organization's, because Workspace is a separate aggregate root
/// (ADR-ARCH-014) — one repository per aggregate root, never a shared one. The access edges have no
/// repository of their own: they are components of the workspace, reached and persisted through it
/// (ADR-ARCH-016). Narrow by design: see <see cref="Organizations.IOrganizationRepository"/>.
/// </remarks>
public interface IWorkspaceRepository
{
    /// <summary>Enrols a newly created workspace in the current unit of work.</summary>
    /// <param name="workspace">The workspace to persist.</param>
    void Add(Workspace workspace);
}

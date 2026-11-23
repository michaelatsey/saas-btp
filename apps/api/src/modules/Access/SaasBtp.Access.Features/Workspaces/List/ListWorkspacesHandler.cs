using SaasBtp.Access.Features.Ports;

namespace SaasBtp.Access.Features.Workspaces.List;

/// <summary>
/// Lists the workspaces the caller can enter (contract.md §2): those for which
/// <c>access.may_act_in</c> is true for the caller, as the queries port returns them, with no
/// condition of its own (ADR-ARCH-032 §5). It lets a client show where the caller may act, and
/// decides no access.
/// </summary>
internal sealed class ListWorkspacesHandler(IAccessQueries queries)
{
    /// <summary>The workspaces the caller can enter.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The workspaces, in the order the port returns them.</returns>
    public Task<IReadOnlyList<EnterableWorkspace>> HandleAsync(CancellationToken cancellationToken = default) =>
        queries.ListEnterableWorkspacesAsync(cancellationToken);
}

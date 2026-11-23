using SaasBtp.Access.Domain.Organizations;

namespace SaasBtp.Access.Features.Ports;

/// <summary>
/// The reads of contract.md §2 that decide nothing of their own (ADR-ARCH-032 §5): what the caller may
/// see or enter is what the policies and <c>access.may_act_in</c> let the subject of the unit of work
/// read. A port of the Features project, implemented by the Adapters (ADR-ARCH-034 decision 1); each
/// read runs in a transaction of its own, on the unit of work's connection, its subject set first
/// (ADR-ARCH-034 decision 3).
/// </summary>
internal interface IAccessQueries
{
    /// <summary>The workspaces for which <c>access.may_act_in</c> is true for the subject, with no filter and no pagination.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The workspaces, ordered by name, then identifier.</returns>
    Task<IReadOnlyList<EnterableWorkspace>> ListEnterableWorkspacesAsync(CancellationToken cancellationToken = default);

    /// <summary>The organization <paramref name="id"/> identifies, when the select policies let the subject see it.</summary>
    /// <param name="id">The organization's identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organization, or <see langword="null"/> whether it is hidden or does not exist.</returns>
    Task<Organization?> FindVisibleOrganizationAsync(OrganizationId id, CancellationToken cancellationToken = default);
}

/// <summary>A workspace the subject may enter, as the listing's SQL row reads it, its properties named after its columns.</summary>
/// <param name="Id">The workspace's identifier.</param>
/// <param name="Name">The workspace's name.</param>
/// <param name="OrganizationId">The identifier of the workspace's organization.</param>
internal sealed record EnterableWorkspace(Guid Id, string Name, Guid OrganizationId);

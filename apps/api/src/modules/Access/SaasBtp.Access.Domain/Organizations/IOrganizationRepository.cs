using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Domain.Organizations;

/// <summary>The organizations (ADR-ARCH-034 §1: a repository port of the domain).</summary>
public interface IOrganizationRepository
{
    /// <summary>Adds a new organization to the unit of work.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancels the addition.</param>
    /// <returns>A task completed once it is added.</returns>
    ValueTask AddAsync(Organization organization, CancellationToken cancellationToken = default);

    /// <summary>
    /// The organization <paramref name="id"/> identifies when <paramref name="owner"/> is its current
    /// owner: the profile of the ownership its current ownership references (ADR-ARCH-033 §6). It
    /// reads current ownership, never visibility alone (contract.md §3).
    /// </summary>
    /// <param name="id">The organization's identifier.</param>
    /// <param name="owner">The person.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organization, or <see langword="null"/>.</returns>
    ValueTask<Organization?> FindCurrentlyOwnedByAsync(OrganizationId id, UserId owner, CancellationToken cancellationToken = default);
}

namespace SaasBtp.Access.Domain.Organizations;

/// <summary>The organizations (ADR-ARCH-034 §1: a repository port of the domain).</summary>
public interface IOrganizationRepository
{
    /// <summary>Adds a new organization to the unit of work.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancels the addition.</param>
    /// <returns>A task completed once it is added.</returns>
    ValueTask AddAsync(Organization organization, CancellationToken cancellationToken = default);
}

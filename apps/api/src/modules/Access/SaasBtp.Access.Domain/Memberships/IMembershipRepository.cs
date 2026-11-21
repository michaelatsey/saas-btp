namespace SaasBtp.Access.Domain.Memberships;

/// <summary>The organization memberships (ADR-ARCH-034 §1: a repository port of the domain).</summary>
public interface IMembershipRepository
{
    /// <summary>Adds a new membership to the unit of work.</summary>
    /// <param name="membership">The membership.</param>
    /// <param name="cancellationToken">Cancels the addition.</param>
    /// <returns>A task completed once it is added.</returns>
    ValueTask AddAsync(Membership membership, CancellationToken cancellationToken = default);
}

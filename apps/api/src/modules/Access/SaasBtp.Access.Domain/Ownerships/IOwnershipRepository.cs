namespace SaasBtp.Access.Domain.Ownerships;

/// <summary>The ownerships (ADR-ARCH-034 §1: a repository port of the domain).</summary>
public interface IOwnershipRepository
{
    /// <summary>Adds a new ownership to the unit of work.</summary>
    /// <param name="ownership">The ownership.</param>
    /// <param name="cancellationToken">Cancels the addition.</param>
    /// <returns>A task completed once it is added.</returns>
    ValueTask AddAsync(Ownership ownership, CancellationToken cancellationToken = default);
}

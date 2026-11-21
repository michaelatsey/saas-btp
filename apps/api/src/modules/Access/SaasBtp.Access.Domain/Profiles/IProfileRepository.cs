namespace SaasBtp.Access.Domain.Profiles;

/// <summary>The profiles (ADR-ARCH-034 §1: a repository port of the domain).</summary>
public interface IProfileRepository
{
    /// <summary>The profile of <paramref name="userId"/>, or null when the person has none.</summary>
    /// <param name="userId">The identity provider's identifier of the person.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The profile, or null.</returns>
    ValueTask<Profile?> FindByUserAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new profile to the unit of work.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="cancellationToken">Cancels the addition.</param>
    /// <returns>A task completed once the profile is added.</returns>
    ValueTask AddAsync(Profile profile, CancellationToken cancellationToken = default);
}

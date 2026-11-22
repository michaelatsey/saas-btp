using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Features.Ports;

/// <summary>
/// The founding of a company as one unit of work of the Access module (TSK-2, contract.md §1): it
/// commits whole or not at all. A port of the Features project, since the handler cannot reach the
/// Adapters that implement it (ADR-ARCH-034 decision 1).
/// </summary>
internal interface IFoundingUnitOfWork
{
    /// <summary>Founds a company for <paramref name="founder"/> under <paramref name="reference"/>, and commits.</summary>
    /// <param name="reference">The request's reference, the organization's identifier.</param>
    /// <param name="organizationName">The company name, measured.</param>
    /// <param name="workspaceName">The first workspace name, measured.</param>
    /// <param name="founder">The signed-in person, who is also the unit of work's subject (BP001-R2).</param>
    /// <param name="cancellationToken">Cancels the founding, which is then rolled back.</param>
    /// <returns>
    /// What the founding created, once committed; or, when <paramref name="reference"/> already
    /// identifies an organization, that nothing was founded (contract.md §3). A conflict on the
    /// founder's profile is retried once, from the start, in a new transaction (contract.md §4).
    /// </returns>
    /// <remarks>Any other failure, a conflict on the profile that persists after the retry included, is thrown, and nothing was committed.</remarks>
    Task<FoundingAttempt> FoundAsync(
        OrganizationId reference,
        OrganizationName organizationName,
        WorkspaceName workspaceName,
        UserId founder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The organization <paramref name="reference"/> identifies when <paramref name="owner"/> is its
    /// current owner, read in a new transaction under the same subject (contract.md §3).
    /// </summary>
    /// <param name="reference">The request's reference.</param>
    /// <param name="owner">The signed-in person.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organization, or <see langword="null"/> when the person is not its current owner or it does not exist.</returns>
    Task<Organization?> FindOwnedAsync(OrganizationId reference, UserId owner, CancellationToken cancellationToken = default);
}

/// <summary>What one founding through <see cref="IFoundingUnitOfWork.FoundAsync"/> comes to.</summary>
internal abstract record FoundingAttempt
{
    private FoundingAttempt()
    {
    }

    /// <summary>The company is founded and committed.</summary>
    /// <param name="Company">What the founding created.</param>
    internal sealed record Founded(FoundedCompany Company) : FoundingAttempt;

    /// <summary>The reference already identifies an organization: nothing was founded, and the unit of work rolled back.</summary>
    internal sealed record ReferenceInUse : FoundingAttempt;
}

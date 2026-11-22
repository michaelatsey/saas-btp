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
    /// <returns>What the founding created, once committed.</returns>
    Task<FoundedCompany> FoundAsync(
        OrganizationId reference,
        OrganizationName organizationName,
        WorkspaceName workspaceName,
        UserId founder,
        CancellationToken cancellationToken = default);
}

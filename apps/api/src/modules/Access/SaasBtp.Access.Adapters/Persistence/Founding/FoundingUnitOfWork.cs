using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters.Persistence.Founding;

/// <summary>
/// The founding of a company (BP-001, TSK-2) as one unit of work of the Access module
/// (contract.md §1): it commits whole or not at all, profile included, and writes nothing to the
/// identity provider (contract.md §4).
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="ITransactionalContext.ExecuteAsync{TState}"/> holds the whole founding: the read
/// of the founder's profile, the domain's founding and every insert run in the one explicit
/// transaction it opens, whose subject the provider sets first (ADR-ARCH-033 §4), and which commits
/// once the operation returns.
/// </para>
/// <para>
/// The rows are inserted in the order of ADR-ARCH-033 §5: the profile (when the founding creates it),
/// the ownership, the organization, the membership, the access, then the workspace, each saved in its
/// own <c>SaveChanges</c>, since the model leaves EF Core no relation to order them by. Each insert
/// policy admits a row only once the rows it reads are there and visible to the subject: the ownership
/// names the subject's profile, the membership, the access and the workspace require the subject to
/// be the organization's current owner, and the workspace is visible only through its access. The
/// keys to the profile and to the organization are not deferred. A row read back with
/// <c>RETURNING</c> would also have to satisfy the select policies, which this order meets; this
/// model generates no value, so EF Core reads none back today.
/// </para>
/// <para>
/// The unit of work is this context's own, <see cref="EfUnitOfWork{TContext}"/> of
/// <see cref="AccessDbContext"/>, never the unkeyed interfaces another module's registration could
/// replace: every command of the founding runs on the Access context, in its transaction.
/// </para>
/// <para>
/// No retry: the profile retry of contract.md §4 and the replay of §3 are not made here. The caller
/// founds for the signed-in person, who is also the unit of work's subject (BP001-R2).
/// </para>
/// </remarks>
internal sealed class FoundingUnitOfWork(
    EfUnitOfWork<AccessDbContext> unitOfWork,
    IProfileRepository profiles,
    IOwnershipRepository ownerships,
    IOrganizationRepository organizations,
    IMembershipRepository memberships,
    IWorkspaceAccessRepository accesses,
    IWorkspaceRepository workspaces,
    TimeProvider clock)
{
    /// <summary>Founds a company for <paramref name="founder"/> under <paramref name="reference"/>.</summary>
    /// <param name="reference">The request's reference, the organization's identifier.</param>
    /// <param name="organizationName">The company name, measured.</param>
    /// <param name="workspaceName">The first workspace name, measured.</param>
    /// <param name="founder">The signed-in person.</param>
    /// <param name="cancellationToken">Cancels the founding, which is then rolled back.</param>
    /// <returns>What the founding created, once committed.</returns>
    /// <exception cref="PersistenceException">
    /// A database failure: a write, a policy or a key, the transaction's begin or its commit. Any other
    /// exception raised inside the transaction propagates as it is. In both cases the transaction is
    /// rolled back and nothing was committed.
    /// </exception>
    public Task<FoundedCompany> FoundAsync(
        OrganizationId reference,
        OrganizationName organizationName,
        WorkspaceName workspaceName,
        UserId founder,
        CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(
            static (state, ct) => state.Founding.FoundInTransactionAsync(state.Reference, state.OrganizationName, state.WorkspaceName, state.Founder, ct),
            (Founding: this, Reference: reference, OrganizationName: organizationName, WorkspaceName: workspaceName, Founder: founder),
            cancellationToken);

    private async Task<FoundedCompany> FoundInTransactionAsync(
        OrganizationId reference,
        OrganizationName organizationName,
        WorkspaceName workspaceName,
        UserId founder,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.FindByUserAsync(founder, cancellationToken).ConfigureAwait(false);
        var founded = CompanyFounding.Found(reference, organizationName, workspaceName, founder, profile, clock);

        if (founded.IsProfileCreated)
        {
            await profiles.AddAsync(founded.Profile, cancellationToken).ConfigureAwait(false);
            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await ownerships.AddAsync(founded.Ownership, cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        await organizations.AddAsync(founded.Organization, cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        await memberships.AddAsync(founded.Membership, cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        await accesses.AddAsync(founded.Access, cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        await workspaces.AddAsync(founded.Workspace, cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return founded;
    }
}

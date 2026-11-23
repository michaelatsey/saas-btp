using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Features.Ports;

namespace SaasBtp.Access.Adapters.Persistence.Queries;

/// <summary>
/// The reads of contract.md §2, on the Access context, each in a transaction of its own opened by the
/// context's unit of work, whose subject the provider sets first (ADR-ARCH-033 §4). They add no
/// condition of the API's own: what the subject sees is what the select policies and
/// <c>access.may_act_in</c> let it read (ADR-ARCH-032 §5).
/// </summary>
/// <remarks>
/// <para>
/// The unit of work is this context's own, <see cref="EfUnitOfWork{TContext}"/> of
/// <see cref="AccessDbContext"/>, and the SQL read is that unit of work's
/// <see cref="IUnitOfWorkSqlQuery{TContext}"/>: it runs on the connection and in the transaction the
/// unit of work opened, never on a connection opened apart, which would carry no subject
/// (ADR-ARCH-034 decision 3). The listing's only value is the subject, which the unit of work sets as
/// a parameter.
/// </para>
/// <para>
/// It implements the Features port <see cref="IAccessQueries"/>, which the two reads' handlers call
/// (ADR-ARCH-034 decision 1).
/// </para>
/// </remarks>
internal sealed class AccessQueries(
    EfUnitOfWork<AccessDbContext> unitOfWork,
    IUnitOfWorkSqlQuery<AccessDbContext> sql,
    AccessDbContext context) : IAccessQueries
{
    /// <remarks>
    /// Defined by <c>access.may_act_in</c> (contract.md §2, §8). The workspaces' select policy lets
    /// the subject read every workspace for which it is true, since both read the same current access
    /// paired with the workspace's organization (ADR-ARCH-033 §5); the function decides which of them
    /// are listed.
    /// </remarks>
    /// <exception cref="MicroKit.Persistence.Abstractions.PersistenceException">A database failure.</exception>
    public Task<IReadOnlyList<EnterableWorkspace>> ListEnterableWorkspacesAsync(CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(
            static (sql, ct) => sql.SqlQueryAsync<EnterableWorkspace>(
                $"""
                select w.id as "Id", w.name as "Name", w.organization_id as "OrganizationId"
                from access.workspaces w
                where access.may_act_in(w.id)
                order by w.name, w.id
                """,
                ct),
            sql,
            cancellationToken);

    /// <remarks>
    /// Read by identifier alone: the organizations' select policy hides every organization the subject
    /// may not see, so a hidden one and one that does not exist read alike. Not tracked: nothing read
    /// here is changed.
    /// </remarks>
    /// <exception cref="MicroKit.Persistence.Abstractions.PersistenceException">A database failure.</exception>
    public Task<Organization?> FindVisibleOrganizationAsync(OrganizationId id, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(
            static (state, ct) => state.Organizations.AsNoTracking().SingleOrDefaultAsync(organization => organization.Id == state.Id, ct),
            (Organizations: context.Set<Organization>(), Id: id),
            cancellationToken);
}

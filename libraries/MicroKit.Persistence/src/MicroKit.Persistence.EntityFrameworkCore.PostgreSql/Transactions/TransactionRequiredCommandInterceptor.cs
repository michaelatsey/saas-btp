using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

/// <summary>
/// Refuses any command EF Core sends outside a transaction, before it reaches the database
/// (ADR-ARCH-033 §4): a command without a transaction would run without a subject.
/// </summary>
/// <remarks>Added in this repository (ADR-ARCH-035 §8). Stateless: one instance serves every context.</remarks>
internal sealed class TransactionRequiredCommandInterceptor : DbCommandInterceptor
{
    public static TransactionRequiredCommandInterceptor Instance { get; } = new();

    private TransactionRequiredCommandInterceptor()
    {
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        EnsureTransaction(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        EnsureTransaction(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        EnsureTransaction(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction(command);
        return ValueTask.FromResult(result);
    }

    private static void EnsureTransaction(DbCommand command)
    {
        if (command.Transaction is null)
        {
            throw new InvalidOperationException(
                "A command was sent outside a transaction and refused before it reached the database. "
                + "Every command runs inside the transaction of a unit of work, which sets its subject first "
                + "(ADR-ARCH-033 §4).");
        }
    }
}

using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

/// <summary>
/// Sets the subject of each transaction as its first statement:
/// <c>select set_config('app.user_id', @subject, true)</c>, sent through ADO.NET on the
/// transaction's own connection once the transaction has started, before EF Core returns it and so
/// before any other command or savepoint (ADR-ARCH-032 §3, ADR-ARCH-033 §4).
/// </summary>
/// <remarks>
/// <para>
/// Added in this repository (ADR-ARCH-035 §8). The setting is transaction-local
/// (<c>is_local</c> true): it ends with the transaction, committed or rolled back, so a pooled
/// connection never carries a subject into the next one.
/// </para>
/// <para>
/// A transaction started without a subject, or whose subject could not be set, is rolled back
/// and disposed, and the start fails with an error: no command runs in it. EF Core opened the
/// connection to begin it and leaves it open until the context closes it or is disposed.
/// </para>
/// </remarks>
internal sealed class SubjectTransactionInterceptor(IUnitOfWorkSubject? subject) : DbTransactionInterceptor
{
    internal const string SetSubjectSql = "select set_config('app.user_id', @subject, true)";

    public override DbTransaction TransactionStarted(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        try
        {
            using var command = CreateCommand(connection, result);
            command.ExecuteNonQuery();
        }
        catch
        {
            Abandon(result);
            throw;
        }

        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = CreateCommand(connection, result);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await AbandonAsync(result).ConfigureAwait(false);
            throw;
        }

        return result;
    }

    private DbCommand CreateCommand(DbConnection connection, DbTransaction transaction)
    {
        var current = subject?.Current ?? throw new InvalidOperationException(
            subject is null
                ? "A transaction was started without a subject: no IUnitOfWorkSubject is registered. "
                  + "Register the host's implementation of IUnitOfWorkSubject (ADR-ARCH-033 §4)."
                : "A transaction was started without a subject: IUnitOfWorkSubject.Current is null. "
                  + "Every transaction runs for an authenticated subject (ADR-ARCH-033 §4).");

        var command = connection.CreateCommand();
        command.Transaction = transaction;
#pragma warning disable CA2100 // a constant text; the subject is a parameter (ADR-ARCH-032 §3)
        command.CommandText = SetSubjectSql;
#pragma warning restore CA2100
        var parameter = command.CreateParameter();
        parameter.ParameterName = "subject";
        parameter.DbType = DbType.String;
        parameter.Value = current.ToString();
        command.Parameters.Add(parameter);
        return command;
    }

    // Runs while an exception is in flight: a failure here must not replace it.
#pragma warning disable CA1031 // never mask the in-flight exception
    private static void Abandon(DbTransaction transaction)
    {
        try
        {
            transaction.Rollback();
        }
        catch (Exception)
        {
        }

        transaction.Dispose();
    }

    private static async ValueTask AbandonAsync(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        await transaction.DisposeAsync().ConfigureAwait(false);
    }
#pragma warning restore CA1031
}

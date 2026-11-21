using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

/// <summary>
/// Refuses a transaction passed to the context with <c>Database.UseTransaction</c>, as soon as it is
/// used (ADR-ARCH-033 §4): the context did not begin it, so its subject was never set, and no unit of
/// work opened it.
/// </summary>
/// <remarks>
/// Added in this repository (ADR-ARCH-035 §8, issue #212). EF Core reports such a transaction as
/// used, not started, so <see cref="SubjectTransactionInterceptor"/> never sees it. The refusal comes
/// before the context takes the transaction: <c>Database.CurrentTransaction</c> stays
/// <see langword="null"/>, so a command sent afterwards is refused as outside a transaction. The
/// connection and the transaction are left as the caller had them, open, and are the caller's to end:
/// the open EF Core counted while using the transaction is closed again before the refusal, so the
/// caller's own <c>CloseConnection</c> still closes a connection the context opened.
/// Stateless: one instance serves every context.
/// </remarks>
internal sealed class UsedTransactionInterceptor : DbTransactionInterceptor
{
    public static UsedTransactionInterceptor Instance { get; } = new();

    private UsedTransactionInterceptor()
    {
    }

    // EF Core opens the context's connection, counting the open, before it reports the transaction
    // as used; the refusal skips the close that would match it. Closing once here gives the count
    // back, so the caller's own CloseConnection still closes a connection the context opened.
    public override DbTransaction TransactionUsed(
        DbConnection connection, TransactionEventData eventData, DbTransaction result)
    {
        eventData.Context?.Database.CloseConnection();
        throw Refusal();
    }

    public override async ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection, TransactionEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        throw Refusal();
    }

    private static InvalidOperationException Refusal() =>
        new(
            "A transaction passed in with Database.UseTransaction was refused: the context did not begin it, so "
            + "its subject was never set, and no unit of work opened it. Every command runs in the transaction "
            + "of a unit of work, which sets its subject first (ADR-ARCH-033 §4).");
}

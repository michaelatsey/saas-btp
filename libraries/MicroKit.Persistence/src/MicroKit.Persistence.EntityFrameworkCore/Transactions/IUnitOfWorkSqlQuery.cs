namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// A read in SQL that runs on the connection and transaction of the current unit of work of
/// <typeparamref name="TContext"/>. Added in this repository (issue #213, ADR-ARCH-034 §3,
/// ADR-ARCH-035 §8).
/// </summary>
/// <typeparam name="TContext">
/// The context whose unit of work the read runs in. A module resolves the read of its own context, so
/// that another module's registration never replaces it.
/// </typeparam>
/// <remarks>
/// <para>
/// <see cref="EfUnitOfWork{TContext}"/> implements it, and
/// <see cref="PersistenceServiceCollectionExtensions.AddUnitOfWork{TContext}"/> binds it to the
/// scope's unit of work of <typeparamref name="TContext"/>. A read adapter injects it when a query is
/// better written in SQL than in LINQ, and calls it from inside
/// <see cref="ITransactionalContext.ExecuteAsync{TState}"/> or <c>TryExecuteAsync</c>.
/// </para>
/// <para>
/// <b>The unit of work's connection and transaction.</b> The read runs on the context's own
/// connection, in the transaction the unit of work opened: it sees the subject that transaction set
/// first (ADR-ARCH-032 §3) and the rows the unit of work saved before it. It never opens a connection
/// of its own and never uses <c>Database.UseTransaction</c>.
/// </para>
/// <para>
/// <b>Refused outside a unit of work.</b> With no transaction open, or with a transaction the unit of
/// work did not open (begun on the context, or after the unit of work ended), the read throws an
/// <see cref="InvalidOperationException"/>: it opens no connection and sends no command.
/// </para>
/// <para>
/// <b>Values only as parameters.</b> The SQL is a <see cref="FormattableString"/>, and no overload takes
/// a <see cref="string"/>. Written as an interpolated string, each interpolated value is sent as a
/// database parameter, never in the text of the command (ADR-ARCH-032 §3). A format built at run time
/// (<c>FormattableStringFactory.Create</c> over concatenated text) puts that text in the command
/// unparameterized: it is SQL assembled by concatenation, a defect, and must not be used. The read
/// cannot refuse it: at run time such a string cannot be told apart from one the compiler builds from
/// an interpolated string, as with EF Core's own <c>Database.SqlQuery</c>. No analyzer rule of this
/// library detects it either (MKP001 to MKP009), so review enforces it (ADR-ARCH-032 §3).
/// </para>
/// <para>
/// The rows are materialized and not tracked. A row's type is a scalar type or a type whose properties
/// are named after the columns, as EF Core's <c>Database.SqlQuery</c> maps them.
/// </para>
/// </remarks>
public interface IUnitOfWorkSqlQuery<TContext>
    where TContext : DbContext
{
    /// <summary>
    /// Runs <paramref name="sql"/> on the connection and transaction of the current unit of work and
    /// returns its rows.
    /// </summary>
    /// <typeparam name="TElement">The type of a row: a scalar type, or a type mapped by column name.</typeparam>
    /// <param name="sql">The read, as an interpolated string; each interpolated value becomes a parameter.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The rows the read returned, in its order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The read was called outside the unit of work's own transaction; it opened no connection and sent
    /// no command.
    /// </exception>
    Task<IReadOnlyList<TElement>> SqlQueryAsync<TElement>(FormattableString sql, CancellationToken ct = default);
}

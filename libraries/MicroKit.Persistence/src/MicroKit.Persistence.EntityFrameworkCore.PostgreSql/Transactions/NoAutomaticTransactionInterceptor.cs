using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

/// <summary>
/// Disables EF Core's automatic transactions (ADR-ARCH-033 §4): before each save, it sets
/// <c>Database.AutoTransactionBehavior</c> to <see cref="AutoTransactionBehavior.Never"/>, so that
/// <c>SaveChanges</c> outside a unit of work never begins a transaction of its own, which would
/// receive a subject; its commands are then refused by <see cref="TransactionRequiredCommandInterceptor"/>.
/// </summary>
/// <remarks>
/// Added in this repository (ADR-ARCH-035 §8). The setting exists only on the context's
/// <c>DatabaseFacade</c>, not in its options, and <c>SaveChanges</c> reads it after its interceptors
/// have run; setting it at each save also covers a pooled context, whose reset restores the default,
/// and a context that changed it. Stateless: one instance serves every context.
/// </remarks>
internal sealed class NoAutomaticTransactionInterceptor : SaveChangesInterceptor
{
    public static NoAutomaticTransactionInterceptor Instance { get; } = new();

    private NoAutomaticTransactionInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Disable(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Disable(eventData);
        return ValueTask.FromResult(result);
    }

    private static void Disable(DbContextEventData eventData)
    {
        if (eventData.Context is { } context)
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
    }
}

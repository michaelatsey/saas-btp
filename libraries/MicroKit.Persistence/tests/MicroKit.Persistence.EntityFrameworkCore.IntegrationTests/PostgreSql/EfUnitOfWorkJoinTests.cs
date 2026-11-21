using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.PostgreSql;

/// <summary>
/// #196 D4: called while the unit of work's own transaction is open, <c>ExecuteAsync</c> joins it.
/// It runs the operation directly — no execution strategy, no begin, commit or rollback — and
/// leaves the transaction exactly as it found it, whether the operation succeeds or throws. A
/// transaction the caller opened is refused instead (changed here, issue #212, ADR-ARCH-033 §4).
/// Every case runs with the default (non-retrying) strategy and with a retrying one.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class EfUnitOfWorkJoinTests(PostgreSqlSuiteFixture fixture)
{
    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_NestedInExecuteAsync_JoinsTheOuterTransaction(int? maxRetryCount)
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var transactions = new TransactionCountingInterceptor();
        await using var context = database.CreateContext(maxRetryCount, transactions);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        await sut.ExecuteAsync(static async (s, ct) =>
        {
            await s.Insert("outer", ct);
            var outer = Present.NotNull(s.Context.Database.CurrentTransaction);

            await s.UnitOfWork.ExecuteAsync(static async (inner, token) =>
            {
                Assert.Same(inner.Outer, inner.Context.Database.CurrentTransaction);
                await inner.Insert("inner", token);
            }, s with { Outer = outer }, ct);

            Assert.Same(outer, s.Context.Database.CurrentTransaction);
            Assert.True(IsOpen(outer), "the inner call must leave the outer transaction open");
        }, new JoinState(sut, context, null), CancellationToken.None);

        Assert.Equal(1, transactions.Begun);
        Assert.Equal(1, transactions.Committed);
        Assert.Equal(0, transactions.RolledBack);
        Assert.Equal(["inner", "outer"], (await database.ReadRowsAsync()).Select(r => r.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_NestedInExecuteAsync_WhenTheInnerOperationThrows_NeitherCommitsNorRollsBack(int? maxRetryCount)
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var transactions = new TransactionCountingInterceptor();
        await using var context = database.CreateContext(maxRetryCount, transactions);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        await sut.ExecuteAsync(static async (s, ct) =>
        {
            await s.Insert("outer", ct);
            var outer = Present.NotNull(s.Context.Database.CurrentTransaction);

            var failure = new InvalidOperationException("inner failed");
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                s.UnitOfWork.ExecuteAsync(static async (inner, token) =>
                {
                    await inner.Insert("inner-before-failure", token);
                    throw inner.Failure!;
                }, s with { Failure = failure }, ct));

            Assert.Same(failure, thrown);
            Assert.Same(outer, s.Context.Database.CurrentTransaction);
            Assert.True(IsOpen(outer), "a failing inner call must not end the caller's transaction");
        }, new JoinState(sut, context, null), CancellationToken.None);

        Assert.Equal(1, transactions.Committed);
        Assert.Equal(0, transactions.RolledBack);
        Assert.Equal(["inner-before-failure", "outer"], (await database.ReadRowsAsync()).Select(r => r.Name));
    }

    // Changed here (issue #212, ADR-ARCH-033 §4): MicroKit joined a transaction the caller opened
    // (ExecuteAsync_InsideACallerTransaction_JoinsIt_AndLeavesItToTheCaller and
    // ExecuteAsync_InsideACallerTransaction_WhenTheOperationThrows_LeavesItOpenAndNotRolledBack, at
    // michaelatsey/microkit@a12097b). The unit of work now refuses a transaction it did not open, so
    // the operation never runs: the two cases become this refusal.
    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_InsideACallerTransaction_IsRefused_AndLeavesItToTheCaller(int? maxRetryCount)
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var transactions = new TransactionCountingInterceptor();
        await using var context = database.CreateContext(maxRetryCount, transactions);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var ran = false;

        // The documented way to open a transaction under any strategy: inside the strategy's own
        // ExecuteAsync (a retrying strategy rejects a user transaction opened outside it).
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var callerTransaction = await context.Database.BeginTransactionAsync();

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sut.ExecuteAsync(async (s, ct) =>
                {
                    ran = true;
                    await s.Insert("never", ct);
                }, new JoinState(sut, context, callerTransaction), CancellationToken.None));

            Assert.Contains("did not open", thrown.Message);
            Assert.False(ran, "a refused unit of work must not run its operation");
            Assert.Same(callerTransaction, context.Database.CurrentTransaction);
            Assert.True(IsOpen(callerTransaction), "the caller decides the fate of its own transaction");
            Assert.Equal(0, transactions.Committed);
            Assert.Equal(0, transactions.RolledBack);

            await callerTransaction.CommitAsync();
        });

        Assert.Equal(1, transactions.Begun);
        Assert.Equal(1, transactions.Committed);
        Assert.Empty(await database.ReadRowsAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_NestedCommitFailure_IsTranslatedExactlyOnce(int? maxRetryCount)
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var existingId = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.Rows.Add(new ExecutionRow { Id = existingId, Name = "existing" });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = database.CreateContext(maxRetryCount);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        var thrown = await Assert.ThrowsAsync<PersistenceException>(() =>
            sut.ExecuteAsync(static (s, ct) =>
                s.UnitOfWork.ExecuteAsync(static async (inner, token) =>
                {
                    inner.Context.Rows.Add(new ExecutionRow { Id = inner.DuplicateId, Name = "duplicate" });
                    await inner.UnitOfWork.CommitAsync(token);
                }, s, ct),
                new JoinState(sut, context, null) { DuplicateId = existingId },
                CancellationToken.None));

        // One PersistenceException around the provider failure: never a PersistenceException in a PersistenceException.
        Assert.IsAssignableFrom<DbUpdateException>(thrown.InnerException);
    }

    // An IDbContextTransaction is open while its underlying DbTransaction still has a connection.
    private static bool IsOpen(IDbContextTransaction transaction) => transaction.GetDbTransaction().Connection is not null;

    private sealed record JoinState(
        EfUnitOfWork<ExecutionDbContext> UnitOfWork,
        ExecutionDbContext Context,
        IDbContextTransaction? Outer)
    {
        public Exception? Failure { get; init; }

        public Guid DuplicateId { get; init; }

        public async Task Insert(string name, CancellationToken ct)
        {
            Context.Rows.Add(new ExecutionRow { Id = Guid.NewGuid(), Name = name });
            await UnitOfWork.CommitAsync(ct);
        }
    }
}

using MicroKit.Persistence.EntityFrameworkCore;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.PostgreSql;

/// <summary>
/// #196 D5: the rollback that follows a failed operation never runs with a cancelled token and
/// never replaces the exception in flight — the caller always observes the original failure.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class EfUnitOfWorkRollbackTests(PostgreSqlSuiteFixture fixture)
{
    [Fact]
    public async Task ExecuteAsync_WhenTheTokenIsCancelledAndTheOperationThrows_SurfacesTheOriginalException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var transactions = new TransactionCountingInterceptor();
        await using var context = database.CreateContext(maxRetryCount: null, transactions);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        using var cts = new CancellationTokenSource();
        var failure = new InvalidOperationException("the operation failed after cancellation was requested");

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => sut.ExecuteAsync(
            static async (s, ct) =>
            {
                s.Context.Rows.Add(new ExecutionRow { Id = Guid.NewGuid(), Name = "doomed" });
                await s.UnitOfWork.CommitAsync(ct);
                s.Cancellation.Cancel();
                throw s.Failure;
            },
            (UnitOfWork: sut, Context: context, Cancellation: cts, Failure: failure),
            cts.Token));

        Assert.Same(failure, thrown);
        Assert.Equal(1, transactions.RolledBack);
        Assert.Empty((await database.ReadRowsAsync()));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRollbackItselfFails_SurfacesTheOriginalException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var failingRollback = new FailingRollbackInterceptor();
        await using var context = database.CreateContext(maxRetryCount: null, failingRollback);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var failure = new InvalidOperationException("the operation failed");

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => sut.ExecuteAsync(static async (s, ct) =>
            {
                s.Context.Rows.Add(new ExecutionRow { Id = Guid.NewGuid(), Name = "doomed" });
                await s.UnitOfWork.CommitAsync(ct);
                throw s.Failure;
            }, (UnitOfWork: sut, Context: context, Failure: failure), TestContext.Current.CancellationToken));

        Assert.Equal(1, failingRollback.Attempts);
        Assert.Same(failure, thrown);
        Assert.Empty((await database.ReadRowsAsync()));
    }
}

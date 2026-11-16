using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.PostgreSql;

/// <summary>
/// #196 D3: a transient failure of the command's <c>SaveChangesAsync</c> must be retried by the
/// execution strategy <c>ExecuteAsync</c> runs. The operation is driven the way
/// <c>TransactionBehavior</c> drives it: stage, <c>CommitAsync</c> inside <c>ExecuteAsync</c>,
/// <c>DiscardChanges</c> and rethrow on failure.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class EfUnitOfWorkRetryTests(PostgreSqlSuiteFixture fixture)
{
    [Fact]
    public async Task ExecuteAsync_WhenCommitFailsTransientlyOnce_RetriesTheOperationAndWritesOnce()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var failOnce = new FailingInsertInterceptor(failures: 1);
        await using var context = database.CreateContext(maxRetryCount: 3, failOnce);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, Guid.NewGuid());

        await sut.ExecuteAsync(StageAndCommit, state, TestContext.Current.CancellationToken);

        Assert.Equal(1, failOnce.Thrown);
        Assert.Equal(2, state.Attempts);
        Assert.Equal(state.RowId, Assert.Single((await database.ReadRowsAsync())).Id);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRetriesAreExhausted_ThrowsPersistenceExceptionWrappingRetryLimitExceeded()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        await using var context = database.CreateContext(maxRetryCount: 2, new FailingInsertInterceptor(int.MaxValue));
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, Guid.NewGuid());

        var thrown = await Assert.ThrowsAsync<PersistenceException>(() => sut.ExecuteAsync(StageAndCommit, state, TestContext.Current.CancellationToken));

        Assert.IsType<RetryLimitExceededException>(thrown.InnerException);
        Assert.Equal(3, state.Attempts);
        Assert.Empty((await database.ReadRowsAsync()));
    }

    [Fact]
    public async Task CommitAsync_OutsideExecuteAsync_WhenTheStrategyExhaustsItsRetries_ThrowsPersistenceException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var alwaysFail = new FailingInsertInterceptor(int.MaxValue);
        await using var context = database.CreateContext(maxRetryCount: 2, alwaysFail);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        context.Rows.Add(new ExecutionRow { Id = Guid.NewGuid(), Name = "row" });

        // SaveChangesAsync runs the retrying strategy itself and gives up with
        // RetryLimitExceededException, which is not a DbUpdateException.
        var thrown = await Assert.ThrowsAsync<PersistenceException>(async () => await sut.CommitAsync(TestContext.Current.CancellationToken));

        Assert.IsType<RetryLimitExceededException>(thrown.InnerException);
        Assert.Equal(3, alwaysFail.Thrown);
        Assert.Empty((await database.ReadRowsAsync()));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommitFailsNonTransiently_SurfacesTheOriginalPersistenceExceptionWithoutRetry()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var existingId = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.Rows.Add(new ExecutionRow { Id = existingId, Name = "existing" });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = database.CreateContext(maxRetryCount: 3);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, existingId); // duplicate primary key: 23505

        var thrown = await Assert.ThrowsAsync<PersistenceException>(() => sut.ExecuteAsync(StageAndCommit, state, TestContext.Current.CancellationToken));

        Assert.Same(state.LastCommitFailure, thrown);
        Assert.IsAssignableFrom<DbUpdateException>(thrown.InnerException);
        Assert.Equal(1, state.Attempts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_WhenTheOperationSavesDirectlyAndTheWriteFails_ThrowsPersistenceException(int? maxRetryCount)
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
        var attempts = 0;

        // The operation flushes through the DbContext itself, not through CommitAsync: the
        // DbUpdateException never passes through CommitAsync's translation.
        var thrown = await Assert.ThrowsAsync<PersistenceException>(() => sut.ExecuteAsync(static async (s, ct) =>
        {
            s.Attempts();
            s.Context.Rows.Add(new ExecutionRow { Id = s.Id, Name = "duplicate" }); // duplicate primary key: 23505
            await s.Context.SaveChangesAsync(ct);
        }, (Context: context, Id: existingId, Attempts: (Action)(() => attempts++)), TestContext.Current.CancellationToken));

        Assert.IsAssignableFrom<DbUpdateException>(thrown.InnerException);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_WithTheDefaultStrategy_WhenCommitFailsWithAProviderTransientError_ThrowsPersistenceException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        // NpgsqlException.IsTransient is true for an IOException cause: the provider's default,
        // non-retrying strategy recognises it and wraps it in an InvalidOperationException that
        // suggests enabling retries. The caller must still observe PersistenceException only.
        var transient = new FailingInsertInterceptor(
            failures: 1, () => new Npgsql.NpgsqlException("Simulated broken connection.", new IOException("reset")));
        await using var context = database.CreateContext(maxRetryCount: null, transient);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, Guid.NewGuid());

        var thrown = await Assert.ThrowsAsync<PersistenceException>(() => sut.ExecuteAsync(StageAndCommit, state, TestContext.Current.CancellationToken));

        Assert.Same(state.LastCommitFailure, thrown);
        Assert.Equal(1, state.Attempts);
    }

    [Fact]
    public async Task ExecuteAsync_WhenATransientFailureIsFollowedByANonTransientOne_SurfacesTheSecondPersistenceException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var existingId = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.Rows.Add(new ExecutionRow { Id = existingId, Name = "existing" });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = database.CreateContext(maxRetryCount: 3, new FailingInsertInterceptor(failures: 1));
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, existingId); // attempt 1: transient; attempt 2: 23505

        var thrown = await Assert.ThrowsAsync<PersistenceException>(() => sut.ExecuteAsync(StageAndCommit, state, TestContext.Current.CancellationToken));

        Assert.Equal(2, state.Attempts);
        Assert.Same(state.LastCommitFailure, thrown);
        var dbUpdate = Assert.IsAssignableFrom<DbUpdateException>(thrown.InnerException);
        Assert.NotNull(dbUpdate);
        Assert.Equal("23505", Assert.IsType<Npgsql.PostgresException>(dbUpdate.InnerException).SqlState);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheTransactionCommitFailsTransiently_RetriesTheOperation()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var failCommitOnce = new FailingCommitInterceptor(failures: 1, () => new TransientMarkerException());
        await using var context = database.CreateContext(maxRetryCount: 3, failCommitOnce);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, Guid.NewGuid());

        await sut.ExecuteAsync(StageAndCommit, state, TestContext.Current.CancellationToken);

        Assert.Equal(1, failCommitOnce.Thrown);
        Assert.Equal(2, state.Attempts);
        Assert.Equal(state.RowId, Assert.Single((await database.ReadRowsAsync())).Id);
    }

    [Fact]
    public async Task ExecuteAsync_WithTheDefaultStrategy_WhenTheTransactionCommitFailsTransiently_ThrowsPersistenceException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var failCommit = new FailingCommitInterceptor(
            failures: 1, () => new Npgsql.NpgsqlException("Simulated broken connection.", new IOException("reset")));
        await using var context = database.CreateContext(maxRetryCount: null, failCommit);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        var thrown = await Assert.ThrowsAsync<PersistenceException>(() =>
            sut.ExecuteAsync(StageAndCommit, new CommandState(sut, context, Guid.NewGuid()), TestContext.Current.CancellationToken));

        Assert.NotNull(thrown.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_WhenADeferredConstraintFailsAtCommit_ThrowsPersistenceException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        await using (var ddl = database.CreateContext())
        {
            await ddl.Database.ExecuteSqlRawAsync("CREATE TABLE deferred_codes (code text NOT NULL, CONSTRAINT uq_deferred_code UNIQUE (code) DEFERRABLE INITIALLY DEFERRED)", cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var context = database.CreateContext();
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        // Both inserts succeed; the duplicate is only detected by the transaction's COMMIT.
        var thrown = await Assert.ThrowsAsync<PersistenceException>(() => sut.ExecuteAsync(static async (ctx, ct) => await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO deferred_codes (code) VALUES ('A'), ('A')", ct), context, TestContext.Current.CancellationToken));

        Assert.Equal("23505", Assert.IsType<Npgsql.PostgresException>(thrown.InnerException).SqlState);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheTransactionCommitFailsAfterSaveChanges_RetriesFromACleanChangeTracker()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var counterId = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.Rows.Add(new ExecutionRow { Id = counterId, Name = "0" });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // SaveChanges succeeds and accepts the change; only the transaction's COMMIT fails. The
        // operation does not discard: a plain ITransactionalContext caller has no reason to.
        await using var context = database.CreateContext(maxRetryCount: 3,
            new FailingCommitInterceptor(failures: 1, () => new TransientMarkerException()));
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        await sut.ExecuteAsync(static async (s, ct) =>
        {
            var counter = await s.Context.Rows.SingleAsync(r => r.Id == s.Id, ct);
            counter.Name = (int.Parse(counter.Name, System.Globalization.CultureInfo.InvariantCulture) + 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            await s.UnitOfWork.CommitAsync(ct);
        }, (UnitOfWork: sut, Context: context, Id: counterId), TestContext.Current.CancellationToken);

        Assert.Equal("1", Assert.Single((await database.ReadRowsAsync())).Name);
    }

    private static async Task StageAndCommit(CommandState state, CancellationToken ct)
    {
        state.Attempts++;
        try
        {
            state.Context.Rows.Add(new ExecutionRow { Id = state.RowId, Name = "row" });
            await state.UnitOfWork.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            if (ex is PersistenceException persistence)
                state.LastCommitFailure = persistence;

            state.UnitOfWork.DiscardChanges(); // TransactionBehavior's non-commit exit
            throw;
        }
    }

    private sealed class CommandState(EfUnitOfWork<ExecutionDbContext> unitOfWork, ExecutionDbContext context, Guid rowId)
    {
        public EfUnitOfWork<ExecutionDbContext> UnitOfWork { get; } = unitOfWork;

        public ExecutionDbContext Context { get; } = context;

        public Guid RowId { get; } = rowId;

        public int Attempts { get; set; }

        public PersistenceException? LastCommitFailure { get; set; }
    }
}

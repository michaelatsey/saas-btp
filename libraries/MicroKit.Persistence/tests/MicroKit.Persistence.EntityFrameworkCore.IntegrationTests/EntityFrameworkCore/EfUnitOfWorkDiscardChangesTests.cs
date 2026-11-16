using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// MicroKit ADR-005 (michaelatsey/microkit@a12097b). The tracker-state cases build the model without ever opening the connection;
/// the two cases that assert what reached the database open a SQLite in-memory connection and
/// keep it alive for the duration of the test.
/// The model types (<c>TestDbContext</c>, <c>TestAggregate</c>) are shared with
/// <c>EfDomainEventsProviderTests</c>.
/// </summary>
public sealed class EfUnitOfWorkDiscardChangesTests
{
    [Fact]
    public void DiscardChanges_AfterStagingEntities_LeavesNothingPending()
    {
        using var context = CreateContext();
        context.Add(new TestAggregate());
        context.Add(new TestAggregate());
        var sut = new EfUnitOfWork<TestDbContext>(context);
        Assert.Equal(2, context.ChangeTracker.Entries().Count());

        sut.DiscardChanges();

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public void DiscardChanges_OnEmptyChangeTracker_DoesNotThrow()
    {
        using var context = CreateContext();
        var sut = new EfUnitOfWork<TestDbContext>(context);

        Assert.Null(Record.Exception(() => sut.DiscardChanges()));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void DiscardChanges_CalledTwice_DiscardsEachTime()
    {
        using var context = CreateContext();
        context.Add(new TestAggregate());
        var sut = new EfUnitOfWork<TestDbContext>(context);

        sut.DiscardChanges();
        Assert.Empty(context.ChangeTracker.Entries());

        // A second command in the same scope stages more work before the second discard,
        // so the call has something to abandon rather than re-asserting the first call's result.
        context.Add(new TestAggregate());
        Assert.Single(context.ChangeTracker.Entries());

        Assert.Null(Record.Exception(() => sut.DiscardChanges()));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void DiscardChanges_DetachesStagedEntity()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        context.Add(aggregate);
        var sut = new EfUnitOfWork<TestDbContext>(context);
        Assert.Equal(EntityState.Added, context.Entry(aggregate).State);

        sut.DiscardChanges();

        // The documented MicroKit ADR-005 (michaelatsey/microkit@a12097b) consequence: a reference held across the discard goes detached.
        Assert.Equal(EntityState.Detached, context.Entry(aggregate).State);
    }

    [Fact]
    public async Task DiscardChanges_ThenCommitAsync_WritesNothing()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var sut = new EfUnitOfWork<TestDbContext>(context);
        context.Add(new TestAggregate());

        sut.DiscardChanges();
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, (await context.Aggregates.CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task DiscardChanges_AfterCommit_LeavesCommittedRowsIntact()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var sut = new EfUnitOfWork<TestDbContext>(context);
        context.Add(new TestAggregate());
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        sut.DiscardChanges();

        Assert.Equal(1, (await context.Aggregates.CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task DiscardChanges_WithModifiedAndDeletedEntries_WritesNeitherChange()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var sut = new EfUnitOfWork<TestDbContext>(context);
        context.AddRange(new TestAggregate { Name = "original" }, new TestAggregate { Name = "keep-me" });
        await sut.CommitAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var toModify = await context.Aggregates.SingleAsync(a => a.Name == "original", cancellationToken: TestContext.Current.CancellationToken);
        toModify.Name = "touched";
        var toDelete = await context.Aggregates.SingleAsync(a => a.Name == "keep-me", cancellationToken: TestContext.Current.CancellationToken);
        context.Remove(toDelete);
        // The same states, whatever their order.
        Assert.Equal(
            new[] { EntityState.Modified, EntityState.Deleted }.Order(),
            context.ChangeTracker.Entries<TestAggregate>().Select(e => e.State).Order());

        sut.DiscardChanges();
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var names = await context.Aggregates.Select(a => a.Name).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        // The same names, whatever their order: neither the UPDATE nor the DELETE may survive the discard.
        string[] expected = ["original", "keep-me"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DiscardInsideTransaction_KeepsTheFlushAndDropsTheStagedSet()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var sut = new EfUnitOfWork<TestDbContext>(context);

        // The production call path: the discard runs inside ExecuteAsync, transaction open.
        await sut.ExecuteAsync(static async ((EfUnitOfWork<TestDbContext> Uow, TestDbContext Ctx) s, CancellationToken ct) =>
            {
                s.Ctx.Add(new TestAggregate { Name = "flushed-in-tx" });
                await s.Uow.CommitAsync(ct);

                s.Ctx.Add(new TestAggregate { Name = "staged-then-discarded" });
                s.Uow.DiscardChanges();
            }, (sut, context), TestContext.Current.CancellationToken);

        // The next command in the same scope flushes its own work. This is what makes the
        // assertion real: without the discard, the residue rides along with this commit.
        context.Add(new TestAggregate { Name = "next-command" });
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var names = await context.Aggregates.Select(a => a.Name).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        // The same names, whatever their order: the transaction committed what was already flushed; the discarded set reached nothing.
        string[] expected = ["flushed-in-tx", "next-command"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DiscardThenThrow_RollsBackTheFlushAndKeepsEarlierRows()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var sut = new EfUnitOfWork<TestDbContext>(context);

        // Committed before the transaction begins — must survive the rollback.
        context.Add(new TestAggregate { Name = "committed-earlier" });
        await sut.CommitAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.ExecuteAsync(static async ((EfUnitOfWork<TestDbContext> Uow, TestDbContext Ctx) s, CancellationToken ct) =>
                {
                    s.Ctx.Add(new TestAggregate { Name = "flushed-in-doomed-tx" });
                    await s.Uow.CommitAsync(ct);

                    s.Ctx.Add(new TestAggregate { Name = "staged-then-discarded" });
                    s.Uow.DiscardChanges();
                    throw new InvalidOperationException("handler failed");
                }, (sut, context), TestContext.Current.CancellationToken));

        Assert.Equal("handler failed", thrown.Message);

        // EF Core's rollback does not reset the change tracker, so this later commit is exactly
        // what would have written the failed command's residue.
        context.Add(new TestAggregate { Name = "next-command" });
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var names = await context.Aggregates.Select(a => a.Name).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        // The same names, whatever their order: the rollback undid the in-transaction flush; the discard kept the staged set out of the next commit.
        string[] expected = ["committed-earlier", "next-command"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
    }

    // The connection is never opened: the model is built, nothing is executed.
    private const string InMemoryConnectionString = "DataSource=:memory:";

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(InMemoryConnectionString)
            .UseMicroKitPersistence()
            .Options;

        return new TestDbContext(options);
    }

    // A SQLite in-memory database lives exactly as long as its connection — the caller keeps it open.
    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(InMemoryConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static TestDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .UseMicroKitPersistence()
            .Options;

        return new TestDbContext(options);
    }
}

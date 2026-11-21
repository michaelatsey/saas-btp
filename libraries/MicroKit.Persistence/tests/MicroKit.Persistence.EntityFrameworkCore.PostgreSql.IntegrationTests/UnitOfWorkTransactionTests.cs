using MicroKit.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests;

using MicroKit.Result;

/// <summary>
/// The unit of work runs every command in the one explicit transaction it opened, and refuses a
/// transaction it did not open (ADR-ARCH-033 §4, ADR-ARCH-035 §8, issue #212). The contexts are
/// registered through <c>UsePostgreSQL</c> and <c>AddUnitOfWork</c>, with a subject, except where a
/// case needs a context without the provider's interceptors.
/// </summary>
[Collection(PostgreSqlServerSuite.Name)]
public sealed class UnitOfWorkTransactionTests(PostgreSqlServer server)
{
    private static readonly Guid Subject = Guid.Parse("0193f3c1-7c2a-7d4e-9a51-3b1f6c2d8e01");

    private static readonly Error BusinessFailure = Error.BusinessRule(ErrorCode.From("TEST.FAILED"), "The operation failed.");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public enum Owner
    {
        ExecuteAsync,
        TryExecuteAsync,
    }

    public enum Undo
    {
        OperationThrows,
        FailedResult,
    }

    public enum Entry
    {
        ExecuteAsync,
        ExecuteAsyncWithAResult,
        TryExecuteAsync,
        TryExecuteAsyncWithAValue,
        CommitAsync,
    }

    [Theory]
    [InlineData(Owner.ExecuteAsync)]
    [InlineData(Owner.TryExecuteAsync)]
    public async Task TwoSaveChangesAndAQueryRunInOneTransactionAndCommitTogether(Owner owner)
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build<CountedSubjectDbContext>(database);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CountedSubjectDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<CountedSubjectDbContext>>();
        var probe = Probe.Start();
        var reads = new List<long>();
        long seenAfterFirst = -1;

        async Task<Result> Operation(CancellationToken ct)
        {
            await context.Rows.AddAsync(new SubjectRow { Name = "first" }, ct);
            await unitOfWork.CommitAsync(ct);
            reads.Add(await TransactionIdAsync(context, ct));
            seenAfterFirst = await context.Rows.LongCountAsync(ct);
            await context.Rows.AddAsync(new SubjectRow { Name = "second" }, ct);
            await unitOfWork.CommitAsync(ct);
            reads.Add(await TransactionIdAsync(context, ct));
            return Result.Success();
        }

        if (owner is Owner.ExecuteAsync)
            await unitOfWork.ExecuteAsync(static async (op, ct) => await op(ct), (Func<CancellationToken, Task<Result>>)Operation, Ct);
        else
            Assert.True((await unitOfWork.TryExecuteAsync(static (op, ct) => op(ct), (Func<CancellationToken, Task<Result>>)Operation, Ct)).IsSuccess);

        Assert.Equal(1, probe.TransactionsBegun);
        Assert.Equal(1, seenAfterFirst);
        Assert.Equal(2, reads.Count);
        Assert.Equal(reads[0], reads[1]);
        // Every command went over one connection, the transaction's.
        Assert.Single(probe.Commands.Select(c => c.ConnectorId).Distinct());
        Assert.Equal(2, await database.CountRowsAsync());
    }

    [Theory]
    [InlineData(Undo.OperationThrows)]
    [InlineData(Undo.FailedResult)]
    public async Task TwoSaveChangesRollBackTogether(Undo undo)
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build<SubjectDbContext>(database);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SubjectDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<SubjectDbContext>>();
        var failure = new InvalidOperationException("the operation failed after both saves");

        async Task<Result> Operation(CancellationToken ct)
        {
            await context.Rows.AddAsync(new SubjectRow { Name = "first" }, ct);
            await unitOfWork.CommitAsync(ct);
            await context.Rows.AddAsync(new SubjectRow { Name = "second" }, ct);
            await unitOfWork.CommitAsync(ct);
            Assert.Equal(2, await context.Rows.LongCountAsync(ct));
            return undo is Undo.OperationThrows ? throw failure : Result.Failure(BusinessFailure);
        }

        if (undo is Undo.OperationThrows)
        {
            var thrown = await Record.ExceptionAsync(() =>
                unitOfWork.ExecuteAsync(static async (op, ct) => await op(ct), (Func<CancellationToken, Task<Result>>)Operation, Ct));
            Assert.Same(failure, thrown);
        }
        else
        {
            var result = await unitOfWork.TryExecuteAsync(static (op, ct) => op(ct), (Func<CancellationToken, Task<Result>>)Operation, Ct);
            Assert.Same(BusinessFailure, result.Error);
        }

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(0, await database.CountRowsAsync());
    }

    [Fact]
    public async Task AFailureAfterTheFirstSaveChangesLeavesNoRowOfEither()
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build<SubjectDbContext>(database);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SubjectDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<SubjectDbContext>>();
        var firstSaved = false;

        var thrown = await Record.ExceptionAsync(() => unitOfWork.ExecuteAsync(
            async (_, ct) =>
            {
                var first = new SubjectRow { Name = "first" };
                await context.Rows.AddAsync(first, ct);
                await unitOfWork.CommitAsync(ct);
                firstSaved = true;

                // A second row with the first one's key: the database refuses the second save.
                context.Entry(first).State = EntityState.Detached;
                await context.Rows.AddAsync(new SubjectRow { Id = first.Id, Name = "second" }, ct);
                await unitOfWork.CommitAsync(ct);
            },
            0,
            Ct));

        Assert.True(firstSaved);
        var persistence = Assert.IsType<PersistenceException>(thrown);
        Assert.IsType<PostgresException>(Assert.IsAssignableFrom<DbUpdateException>(persistence.InnerException).InnerException);
        Assert.Equal(0, await database.CountRowsAsync());
    }

    [Fact]
    public async Task AFailureAtCommitLeavesNoRowOfEitherAndTheNextUnitOfWorkBeginsAgain()
    {
        await using var database = await server.CreateDatabaseAsync();
        // A constraint checked only at COMMIT, as the founding's deferred keys are (ADR-ARCH-033 §5).
        await database.ExecuteAsync(
            "alter table subject_rows add constraint subject_rows_name_key unique (\"Name\") deferrable initially deferred");
        await using var services = Build<CountedSubjectDbContext>(database);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CountedSubjectDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<CountedSubjectDbContext>>();
        var probe = Probe.Start();
        var savedBoth = false;

        var thrown = await Record.ExceptionAsync(() => unitOfWork.ExecuteAsync(
            async (_, ct) =>
            {
                await context.Rows.AddAsync(new SubjectRow { Name = "same" }, ct);
                await unitOfWork.CommitAsync(ct);
                await context.Rows.AddAsync(new SubjectRow { Name = "same" }, ct);
                await unitOfWork.CommitAsync(ct);
                savedBoth = true;
            },
            0,
            Ct));

        Assert.True(savedBoth);
        var persistence = Assert.IsType<PersistenceException>(thrown);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(persistence.InnerException).SqlState);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(0, await database.CountRowsAsync());

        // The failed attempt left nothing behind: the same unit of work begins a transaction of its own again.
        await unitOfWork.ExecuteAsync(
            async (_, ct) =>
            {
                await context.Rows.AddAsync(new SubjectRow { Name = "after" }, ct);
                await unitOfWork.CommitAsync(ct);
            },
            0,
            Ct);

        Assert.Equal(2, probe.TransactionsBegun);
        Assert.Equal(1, await database.CountRowsAsync());
    }

    [Fact]
    public async Task ANestedCallJoinsTheUnitOfWorksOwnTransaction()
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build<CountedSubjectDbContext>(database);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CountedSubjectDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<CountedSubjectDbContext>>();
        var probe = Probe.Start();

        await unitOfWork.ExecuteAsync(
            async (_, ct) =>
            {
                await context.Rows.AddAsync(new SubjectRow { Name = "outer" }, ct);
                await unitOfWork.CommitAsync(ct);
                var outer = context.Database.CurrentTransaction;

                await unitOfWork.ExecuteAsync(
                    async (_, token) =>
                    {
                        Assert.Same(outer, context.Database.CurrentTransaction);
                        await context.Rows.AddAsync(new SubjectRow { Name = "inner" }, token);
                        await unitOfWork.CommitAsync(token);
                    },
                    0,
                    ct);

                var result = await unitOfWork.TryExecuteAsync(
                    async (_, token) =>
                    {
                        Assert.Same(outer, context.Database.CurrentTransaction);
                        await context.Rows.AddAsync(new SubjectRow { Name = "tried" }, token);
                        await unitOfWork.CommitAsync(token);
                        return Result.Success();
                    },
                    0,
                    ct);
                Assert.True(result.IsSuccess);
            },
            0,
            Ct);

        Assert.Equal(1, probe.TransactionsBegun);
        Assert.Equal(3, await database.CountRowsAsync());
    }

    [Theory]
    [InlineData(Entry.ExecuteAsync)]
    [InlineData(Entry.ExecuteAsyncWithAResult)]
    [InlineData(Entry.TryExecuteAsync)]
    [InlineData(Entry.TryExecuteAsyncWithAValue)]
    [InlineData(Entry.CommitAsync)]
    public async Task AUnitOfWorkRefusesATransactionBegunOnTheContext(Entry entry)
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build<SubjectDbContext>(database);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SubjectDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<SubjectDbContext>>();

        // Begun on the context, outside the unit of work: it sets its subject first (#211).
        await using var caller = await context.Database.BeginTransactionAsync(Ct);

        var ran = await AssertRefusedAsync(context, unitOfWork, entry);

        Assert.False(ran, "a refused unit of work must not run its operation");
        Assert.Same(caller, context.Database.CurrentTransaction);
        Assert.NotNull(caller.GetDbTransaction().Connection);

        // The caller still owns its transaction: committing it writes nothing of the refused work.
        await caller.CommitAsync(Ct);
        Assert.Equal(0, await database.CountRowsAsync());
    }

    [Theory]
    [InlineData(Entry.ExecuteAsync)]
    [InlineData(Entry.ExecuteAsyncWithAResult)]
    [InlineData(Entry.TryExecuteAsync)]
    [InlineData(Entry.TryExecuteAsyncWithAValue)]
    [InlineData(Entry.CommitAsync)]
    public async Task AUnitOfWorkRefusesATransactionPassedInThroughUseTransaction(Entry entry)
    {
        // A context without the provider's interceptors, which refuse UseTransaction itself
        // (SubjectInterceptionTests): here the unit of work's own refusal is what is tested.
        await using var database = await server.CreateDatabaseAsync();
        var options = new DbContextOptionsBuilder<SubjectDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var context = new SubjectDbContext(options);
        var unitOfWork = new EfUnitOfWork<SubjectDbContext>(context);
        await context.Database.OpenConnectionAsync(Ct);
        await using var external = await context.Database.GetDbConnection().BeginTransactionAsync(Ct);
        await context.Database.UseTransactionAsync(external, Ct);

        var ran = await AssertRefusedAsync(context, unitOfWork, entry);

        Assert.False(ran, "a refused unit of work must not run its operation");
        Assert.NotNull(context.Database.CurrentTransaction);
        Assert.Same(external, context.Database.CurrentTransaction.GetDbTransaction());
        await external.CommitAsync(Ct);
        Assert.Equal(0, await database.CountRowsAsync());
    }

    // Calls the unit of work at the entry point; asserts the refusal; returns whether the operation ran.
    private static async Task<bool> AssertRefusedAsync(SubjectDbContext context, EfUnitOfWork<SubjectDbContext> unitOfWork, Entry entry)
    {
        var ran = false;
        async Task Work(CancellationToken ct)
        {
            ran = true;
            await context.Rows.AddAsync(new SubjectRow { Name = "refused" }, ct);
            await unitOfWork.CommitAsync(ct);
        }

        Exception? thrown;
        switch (entry)
        {
            case Entry.ExecuteAsync:
                thrown = await Record.ExceptionAsync(() => unitOfWork.ExecuteAsync(static (w, ct) => w(ct), (Func<CancellationToken, Task>)Work, Ct));
                break;
            case Entry.ExecuteAsyncWithAResult:
                thrown = await Record.ExceptionAsync(() => unitOfWork.ExecuteAsync(
                    static async (w, ct) =>
                    {
                        await w(ct);
                        return 1;
                    },
                    (Func<CancellationToken, Task>)Work,
                    Ct));
                break;
            case Entry.TryExecuteAsync:
                thrown = await Record.ExceptionAsync(async () => await unitOfWork.TryExecuteAsync(
                    static async (w, ct) =>
                    {
                        await w(ct);
                        return Result.Success();
                    },
                    (Func<CancellationToken, Task>)Work,
                    Ct));
                break;
            case Entry.TryExecuteAsyncWithAValue:
                thrown = await Record.ExceptionAsync(async () => await unitOfWork.TryExecuteAsync(
                    static async (w, ct) =>
                    {
                        await w(ct);
                        return Result.Success(1);
                    },
                    (Func<CancellationToken, Task>)Work,
                    Ct));
                break;
            case Entry.CommitAsync:
                // The change is staged outside any operation; only the commit is the unit of work's.
                await context.Rows.AddAsync(new SubjectRow { Name = "refused" }, Ct);
                thrown = await Record.ExceptionAsync(async () => await unitOfWork.CommitAsync(Ct));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entry), entry, null);
        }

        Assert.Contains("did not open", Assert.IsType<InvalidOperationException>(thrown).Message);
        return ran;
    }

    private static async Task<long> TransactionIdAsync(SubjectDbContext context, CancellationToken ct) =>
        (await context.Database.SqlQueryRaw<long>("select txid_current() as \"Value\"").ToListAsync(ct)).Single();

    private static ServiceProvider Build<TContext>(TestDatabase database)
        where TContext : SubjectDbContext
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWorkSubject>(new FixedSubject(Subject));
        services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef =>
        {
            ef.UsePostgreSQL<TContext>(database.ConnectionString, Probe.NpgsqlOptions);
            ef.AddUnitOfWork<TContext>();
        }));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FixedSubject(Guid? subject) : IUnitOfWorkSubject
    {
        public Guid? Current => subject;
    }
}

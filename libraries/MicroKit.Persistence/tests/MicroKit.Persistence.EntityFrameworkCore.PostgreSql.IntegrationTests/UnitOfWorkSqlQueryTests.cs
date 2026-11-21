using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests;

using MicroKit.Result;

/// <summary>
/// A read in SQL obtained through <see cref="IUnitOfWorkSqlQuery{TContext}"/> runs on the connection and
/// transaction of the unit of work, is refused outside it before any connection is opened, and takes
/// its values only as parameters (ADR-ARCH-032 §3, ADR-ARCH-034 §3, ADR-ARCH-035 §8, issue #213). The
/// contexts are registered through <c>UsePostgreSQL</c> and <c>AddUnitOfWork</c>, with a subject.
/// </summary>
[Collection(PostgreSqlServerSuite.Name)]
public sealed class UnitOfWorkSqlQueryTests(PostgreSqlServer server)
{
    private static readonly Guid Subject = Guid.Parse("0193f3c1-7c2a-7d4e-9a51-3b1f6c2d8e02");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public enum Inside
    {
        ExecuteAsync,
        TryExecuteAsync,
        NestedExecuteAsync,
        NestedTryExecuteAsync,
        ExecuteAsyncWithRetries,
    }

    public enum Outside
    {
        NoTransaction,
        ATransactionBegunOnTheContext,
        AfterTheUnitOfWorkEnded,
        TheEndedUnitOfWorksTransactionPassedBackIn,
    }

    [Theory]
    [InlineData(Inside.ExecuteAsync)]
    [InlineData(Inside.TryExecuteAsync)]
    [InlineData(Inside.NestedExecuteAsync)]
    [InlineData(Inside.NestedTryExecuteAsync)]
    [InlineData(Inside.ExecuteAsyncWithRetries)]
    public async Task ASqlReadInAUnitOfWorkRunsOnItsConnectionAndTransaction(Inside inside)
    {
        await using var database = await server.CreateDatabaseAsync();
        var options = inside is Inside.ExecuteAsyncWithRetries ? new ProviderOptions { Retry = new RetryOnFailureOptions() } : null;
        await using var services = Build(database, options);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CountedSubjectDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<CountedSubjectDbContext>>();
        var sql = scope.ServiceProvider.GetRequiredService<IUnitOfWorkSqlQuery<CountedSubjectDbContext>>();
        Assert.Same(unitOfWork, sql);
        var probe = Probe.Start();
        string? subject = null;
        long readTransaction = -1;
        long contextTransaction = -2;
        IReadOnlyList<string> names = [];
        var attempts = 0;

        async Task<Result> Operation(CancellationToken ct)
        {
            attempts++;
            await context.Rows.AddAsync(new SubjectRow { Name = "saved before the read" }, ct);
            await unitOfWork.CommitAsync(ct);
            if (inside is Inside.ExecuteAsyncWithRetries && attempts == 1)
            {
                // A transient failure of the first attempt (serialization_failure): the provider's retrying
                // strategy rolls it back and runs the operation again, in a new transaction.
                await context.Database.ExecuteSqlRawAsync("do $$ begin raise exception using errcode = '40001'; end $$", ct);
            }

            subject = (await sql.SqlQueryAsync<string>($"select current_setting('app.user_id', true) as \"Value\"", ct)).Single();
            readTransaction = (await sql.SqlQueryAsync<long>($"select txid_current() as \"Value\"", ct)).Single();
            contextTransaction = (await context.Database.SqlQueryRaw<long>("select txid_current() as \"Value\"").ToListAsync(ct)).Single();
            names = await sql.SqlQueryAsync<string>($"select \"Name\" as \"Value\" from subject_rows", ct);
            return Result.Success();
        }

        var operation = (Func<CancellationToken, Task<Result>>)Operation;
        switch (inside)
        {
            case Inside.ExecuteAsync:
            case Inside.ExecuteAsyncWithRetries:
                await unitOfWork.ExecuteAsync(static async (op, ct) => await op(ct), operation, Ct);
                break;
            case Inside.TryExecuteAsync:
                Assert.True((await unitOfWork.TryExecuteAsync(static (op, ct) => op(ct), operation, Ct)).IsSuccess);
                break;
            case Inside.NestedExecuteAsync:
                await unitOfWork.ExecuteAsync(
                    static (s, ct) => s.UnitOfWork.ExecuteAsync(static async (op, token) => await op(token), s.Operation, ct),
                    (UnitOfWork: unitOfWork, Operation: operation),
                    Ct);
                break;
            case Inside.NestedTryExecuteAsync:
                await unitOfWork.ExecuteAsync(
                    static async (s, ct) => Assert.True((await s.UnitOfWork.TryExecuteAsync(static (op, token) => op(token), s.Operation, ct)).IsSuccess),
                    (UnitOfWork: unitOfWork, Operation: operation),
                    Ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(inside), inside, null);
        }

        // With retries, the first attempt failed and the second ran the read; otherwise one attempt.
        var expectedAttempts = inside is Inside.ExecuteAsyncWithRetries ? 2 : 1;
        Assert.Equal(expectedAttempts, attempts);
        // The read sees the subject the unit of work's transaction set first.
        Assert.Equal(Subject.ToString(), subject);
        // It runs in that transaction: the same transaction id, and the row saved before it, uncommitted
        // then. A rolled-back attempt left no row.
        Assert.Equal(contextTransaction, readTransaction);
        Assert.Equal(["saved before the read"], names);
        Assert.Equal(1, await database.CountRowsAsync());
        Assert.Equal(expectedAttempts, probe.TransactionsBegun);
        // On that connection: EF Core opened it once per attempt, to begin its transaction, never for a
        // read, and every command of an attempt ran on it.
        Assert.Equal(expectedAttempts, probe.ConnectionsOpened);
        Assert.All(probe.Commands, c => Assert.NotNull(c.ConnectorId));
        if (expectedAttempts == 1)
            Assert.Single(probe.Commands.Select(c => c.ConnectorId).Distinct());
    }

    [Theory]
    [InlineData(Outside.NoTransaction)]
    [InlineData(Outside.ATransactionBegunOnTheContext)]
    [InlineData(Outside.AfterTheUnitOfWorkEnded)]
    [InlineData(Outside.TheEndedUnitOfWorksTransactionPassedBackIn)]
    public async Task ASqlReadOutsideAUnitOfWorkFailsAndOpensNoConnection(Outside outside)
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build(database);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CountedSubjectDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<CountedSubjectDbContext>>();
        var sql = scope.ServiceProvider.GetRequiredService<IUnitOfWorkSqlQuery<CountedSubjectDbContext>>();
        await using var begun = outside is Outside.ATransactionBegunOnTheContext
            ? await context.Database.BeginTransactionAsync(Ct)
            : null;
        if (outside is Outside.AfterTheUnitOfWorkEnded or Outside.TheEndedUnitOfWorksTransactionPassedBackIn)
        {
            var ended = await unitOfWork.ExecuteAsync(
                static async (s, ct) =>
                {
                    await s.Sql.SqlQueryAsync<string>($"select current_setting('app.user_id', true) as \"Value\"", ct);
                    return s.Context.Database.CurrentTransaction!.GetDbTransaction();
                },
                (Sql: sql, Context: context),
                Ct);
            if (outside is Outside.TheEndedUnitOfWorksTransactionPassedBackIn)
            {
                // The unit of work's own transaction, kept past its end and handed back to the context:
                // the provider refuses it (issue #212), and the read must not take it for the unit of work's.
                Assert.IsType<InvalidOperationException>(Record.Exception(() => context.Database.UseTransaction(ended)));
                Assert.Null(context.Database.CurrentTransaction);
            }
        }

        // Only what the read itself does is observed.
        var probe = Probe.Start();
        var thrown = await Record.ExceptionAsync(() =>
            sql.SqlQueryAsync<string>($"select current_setting('app.user_id', true) as \"Value\"", Ct));

        var refusal = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(0, probe.ConnectionsOpened);
        Assert.Empty(probe.Commands);
        Assert.Contains("outside the transaction of the unit of work", refusal.Message);
        if (begun is not null)
            Assert.Same(begun, context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task ASqlReadSendsItsValuesAsParameters()
    {
        const string value = "'); select 1; --";
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build(database);
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<CountedSubjectDbContext>>();
        var sql = scope.ServiceProvider.GetRequiredService<IUnitOfWorkSqlQuery<CountedSubjectDbContext>>();
        var probe = Probe.Start();

        var read = await unitOfWork.ExecuteAsync(
            static (s, ct) => s.SqlQueryAsync<string>($"select {value}::text as \"Value\"", ct),
            sql,
            Ct);

        Assert.Equal([value], read);
        var sent = Assert.Single(probe.Commands, c => c.CommandText.Contains("::text as \"Value\"", StringComparison.Ordinal));
        Assert.Contains("$1", sent.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain(value, sent.CommandText, StringComparison.Ordinal);
        Assert.Equal([value], sent.Parameters);
    }

    [Fact]
    public void TheSqlReadTakesNoString()
    {
        var methods = typeof(IUnitOfWorkSqlQuery<>).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var parameters = method.GetParameters();
            Assert.Contains(parameters, p => p.ParameterType == typeof(FormattableString));
            Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(string));
        });
    }

    [Fact]
    public async Task EachContextResolvesTheSqlReadOfItsOwnUnitOfWork()
    {
        await using var database = await server.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWorkSubject>(new FixedSubject(Subject));
        services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef =>
        {
            ef.UsePostgreSQL<CountedSubjectDbContext>(database.ConnectionString, Probe.NpgsqlOptions);
            ef.AddUnitOfWork<CountedSubjectDbContext>();
            // A second context registered after the first, as a second module would.
            ef.UsePostgreSQL<OtherSubjectDbContext>(database.ConnectionString, Probe.NpgsqlOptions);
            ef.AddUnitOfWork<OtherSubjectDbContext>();
        }));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var first = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<CountedSubjectDbContext>>();
        var second = scope.ServiceProvider.GetRequiredService<EfUnitOfWork<OtherSubjectDbContext>>();

        Assert.Same(first, scope.ServiceProvider.GetRequiredService<IUnitOfWorkSqlQuery<CountedSubjectDbContext>>());
        Assert.Same(second, scope.ServiceProvider.GetRequiredService<IUnitOfWorkSqlQuery<OtherSubjectDbContext>>());

        // The first context's read still runs in its own unit of work.
        var sql = scope.ServiceProvider.GetRequiredService<IUnitOfWorkSqlQuery<CountedSubjectDbContext>>();
        var read = await first.ExecuteAsync(
            static (s, ct) => s.SqlQueryAsync<string>($"select current_setting('app.user_id', true) as \"Value\"", ct),
            sql,
            Ct);
        Assert.Equal([Subject.ToString()], read);
    }

    private static ServiceProvider Build(TestDatabase database, ProviderOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWorkSubject>(new FixedSubject(Subject));
        services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef =>
        {
            ef.UsePostgreSQL<CountedSubjectDbContext>(database.ConnectionString, Probe.NpgsqlOptions, options);
            ef.AddUnitOfWork<CountedSubjectDbContext>();
        }));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FixedSubject(Guid? subject) : IUnitOfWorkSubject
    {
        public Guid? Current => subject;
    }

    /// <summary>A second context with options of its own, as a second module's would be.</summary>
    public sealed class OtherSubjectDbContext(DbContextOptions<OtherSubjectDbContext> options) : SubjectDbContext(options);
}

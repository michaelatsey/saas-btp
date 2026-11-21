using System.Data.Common;
using MicroKit.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests;

/// <summary>
/// Every context <c>UsePostgreSQL</c> registers sets the subject of each transaction as its first
/// statement and refuses any command outside a transaction (ADR-ARCH-033 §4, ADR-ARCH-035 §8).
/// What reached the database is read from the commands the Npgsql driver logged, and from the
/// database itself.
/// </summary>
[Collection(PostgreSqlServerSuite.Name)]
public sealed class SubjectInterceptionTests(PostgreSqlServer server)
{
    // The provider's statement as Npgsql sends it: its @subject parameter becomes $1.
    private const string SetSubjectAsSent = "select set_config('app.user_id', $1, true)";

    private static readonly Guid Subject = Guid.Parse("0193f3c1-7c2a-7d4e-9a51-3b1f6c2d8e01");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public enum Refused
    {
        Query,
        QueryAsync,
        SaveChangesOfTwoRows,
        SaveChangesOfTwoRowsAsync,
        RawInsert,
        RawInsertAsync,
        Scalar,
        ScalarAsync,
    }

    public enum Begin
    {
        BeginTransaction,
        BeginTransactionAsync,
        UnitOfWorkExecuteAsync,
    }

    public enum Missing
    {
        NullSubject,
        NullSubjectAsync,
        NoPortRegistered,
        NoPortRegisteredAsync,
    }

    [Theory]
    [InlineData(Refused.Query)]
    [InlineData(Refused.QueryAsync)]
    [InlineData(Refused.SaveChangesOfTwoRows)]
    [InlineData(Refused.SaveChangesOfTwoRowsAsync)]
    [InlineData(Refused.RawInsert)]
    [InlineData(Refused.RawInsertAsync)]
    [InlineData(Refused.Scalar)]
    [InlineData(Refused.ScalarAsync)]
    public async Task ACommandWithoutATransactionIsRefusedAndReachesNoDatabase(Refused command)
    {
        await using var database = await server.CreateDatabaseAsync();
        // A subject is there: were automatic transactions still enabled, SaveChanges would begin
        // one, the subject would be set, and the two rows would be written.
        await using var services = Build<CountedSubjectDbContext>(
            database.ConnectionString, s => s.AddSingleton<IUnitOfWorkSubject>(new FixedSubject(Subject)));
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CountedSubjectDbContext>();
        var probe = Probe.Start();

        var thrown = await Record.ExceptionAsync(() => RunAsync(context, command));

        var refusal = Present(thrown);
        if (command is Refused.SaveChangesOfTwoRows or Refused.SaveChangesOfTwoRowsAsync)
            refusal = Present(Assert.IsType<DbUpdateException>(refusal).InnerException);
        Assert.Contains("outside a transaction", Assert.IsType<InvalidOperationException>(refusal).Message);
        Assert.Equal(0, probe.TransactionsBegun);
        Assert.Empty(probe.Commands);
        Assert.Equal(0, await database.CountRowsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUnitOfWorkCommitOutsideATransactionIsRefusedAndWritesNothing(bool tryCommit)
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build(database, s => s.AddSingleton<IUnitOfWorkSubject>(new FixedSubject(Subject)));
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SubjectDbContext>();
        await context.Rows.AddRangeAsync([new SubjectRow { Name = "one" }, new SubjectRow { Name = "two" }], Ct);

        var thrown = tryCommit
            ? await Record.ExceptionAsync(async () => await scope.ServiceProvider.GetRequiredService<IResultUnitOfWork>().TryCommitAsync(Ct))
            : await Record.ExceptionAsync(async () => await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(Ct));

        var update = Assert.IsType<DbUpdateException>(Assert.IsType<PersistenceException>(thrown).InnerException);
        Assert.Contains("outside a transaction", Assert.IsType<InvalidOperationException>(update.InnerException).Message);
        Assert.Equal(0, await database.CountRowsAsync());
    }

    [Fact]
    public async Task ATransactionTheContextDidNotBeginReceivesNoSubject()
    {
        // Refusing it belongs to the unit of work (issue #212); until then its commands run with no
        // subject, which row level security reads as NULL.
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build(database, s => s.AddSingleton<IUnitOfWorkSubject>(new FixedSubject(Subject)));
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SubjectDbContext>();
        await context.Database.OpenConnectionAsync(Ct);
        await using var external = await context.Database.GetDbConnection().BeginTransactionAsync(Ct);
        await context.Database.UseTransactionAsync(external, Ct);

        var seen = (await context.Database
            .SqlQueryRaw<string?>("select nullif(current_setting('app.user_id', true), '') as \"Value\"")
            .ToListAsync(Ct)).Single();

        Assert.Null(seen);
    }

    [Theory]
    [InlineData(Begin.BeginTransaction)]
    [InlineData(Begin.BeginTransactionAsync)]
    [InlineData(Begin.UnitOfWorkExecuteAsync)]
    public async Task TheFirstCommandOfATransactionSetsTheSubjectAsAParameter(Begin begin)
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build<CountedSubjectDbContext>(
            database.ConnectionString, s => s.AddSingleton<IUnitOfWorkSubject>(new FixedSubject(Subject)));
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CountedSubjectDbContext>();
        var probe = Probe.Start();

        var seen = await ReadSubjectInATransactionAsync(scope.ServiceProvider, context, begin);

        Assert.Equal(Subject.ToString(), seen);
        Assert.Equal(1, probe.TransactionsBegun);
        var commands = probe.Commands;
        Assert.Equal(2, commands.Count);
        var first = commands[0];
        Assert.Equal(SetSubjectAsSent, first.CommandText);
        Assert.Equal([Subject.ToString()], first.Parameters);
        Assert.DoesNotContain(Subject.ToString(), first.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first.ConnectorId, commands[1].ConnectorId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARollbackToASavepointKeepsTheSubject(bool async)
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build(database, s => s.AddSingleton<IUnitOfWorkSubject>(new FixedSubject(Subject)));
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SubjectDbContext>();

        string seen;
        if (async)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Ct);
            await transaction.CreateSavepointAsync("after_begin", Ct);
            await context.Rows.AddAsync(new SubjectRow { Name = "undone" }, Ct);
            await context.SaveChangesAsync(Ct);
            await transaction.RollbackToSavepointAsync("after_begin", Ct);
            seen = await ReadSettingAsync(context);
        }
        else
        {
            using var transaction = context.Database.BeginTransaction();
            transaction.CreateSavepoint("after_begin");
            context.Rows.Add(new SubjectRow { Name = "undone" });
            context.SaveChanges();
            transaction.RollbackToSavepoint("after_begin");
            seen = ReadSetting(context);
        }

        Assert.Equal(Subject.ToString(), seen);
    }

    [Theory]
    [InlineData(Missing.NullSubject)]
    [InlineData(Missing.NullSubjectAsync)]
    [InlineData(Missing.NoPortRegistered)]
    [InlineData(Missing.NoPortRegisteredAsync)]
    public async Task ATransactionStartedWithoutASubjectFailsWithAnError(Missing missing)
    {
        await using var database = await server.CreateDatabaseAsync();
        var port = new MutableSubject();
        await using var services = Build(
            database,
            missing is Missing.NullSubject or Missing.NullSubjectAsync ? s => s.AddSingleton<IUnitOfWorkSubject>(port) : null);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SubjectDbContext>();
        var async = missing is Missing.NullSubjectAsync or Missing.NoPortRegisteredAsync;
        var probe = Probe.Start();

        var thrown = async
            ? await Record.ExceptionAsync(async () => await context.Database.BeginTransactionAsync(Ct))
            : Record.Exception(() => context.Database.BeginTransaction());

        Assert.Contains("without a subject", Assert.IsType<InvalidOperationException>(thrown).Message);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(probe.Commands);

        // The refused transaction was rolled back: once there is a subject, the same context begins
        // another one on the same connection, which a transaction left open would make fail.
        if (missing is Missing.NullSubject or Missing.NullSubjectAsync)
        {
            port.Current = Subject;
            await using var transaction = await context.Database.BeginTransactionAsync(Ct);
            Assert.Equal(Subject.ToString(), await ReadSettingAsync(context));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnOnePooledConnectionTheSubjectDoesNotOutliveItsTransaction(bool commit)
    {
        await using var database = await server.CreateDatabaseAsync();
        // One physical connection, and no reset when it returns to the pool: only the
        // transaction-local setting keeps the subject from reaching the next transaction.
        var connectionString = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            MaxPoolSize = 1,
            NoResetOnClose = true,
        }.ConnectionString;
        await using var services = Build(connectionString, s => s.AddSingleton<IUnitOfWorkSubject>(new FixedSubject(Subject)));
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SubjectDbContext>();

        await context.Database.OpenConnectionAsync(Ct);
        var backend = await BackendPidAsync(context.Database.GetDbConnection());
        await using (var transaction = await context.Database.BeginTransactionAsync(Ct))
        {
            Assert.Equal(Subject.ToString(), await ReadSettingAsync(context));
            if (commit)
                await transaction.CommitAsync(Ct);
            else
                await transaction.RollbackAsync(Ct);
        }

        await context.Database.CloseConnectionAsync();
        await context.Database.OpenConnectionAsync(Ct);
        var connection = context.Database.GetDbConnection();
        Assert.Equal(backend, await BackendPidAsync(connection));

        // The next transaction, begun on the connection itself so that no subject is set first.
        await using var next = await connection.BeginTransactionAsync(Ct);
        await using var read = connection.CreateCommand();
        read.Transaction = next;
        read.CommandText = "select current_setting('app.user_id', true), nullif(current_setting('app.user_id', true), '')";
        await using var reader = await read.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        // The setting exists in the session, as an empty string, and nullif reads it as NULL.
        Assert.Equal(string.Empty, reader.GetString(0));
        Assert.True(await reader.IsDBNullAsync(1, Ct));
    }

    [Fact]
    public async Task OnAPooledContextEachTransactionReadsTheSubjectAgain()
    {
        await using var database = await server.CreateDatabaseAsync();
        var port = new MutableSubject();
        await using var services = Build(
            database.ConnectionString,
            s => s.AddSingleton<IUnitOfWorkSubject>(port),
            new ProviderOptions { PoolSize = 2 });
        var other = Guid.Parse("0193f3c1-7c2a-7d4e-9a51-3b1f6c2d8e02");

        SubjectDbContext firstLease;
        await using (var first = services.CreateAsyncScope())
        {
            firstLease = first.ServiceProvider.GetRequiredService<SubjectDbContext>();
            port.Current = Subject;
            await using var transaction = await firstLease.Database.BeginTransactionAsync(Ct);
            Assert.Equal(Subject.ToString(), await ReadSettingAsync(firstLease));
        }

        await using var next = services.CreateAsyncScope();
        var context = next.ServiceProvider.GetRequiredService<SubjectDbContext>();
        Assert.Same(firstLease, context);
        port.Current = other;
        await using (var transaction = await context.Database.BeginTransactionAsync(Ct))
            Assert.Equal(other.ToString(), await ReadSettingAsync(context));
    }

    [Fact]
    public async Task EachScopeSetsItsOwnSubject()
    {
        await using var database = await server.CreateDatabaseAsync();
        await using var services = Build(database, s => s.AddScoped<MutableSubject>().AddScoped<IUnitOfWorkSubject>(p => p.GetRequiredService<MutableSubject>()));
        var other = Guid.Parse("0193f3c1-7c2a-7d4e-9a51-3b1f6c2d8e02");

        await using var first = services.CreateAsyncScope();
        await using var second = services.CreateAsyncScope();
        first.ServiceProvider.GetRequiredService<MutableSubject>().Current = Subject;
        second.ServiceProvider.GetRequiredService<MutableSubject>().Current = other;
        var firstContext = first.ServiceProvider.GetRequiredService<SubjectDbContext>();
        var secondContext = second.ServiceProvider.GetRequiredService<SubjectDbContext>();

        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync(Ct);
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync(Ct);

        Assert.Equal(Subject.ToString(), await ReadSettingAsync(firstContext));
        Assert.Equal(other.ToString(), await ReadSettingAsync(secondContext));
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task WithAPoolSizeASubjectPortThatIsNotASingletonIsRefused(ServiceLifetime lifetime)
    {
        // Scopes not validated, so that the refusal is the provider's, not the container's.
        var services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(IUnitOfWorkSubject), _ => new FixedSubject(Subject), lifetime));
        Register<SubjectDbContext>(services, "Host=localhost;Database=unused;Username=unused;Password=unused", new ProviderOptions { PoolSize = 2 });
        await using var root = services.BuildServiceProvider(validateScopes: false);
        await using var scope = root.CreateAsyncScope();

        var thrown = Record.Exception(() => scope.ServiceProvider.GetRequiredService<SubjectDbContext>());

        var refusal = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains(nameof(IUnitOfWorkSubject), refusal.Message);
        Assert.Contains(lifetime.ToString(), refusal.Message);
    }

    private static ServiceProvider Build(TestDatabase database, Action<IServiceCollection>? port) =>
        Build<SubjectDbContext>(database.ConnectionString, port);

    private static ServiceProvider Build(string connectionString, Action<IServiceCollection>? port, ProviderOptions? options = null) =>
        Build<SubjectDbContext>(connectionString, port, options);

    private static ServiceProvider Build<TContext>(
        string connectionString, Action<IServiceCollection>? port, ProviderOptions? options = null)
        where TContext : SubjectDbContext
    {
        var services = new ServiceCollection();
        port?.Invoke(services);
        Register<TContext>(services, connectionString, options);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static void Register<TContext>(IServiceCollection services, string connectionString, ProviderOptions? options)
        where TContext : SubjectDbContext =>
        services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef =>
        {
            ef.UsePostgreSQL<TContext>(connectionString, Probe.NpgsqlOptions, options);
            ef.AddUnitOfWork<TContext>();
        }));

    private static async Task RunAsync(SubjectDbContext context, Refused command)
    {
        switch (command)
        {
            case Refused.Query:
                _ = context.Rows.ToList();
                break;
            case Refused.QueryAsync:
                _ = await context.Rows.ToListAsync(Ct);
                break;
            case Refused.SaveChangesOfTwoRows:
                context.Rows.AddRange(new SubjectRow { Name = "one" }, new SubjectRow { Name = "two" });
                context.SaveChanges();
                break;
            case Refused.SaveChangesOfTwoRowsAsync:
                await context.Rows.AddRangeAsync([new SubjectRow { Name = "one" }, new SubjectRow { Name = "two" }], Ct);
                await context.SaveChangesAsync(Ct);
                break;
            case Refused.RawInsert:
                context.Database.ExecuteSql($"insert into subject_rows (\"Id\", \"Name\") values ({Guid.NewGuid()}, 'raw')");
                break;
            case Refused.RawInsertAsync:
                await context.Database.ExecuteSqlAsync($"insert into subject_rows (\"Id\", \"Name\") values ({Guid.NewGuid()}, 'raw')", Ct);
                break;
            // EF Core sends this through ExecuteScalar: the interceptor's scalar callbacks.
            case Refused.Scalar:
                _ = context.GetService<IRelationalDatabaseCreator>().HasTables();
                break;
            case Refused.ScalarAsync:
                _ = await context.GetService<IRelationalDatabaseCreator>().HasTablesAsync(Ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private static async Task<string> ReadSubjectInATransactionAsync(IServiceProvider scope, SubjectDbContext context, Begin begin)
    {
        switch (begin)
        {
            case Begin.BeginTransaction:
            {
                using var transaction = context.Database.BeginTransaction();
                var seen = ReadSetting(context);
                transaction.Commit();
                return seen;
            }

            case Begin.BeginTransactionAsync:
            {
                await using var transaction = await context.Database.BeginTransactionAsync(Ct);
                var seen = await ReadSettingAsync(context);
                await transaction.CommitAsync(Ct);
                return seen;
            }

            case Begin.UnitOfWorkExecuteAsync:
                return await scope.GetRequiredService<ITransactionalContext>().ExecuteAsync(
                    static (c, _) => ReadSettingAsync(c),
                    context,
                    Ct);

            default:
                throw new ArgumentOutOfRangeException(nameof(begin), begin, null);
        }
    }

    private static string ReadSetting(SubjectDbContext context) =>
        context.Database.SqlQueryRaw<string>("select current_setting('app.user_id') as \"Value\"").AsEnumerable().Single();

    private static async Task<string> ReadSettingAsync(SubjectDbContext context) =>
        (await context.Database.SqlQueryRaw<string>("select current_setting('app.user_id') as \"Value\"").ToListAsync(Ct)).Single();

    // A command sent on the ADO.NET connection directly: EF's interceptors do not see it.
    private static async Task<int> BackendPidAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select pg_backend_pid()";
        return (int)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static Exception Present(Exception? exception)
    {
        Assert.NotNull(exception);
        return exception;
    }

    private sealed class FixedSubject(Guid? subject) : IUnitOfWorkSubject
    {
        public Guid? Current => subject;
    }

    private sealed class MutableSubject : IUnitOfWorkSubject
    {
        public Guid? Current { get; set; }
    }
}

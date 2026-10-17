using Npgsql;
using SaasBtp.Testing;
using Testcontainers.PostgreSql;

namespace SaasBtp.Migrations.IntegrationTests;

public sealed class RunnerTimeoutTests
{
    private const int ConnectionStringCommandTimeoutSeconds = 2;

    // Longer than the connection string's command timeout, once the runner is seen waiting.
    private static readonly TimeSpan LockHeldWhileWaiting = TimeSpan.FromSeconds(ConnectionStringCommandTimeoutSeconds + 3);

    [Fact]
    public async Task TheRunnerWaitsLongerThanTheCommandTimeoutOfItsConnectionString()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using PostgreSqlContainer postgres = PostgresImage.CreateBuilder().Build();
        await postgres.StartAsync(cancellationToken);
        var firstRun = await Runner.RunAsync(postgres.GetConnectionString(), cancellationToken);
        Assert.Equal(0, firstRun.ExitCode);

        // The second run reads the journal while another session holds it locked for longer than
        // the connection string allows a command to wait.
        await using var locker = new NpgsqlConnection(postgres.GetConnectionString());
        await locker.OpenAsync(cancellationToken);
        await using var transaction = await locker.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("lock table dbup.schemaversions in access exclusive mode", locker, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
            {
                CommandTimeout = ConnectionStringCommandTimeoutSeconds,
            }
            .ConnectionString;
        var secondRun = Runner.RunAsync(connectionString, cancellationToken);
        await WaitUntilTheJournalLockIsAwaitedAsync(postgres.GetConnectionString(), secondRun, cancellationToken);
        await Task.Delay(LockHeldWhileWaiting, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var result = await secondRun;

        Assert.True(result.ExitCode == 0, result.Output);
    }

    /// <summary>
    /// Returns once another session waits for a lock on the journal, so that the time the lock is
    /// then held is time the runner spends waiting, whatever its start-up took.
    /// </summary>
    private static async Task WaitUntilTheJournalLockIsAwaitedAsync(
        string connectionString, Task<RunnerResult> run, CancellationToken cancellationToken)
    {
        const string sql = "select count(*) from pg_locks where not granted and relation = 'dbup.schemaversions'::regclass";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        while ((long)(await command.ExecuteScalarAsync(cancellationToken))! == 0)
        {
            if (run.IsCompleted)
            {
                Assert.Fail($"The runner ended before waiting for the journal: {(await run).Output}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }
}

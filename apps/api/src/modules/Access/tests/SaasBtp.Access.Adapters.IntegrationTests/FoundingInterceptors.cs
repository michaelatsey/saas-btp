using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// Holds the first transaction that reaches its commit until the test releases it, and gives the
/// test the backend that holds it, so that a second submission can be seen waiting on a key the
/// first holds (contract.md §4). Every later commit passes.
/// </summary>
internal sealed class CommitGate : DbTransactionInterceptor
{
    private readonly TaskCompletionSource<int> _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _taken;

    /// <summary>The process id of the backend whose commit is held, once a commit is held.</summary>
    public Task<int> Held => _held.Task;

    public void Release() => _released.TrySetResult();

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _taken, 1) == 0)
        {
            _held.TrySetResult(((NpgsqlConnection)transaction.Connection!).ProcessID);
            await _released.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Waits until a backend is blocked by <paramref name="holder"/>, read past row level security in
    /// <c>pg_blocking_pids</c>; fails after a bound.
    /// </summary>
    public static async Task WaitUntilBlockedByAsync(AccessDatabase database, int holder)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await database.CountAsSuperuserAsync(
            "select count(*) from pg_stat_activity where @holder = any(pg_blocking_pids(pid))", ("holder", holder)) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "No submission waited on a key the first one holds.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>
/// Makes another transaction commit the founder's profile first: just before the unit of work
/// inserts the founder's profile, a transaction of its own, as <c>saasbtp_api</c> under the same
/// person, inserts and commits one, so that the unit of work's insert conflicts on the profile's
/// unique key (contract.md §4). It also counts the transactions the module begins and the profile
/// reads and inserts it sends.
/// </summary>
/// <param name="database">The database the conflicting profile is committed to.</param>
/// <param name="user">The founder.</param>
/// <param name="persistent">
/// When true, the conflict is made on every attempt: before each profile read, the conflicting
/// profiles made so far are removed (as the container's superuser), so that every attempt reads
/// no profile and inserts one. When false, only the first attempt meets it.
/// </param>
internal sealed partial class ProfileConflict(AccessDatabase database, Guid user, bool persistent) : IDbCommandInterceptor, IDbTransactionInterceptor
{
    private readonly Lock _gate = new();

    private readonly List<Guid> _committed = [];

    private int _begun;

    private int _reads;

    private int _inserts;

    /// <summary>The transactions the module began.</summary>
    public int Begun => Volatile.Read(ref _begun);

    /// <summary>The profile reads the module sent.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>The profile inserts the module sent.</summary>
    public int Inserts => Volatile.Read(ref _inserts);

    /// <summary>The profiles the other transaction committed, in order.</summary>
    public IReadOnlyList<Guid> Committed
    {
        get
        {
            lock (_gate)
            {
                return [.. _committed];
            }
        }
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _begun);
        return ValueTask.FromResult(result);
    }

    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await BeforeAsync(command.CommandText);
        return result;
    }

    public async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await BeforeAsync(command.CommandText);
        return result;
    }

    private async Task BeforeAsync(string commandText)
    {
        if (ProfileRead().IsMatch(commandText))
        {
            var attempt = Interlocked.Increment(ref _reads);
            if (persistent && attempt > 1)
            {
                await database.AsSuperuserAsync("delete from access.profiles where id = any(@ids)", ("ids", Committed.ToArray()));
            }
        }
        else if (ProfileInsert().IsMatch(commandText))
        {
            var attempt = Interlocked.Increment(ref _inserts);
            if (persistent || attempt == 1)
            {
                var profile = Guid.CreateVersion7();
                await using var other = await database.AsAsync(user);
                await other.InsertProfileAsync(profile, user);
                await other.CommitAsync();
                lock (_gate)
                {
                    _committed.Add(profile);
                }
            }
        }
    }

    [GeneratedRegex("""^\s*SELECT\b.*\bFROM "?access"?\."?profiles"?""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ProfileRead();

    [GeneratedRegex("""^\s*INSERT INTO "?access"?\."?profiles"?""", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileInsert();
}

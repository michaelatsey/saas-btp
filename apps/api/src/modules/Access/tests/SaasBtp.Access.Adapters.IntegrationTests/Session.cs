using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// One transaction as <c>saasbtp_api</c> whose first statement sets the subject with a
/// transaction-local <c>set_config</c>, parameterized, as the API does (ADR-ARCH-032 §3,
/// ADR-ARCH-033 §4). Disposing it rolls back what was not committed.
/// </summary>
internal sealed class Session : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;

    private readonly bool _ownsConnection;

    private Session(NpgsqlConnection connection, NpgsqlTransaction transaction, bool ownsConnection)
    {
        _connection = connection;
        Transaction = transaction;
        _ownsConnection = ownsConnection;
    }

    private NpgsqlTransaction Transaction { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Opens a connection of its own and starts the transaction on it.</summary>
    public static async Task<Session> BeginAsync(string connectionString, Guid? subject) =>
        await BeginAsync(connectionString, subject?.ToString());

    /// <summary>As <see cref="BeginAsync(string, Guid?)"/>, with a subject that need not be a uuid.</summary>
    public static Task<Session> BeginRawAsync(string connectionString, string subject) =>
        BeginAsync(connectionString, subject);

    /// <summary>
    /// Starts the transaction on a connection the caller keeps open, so that successive sessions
    /// share one server session, as Supavisor's session mode reuses one (ADR-ARCH-028 §4).
    /// </summary>
    public static Task<Session> OnAsync(NpgsqlConnection connection, Guid? subject) =>
        StartAsync(connection, subject?.ToString(), ownsConnection: false);

    /// <summary>The SQLSTATE of the error <paramref name="statement"/> raises in a session as <paramref name="subject"/>.</summary>
    public static async Task<string> RefusalAsync(string connectionString, Guid subject, Func<Session, Task> statement)
    {
        await using var session = await BeginAsync(connectionString, subject);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => statement(session));
        return refusal.SqlState;
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteNonQueryAsync(Token);
    }

    /// <summary>
    /// The first column of the first row. A SQL null is returned only to a caller that asks for a
    /// nullable type, so that "null" and "false" (or the nil uuid) are never mistaken for each other.
    /// </summary>
    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        var value = await command.ExecuteScalarAsync(Token);
        if (value is null or DBNull)
        {
            return default(T) is null
                ? default!
                : throw new InvalidOperationException($"The statement returned null, read as {typeof(T).Name}: {sql}");
        }

        return (T)value;
    }

    public Task<long> CountAsync(string sql, params (string Name, object? Value)[] parameters) =>
        ScalarAsync<long>(sql, parameters);

    public Task<bool> MayActInAsync(Guid workspaceId) =>
        ScalarAsync<bool>("select access.may_act_in(@id)", ("id", workspaceId));

    public Task CommitAsync() => Transaction.CommitAsync(Token);

    public Task RollbackAsync() => Transaction.RollbackAsync(Token);

    public Task InsertProfileAsync(Guid id, Guid userId, bool returning = true) =>
        InsertAsync("profiles", returning, ("id", id), ("user_id", userId));

    public Task InsertOwnershipAsync(Guid id, Guid? profileId, Guid organizationId, bool returning = true) =>
        InsertAsync("ownerships", returning, ("id", id), ("profile_id", profileId), ("organization_id", organizationId));

    public Task InsertOrganizationAsync(
        Guid id, string name, Guid currentOwnershipId, bool returning = true, string status = "declared") =>
        InsertAsync(
            "organizations", returning,
            ("id", id), ("name", name), ("status", status), ("current_ownership_id", currentOwnershipId));

    public Task InsertMembershipAsync(Guid id, Guid? profileId, Guid organizationId, bool returning = true) =>
        InsertAsync("memberships", returning, ("id", id), ("profile_id", profileId), ("organization_id", organizationId));

    public Task InsertAccessAsync(Guid id, Guid? profileId, Guid workspaceId, Guid organizationId, bool returning = true) =>
        InsertAsync(
            "workspace_accesses", returning,
            ("id", id), ("profile_id", profileId), ("workspace_id", workspaceId), ("organization_id", organizationId));

    public Task InsertWorkspaceAsync(Guid id, Guid organizationId, string name, bool returning = true) =>
        InsertAsync("workspaces", returning, ("id", id), ("organization_id", organizationId), ("name", name));

    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        if (_ownsConnection)
        {
            await _connection.DisposeAsync();
        }
    }

    private static async Task<Session> BeginAsync(string connectionString, string? subject)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(Token);
            return await StartAsync(connection, subject, ownsConnection: true);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<Session> StartAsync(NpgsqlConnection connection, string? subject, bool ownsConnection)
    {
        var transaction = await connection.BeginTransactionAsync(Token);
        var session = new Session(connection, transaction, ownsConnection);
        if (subject is not null)
        {
            await session.ExecuteAsync("select set_config('app.user_id', @subject, true)", ("subject", subject));
        }

        return session;
    }

    /// <summary>
    /// Inserts one row with the audit and version columns every Access table carries (sql.md),
    /// reading it back with <c>RETURNING</c> when <paramref name="returning"/> is true, as EF does
    /// (ADR-ARCH-033 §5).
    /// </summary>
    private Task<int> InsertAsync(string table, bool returning, params (string Name, object? Value)[] values)
    {
        (string Name, object? Value)[] columns =
        [
            .. values,
            ("created_at", DateTimeOffset.UtcNow),
            ("created_by", "integration-test"),
            ("aggregate_version", 1L),
        ];
        var names = string.Join(", ", columns.Select(column => column.Name));
        var placeholders = string.Join(", ", columns.Select(column => "@" + column.Name));
        var sql = $"insert into access.{table} ({names}) values ({placeholders}){(returning ? " returning *" : string.Empty)}";

        return ExecuteAsync(sql, columns);
    }

    private NpgsqlCommand Command(string sql, (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, _connection, Transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}

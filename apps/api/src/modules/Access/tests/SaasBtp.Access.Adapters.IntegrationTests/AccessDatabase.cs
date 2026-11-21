using Npgsql;
using SaasBtp.Access.Adapters.IntegrationTests;
using SaasBtp.Testing;

[assembly: AssemblyFixture(typeof(AccessDatabase))]

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// One database migrated by the runner for the whole assembly. Every test creates its own people
/// and records under fresh identifiers, so tests share it without seeing one another's data
/// except where a test means to.
/// </summary>
public sealed class AccessDatabase : IAsyncLifetime
{
    private MigratedDatabase? _migrated;

    private RolesDatabase Database =>
        _migrated?.Database ?? throw new InvalidOperationException("The database is not started.");

    internal string Api => Database.ApiConnectionString;

    internal string Migrator => Database.MigratorConnectionString;

    internal string Superuser => Database.SuperuserConnectionString;

    public async ValueTask InitializeAsync() =>
        _migrated = await MigratedDatabase.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _migrated?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>A transaction as <c>saasbtp_api</c> on a connection of its own, its subject set first.</summary>
    internal Task<Session> AsAsync(Guid? subject) => Session.BeginAsync(Api, subject);

    /// <summary>A transaction as <c>saasbtp_api</c> whose subject is set to a raw value.</summary>
    internal Task<Session> AsRawAsync(string subject) => Session.BeginRawAsync(Api, subject);

    /// <summary>Runs one statement as the container's superuser, which bypasses row level security.</summary>
    internal async Task<int> AsSuperuserAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(Superuser);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Counts rows as the container's superuser, to confirm that the rows a test reads past exist.</summary>
    internal async Task<long> CountAsSuperuserAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(Superuser);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

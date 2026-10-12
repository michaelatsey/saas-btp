using Npgsql;
using SaasBtp.Testing;
using Testcontainers.PostgreSql;

namespace SaasBtp.Migrations.IntegrationTests;

/// <summary>
/// Two consecutive executions of the runner against a PostgreSQL that starts empty, with what
/// each schema held before and after them.
/// </summary>
public sealed class EmptyDatabaseRun : IAsyncLifetime
{
    internal const string JournalTable = "relation:schemaversions";

    private readonly PostgreSqlContainer _postgres = PostgresImage.CreateBuilder().Build();

    internal RunnerResult FirstRun { get; private set; } = null!;

    internal RunnerResult SecondRun { get; private set; } = new(ExitCode: -1, Output: "The second run did not take place.");

    internal IReadOnlyList<string> PublicBefore { get; private set; } = [];

    internal IReadOnlyList<string> PublicAfter { get; private set; } = ["The runs did not complete."];

    internal IReadOnlyList<string> JournalSchemaAfterFirstRun { get; private set; } = [];

    internal IReadOnlyList<string> JournalSchemaAfterSecondRun { get; private set; } = [];

    internal long JournalEntriesAfterFirstRun { get; private set; } = -1;

    internal long JournalEntriesAfterSecondRun { get; private set; } = -2;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _postgres.StartAsync(cancellationToken);
        var connectionString = _postgres.GetConnectionString();

        PublicBefore = await ObjectsInSchemaAsync(connectionString, "public", cancellationToken);

        FirstRun = await Runner.RunAsync(connectionString, cancellationToken);
        JournalSchemaAfterFirstRun = await ObjectsInSchemaAsync(connectionString, "dbup", cancellationToken);
        if (!JournalSchemaAfterFirstRun.Contains(JournalTable))
        {
            // No journal to count: the tests fail on the properties left at their initial values.
            return;
        }

        JournalEntriesAfterFirstRun = await CountJournalEntriesAsync(connectionString, cancellationToken);

        SecondRun = await Runner.RunAsync(connectionString, cancellationToken);
        JournalSchemaAfterSecondRun = await ObjectsInSchemaAsync(connectionString, "dbup", cancellationToken);
        JournalEntriesAfterSecondRun = await CountJournalEntriesAsync(connectionString, cancellationToken);
        PublicAfter = await ObjectsInSchemaAsync(connectionString, "public", cancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    /// <summary>Every relation, function and type of a schema, as <c>kind:name</c>.</summary>
    private static async Task<IReadOnlyList<string>> ObjectsInSchemaAsync(
        string connectionString, string schema, CancellationToken cancellationToken)
    {
        const string sql = """
            select 'relation:' || c.relname from pg_class c
              join pg_namespace n on n.oid = c.relnamespace where n.nspname = @schema
            union all
            select 'function:' || p.proname from pg_proc p
              join pg_namespace n on n.oid = p.pronamespace where n.nspname = @schema
            union all
            select 'type:' || t.typname from pg_type t
              join pg_namespace n on n.oid = t.typnamespace where n.nspname = @schema
            order by 1
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);

        var objects = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            objects.Add(reader.GetString(0));
        }

        return objects;
    }

    private static async Task<long> CountJournalEntriesAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select count(*) from dbup.schemaversions", connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

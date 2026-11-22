using System.Reflection;
using Npgsql;
using SaasBtp.Testing;

namespace SaasBtp.Migrations.IntegrationTests;

/// <summary>
/// Two consecutive executions of the runner, as <c>saasbtp_migrator</c>, against a PostgreSQL that
/// holds only the roles of <c>roles.sql</c> (ADR-ARCH-032, Consequences), with what each schema held
/// before and after them. Observations are made as the container's superuser.
/// </summary>
public sealed class EmptyDatabaseRun : IAsyncLifetime
{
    internal const string JournalTable = "relation:schemaversions";

    private RolesDatabase? _database;

    internal RunnerResult FirstRun { get; private set; } = null!;

    internal RunnerResult SecondRun { get; private set; } = new(ExitCode: -1, Output: "The second run did not take place.");

    internal IReadOnlyList<string> PublicBefore { get; private set; } = [];

    internal IReadOnlyList<string> PublicAfter { get; private set; } = ["The runs did not complete."];

    internal IReadOnlyList<string> JournalSchemaAfterFirstRun { get; private set; } = [];

    internal IReadOnlyList<string> JournalSchemaAfterSecondRun { get; private set; } = [];

    internal long JournalEntriesAfterFirstRun { get; private set; } = -1;

    internal long JournalEntriesAfterSecondRun { get; private set; } = -2;

    internal IReadOnlyList<string> AccessSchemaAfterFirstRun { get; private set; } = [];

    /// <summary>The names the journal holds after the first run, as DbUp records them.</summary>
    internal IReadOnlyList<string> JournaledScriptsAfterFirstRun { get; private set; } = [];

    /// <summary>The scripts the runner carries, embedded in its assembly (ADR-ARCH-019).</summary>
    internal static IReadOnlyList<string> EmbeddedScripts { get; } = Assembly.Load(new AssemblyName("SaasBtp.Migrations"))
        .GetManifestResourceNames()
        .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal)
        .ToList();

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _database = await RolesDatabase.StartAsync(cancellationToken);
        var connectionString = _database.SuperuserConnectionString;

        PublicBefore = await ObjectsInSchemaAsync(connectionString, "public", cancellationToken);

        FirstRun = await _database.MigrateAsync(cancellationToken);
        JournalSchemaAfterFirstRun = await ObjectsInSchemaAsync(connectionString, "dbup", cancellationToken);
        AccessSchemaAfterFirstRun = await ObjectsInSchemaAsync(connectionString, "access", cancellationToken);
        if (!JournalSchemaAfterFirstRun.Contains(JournalTable))
        {
            // No journal to count: the tests fail on the properties left at their initial values.
            return;
        }

        JournalEntriesAfterFirstRun = await CountJournalEntriesAsync(connectionString, cancellationToken);
        JournaledScriptsAfterFirstRun = await JournaledScriptsAsync(connectionString, cancellationToken);

        SecondRun = await _database.MigrateAsync(cancellationToken);
        JournalSchemaAfterSecondRun = await ObjectsInSchemaAsync(connectionString, "dbup", cancellationToken);
        JournalEntriesAfterSecondRun = await CountJournalEntriesAsync(connectionString, cancellationToken);
        PublicAfter = await ObjectsInSchemaAsync(connectionString, "public", cancellationToken);
    }

    public ValueTask DisposeAsync() => _database?.DisposeAsync() ?? ValueTask.CompletedTask;

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

    private static async Task<IReadOnlyList<string>> JournaledScriptsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select scriptname from dbup.schemaversions order by scriptname", connection);

        var scripts = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            scripts.Add(reader.GetString(0));
        }

        return scripts;
    }

    private static async Task<long> CountJournalEntriesAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select count(*) from dbup.schemaversions", connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

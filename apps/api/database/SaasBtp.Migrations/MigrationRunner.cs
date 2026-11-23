using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Engine.Transactions;
using DbUp.Postgresql;
using Npgsql;

namespace SaasBtp.Migrations;

/// <summary>
/// Applies the embedded SQL scripts with DbUp (ADR-ARCH-019). Each deployment runs it as a
/// separate step, before the application starts.
/// </summary>
internal static class MigrationRunner
{
    public const string ConnectionStringVariable = "ConnectionStrings__Migrations";

    public const int ExitCodeSuccess = 0;

    public const int ExitCodeFailure = 1;

    // The journal lives outside "public", which the Supabase Data API exposes by default.
    private const string JournalSchema = "dbup";

    private const string JournalTable = "schemaversions";

    // How long one command may run, journal commands included, whatever the connection string
    // says. Above the role's statement_timeout of 110 seconds (infra/supabase/roles.sql,
    // ADR-ARCH-027), so that the server cancels a long statement first and reports why.
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);

    /// <summary>The argument that excludes one script, followed by its file name.</summary>
    public const string ExcludeArgument = "--exclude";

    // The prefix of an embedded script's resource name, which DbUp uses as the script's name and
    // journals: the assembly's root namespace, then the Scripts folder.
    private const string ScriptResourcePrefix = "SaasBtp.Migrations.Scripts.";

    /// <summary>
    /// Reads the scripts to exclude from the command-line arguments. The only argument is
    /// <c>--exclude &lt;file name&gt;</c>, repeatable; each file name must be exactly, case included,
    /// the file name of a script the runner carries.
    /// </summary>
    /// <param name="arguments">The process's command-line arguments.</param>
    /// <param name="excluded">The resource names of the excluded scripts, when the arguments are valid.</param>
    /// <param name="refusal">Why the arguments are refused, when they are not valid.</param>
    /// <returns>True when every argument is valid.</returns>
    public static bool TryReadExclusions(string[] arguments, out IReadOnlySet<string> excluded, out string refusal)
    {
        var scripts = typeof(MigrationRunner).Assembly.GetManifestResourceNames()
            .Where(IsScript)
            .ToHashSet(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        excluded = names;
        refusal = string.Empty;

        for (var index = 0; index < arguments.Length; index++)
        {
            if (!string.Equals(arguments[index], ExcludeArgument, StringComparison.Ordinal))
            {
                refusal = $"Unknown argument: {arguments[index]}. The only argument is {ExcludeArgument} <script file name>.";

                return false;
            }

            if (index + 1 == arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                refusal = $"{ExcludeArgument} needs the file name of a script, such as 0001__access__create_tables.sql.";

                return false;
            }

            var fileName = arguments[++index];
            if (!scripts.Contains(ScriptResourcePrefix + fileName))
            {
                refusal = $"{ExcludeArgument} names no script of the runner: {fileName}";

                return false;
            }

            names.Add(ScriptResourcePrefix + fileName);
        }

        return true;
    }

    /// <summary>
    /// Runs every script the journal does not hold yet, in the order of their names, except the
    /// excluded ones: those are neither applied nor journaled, so a later run without the exclusion
    /// applies them.
    /// </summary>
    /// <param name="connectionString">The runner's connection string.</param>
    /// <param name="excluded">The resource names of the scripts to exclude (<see cref="TryReadExclusions"/>).</param>
    /// <returns>The process exit code: zero only when every pending script was applied.</returns>
    public static int Run(string connectionString, IReadOnlySet<string> excluded)
    {
        if (!IsValid(connectionString))
        {
            Console.Error.WriteLine(
                $"Migration failed: the value of {ConnectionStringVariable} is not an Npgsql connection string "
                + "(keyword=value pairs separated by semicolons, not a URI).");

            return ExitCodeFailure;
        }

        try
        {
            IUpgradeLog log = new ConsoleUpgradeLog();
            IConnectionManager connectionManager = new PostgresqlConnectionManager(connectionString);
            IJournal journal = new PostgresqlTableJournal(() => connectionManager, () => log, JournalSchema, JournalTable);

            // No EnsureDatabase: the runner never creates the database it migrates.
            var upgrader = DeployChanges.To
                .PostgresqlDatabase(connectionManager)
                .JournalTo(journal)
                .WithScriptsEmbeddedInAssembly(
                    typeof(MigrationRunner).Assembly,
                    resourceName => IsScript(resourceName) && !excluded.Contains(resourceName))
                .WithTransactionPerScript()
                .WithExecutionTimeout(CommandTimeout)
                .WithVariablesDisabled()
                .LogTo(log)
                .Build();

            // Never silent: each excluded script is named before anything runs.
            foreach (var resourceName in excluded.Order(StringComparer.Ordinal))
            {
                Console.WriteLine($"Excluded, not applied: {resourceName[ScriptResourcePrefix.Length..]}");
            }

            EnsureJournal(connectionManager, journal, log);

            var result = upgrader.PerformUpgrade();
            if (!result.Successful)
            {
                // DbUp has already logged the failing script and its error.
                return ExitCodeFailure;
            }

            return ExitCodeSuccess;
        }
        catch (Exception exception)
        {
            // The driver often wraps the cause: the whole chain is what tells a timeout from a
            // refused connection or a missing privilege.
            for (var current = exception; current is not null; current = current.InnerException)
            {
                Console.Error.WriteLine($"Migration failed: {current.GetType().Name}: {current.Message}");
            }

            return ExitCodeFailure;
        }
    }

    private static bool IsScript(string resourceName) =>
        resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses the connection string before anything else uses it. The driver quotes what it
    /// cannot read in the message of its exception, credentials included: that message is
    /// never printed.
    /// </summary>
    private static bool IsValid(string connectionString)
    {
        try
        {
            _ = new NpgsqlConnectionStringBuilder(connectionString);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the journal schema and the journal table when they are absent. DbUp creates the
    /// table only when it records its first script, and never the schema: without this step, a
    /// run that finds no script to apply would leave no journal behind.
    /// </summary>
    private static void EnsureJournal(IConnectionManager connectionManager, IJournal journal, IUpgradeLog log)
    {
        using (connectionManager.OperationStarting(log, []))
        {
            connectionManager.ExecuteCommandsWithManagedConnection(commandFactory =>
            {
                using (var command = commandFactory())
                {
                    command.CommandText = $"create schema if not exists {JournalSchema}";
                    command.ExecuteNonQuery();
                }

                journal.EnsureTableExistsAndIsLatestVersion(commandFactory);
            });
        }
    }
}

using System.Reflection;
using DbUp;
using DbUp.Engine;

namespace SaasBtp.Database.Migrator;

/// <summary>
/// Applies the embedded PostgreSQL DDL scripts via DbUp.
/// </summary>
/// <remarks>
/// Single source of schema truth (ADR-ARCH-006): both the console entry point and the anti-drift
/// integration test call this method, so the schema the tests assert against is byte-for-byte the
/// schema deployment applies. Exposed as a public static so the test can reuse it without
/// duplicating the DbUp configuration.
/// </remarks>
public static class MigrationRunner
{
    /// <summary>
    /// Runs all pending embedded scripts against <paramref name="connectionString"/> in a single
    /// journaled history, ordered globally by the <c>NNNN_</c> filename prefix.
    /// </summary>
    /// <param name="connectionString">
    /// A privileged, DIRECT PostgreSQL connection (owner role, not PostgREST): required for DDL and
    /// RLS changes, and it bypasses RLS by design (ADR-ARCH-005 / sql.md §RLS).
    /// </param>
    /// <returns>The DbUp upgrade result (inspect <see cref="DatabaseUpgradeResult.Successful"/>).</returns>
    public static DatabaseUpgradeResult Run(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            // Order is the embedded-resource name; the NNNN_ prefix fixes one global order across
            // bounded contexts in a single flat journal (ADR-ARCH-006 script convention). One
            // journal table only — DbUp's default (schemaversions) — no per-module journals.
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                static resourceName => resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            // DDL is transactional in PostgreSQL; per-script transactions keep a failed script from
            // leaving a half-applied schema.
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        return upgrader.PerformUpgrade();
    }
}

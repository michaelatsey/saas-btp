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
    /// Embedded-resource-name markers of the AUTH-COUPLED scripts: they create objects on GoTrue's
    /// <c>auth.users</c> or name the <c>supabase_auth_admin</c> role, neither of which exists on a
    /// bare PostgreSQL (conventions/sql.md §Auth-coupled scripts). The deploy console applies every
    /// script; a bare-Postgres (Testcontainer) run must exclude these via <see cref="IsPortableScript"/>.
    /// This is the SINGLE source of truth every Testcontainer harness shares, so a new auth-coupled
    /// script left off this list runs on bare Postgres and fails loudly rather than being skipped silently.
    /// </summary>
    public static readonly IReadOnlyList<string> AuthCoupledScriptMarkers =
    [
        "0003_access_profiles_permissions",
        "0004_access_profiles_trigger",
        "0005_access_profiles_jwt_hook",
    ];

    /// <summary>
    /// True when <paramref name="resourceName"/> is a PORTABLE script (runs on bare Postgres) — i.e.
    /// it matches none of <see cref="AuthCoupledScriptMarkers"/>. Pass as the <c>scriptFilter</c> to
    /// <see cref="Run"/> from a Testcontainer harness so only the portable subset is applied.
    /// </summary>
    public static bool IsPortableScript(string resourceName) =>
        !AuthCoupledScriptMarkers.Any(resourceName.Contains);

    /// <summary>
    /// Runs all pending embedded scripts against <paramref name="connectionString"/> in a single
    /// journaled history, ordered globally by the <c>NNNN_</c> filename prefix.
    /// </summary>
    /// <param name="connectionString">
    /// A privileged, DIRECT PostgreSQL connection (owner role, not PostgREST): required for DDL and
    /// RLS changes, and it bypasses RLS by design (ADR-ARCH-005 / sql.md §RLS).
    /// </param>
    /// <param name="scriptFilter">
    /// Optional extra predicate on the embedded-resource name, ANDed with the <c>.sql</c> filter. The
    /// deploy console passes nothing, so it applies EVERY script. Testcontainer harnesses pass
    /// <see cref="IsPortableScript"/> to run only the portable subset and EXCLUDE the auth-coupled
    /// scripts (<see cref="AuthCoupledScriptMarkers"/>), which reference <c>auth.users</c> /
    /// <c>supabase_auth_admin</c> and cannot run on bare Postgres. <see langword="null"/> = no filter.
    /// </param>
    /// <returns>The DbUp upgrade result (inspect <see cref="DatabaseUpgradeResult.Successful"/>).</returns>
    public static DatabaseUpgradeResult Run(string connectionString, Func<string, bool>? scriptFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            // Order is the embedded-resource name; the NNNN_ prefix fixes one global order across
            // bounded contexts in a single flat journal (ADR-ARCH-006 script convention). One
            // journal table only — DbUp's default (schemaversions) — no per-module journals.
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                resourceName => resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                                && (scriptFilter is null || scriptFilter(resourceName)))
            // DDL is transactional in PostgreSQL; per-script transactions keep a failed script from
            // leaving a half-applied schema.
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        return upgrader.PerformUpgrade();
    }
}

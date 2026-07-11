using SaasBtp.Database.Migrator;

// Connection string comes from the environment (Doppler injects env vars) or the first CLI
// argument — NEVER hard-coded (ADR-ARCH-006). It must use the privileged owner role on a DIRECT
// Postgres connection (not PostgREST): DDL, RLS and future publications require it.
// ConnectionStrings__MigratorPostgres follows the standard .NET "ConnectionStrings:" env mapping.
var connectionString =
    (args.Length > 0 ? args[0] : null)
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__MigratorPostgres")
    ?? Environment.GetEnvironmentVariable("MIGRATOR_DB_CONNECTION");

if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync(
        "No connection string. Set ConnectionStrings__MigratorPostgres (Doppler/env) or pass it "
        + "as the first argument. Use the privileged owner role on a direct Postgres connection "
        + "(never PostgREST).");
    return 2;
}

var result = MigrationRunner.Run(connectionString);

if (!result.Successful)
{
    await Console.Error.WriteLineAsync(
        $"Migration failed on '{result.ErrorScript?.Name}': {result.Error}");
    return 1;
}

Console.WriteLine("Migration complete — all pending scripts applied.");
return 0;

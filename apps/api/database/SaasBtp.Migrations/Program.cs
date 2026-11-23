using SaasBtp.Migrations;

// The arguments are checked first: they need no database. A deployment passes none; the
// Testcontainers runs and the local Migrate step exclude the auth-coupled scripts by name
// (apps/api/CLAUDE.md, Migrate).
if (!MigrationRunner.TryReadExclusions(args, out var excluded, out var refusal))
{
    await Console.Error.WriteLineAsync(refusal);

    return MigrationRunner.ExitCodeFailure;
}

// The connection string comes from the environment, never from a committed file
// (ADR-ARCH-019). It is never written to the output.
var connectionString = Environment.GetEnvironmentVariable(MigrationRunner.ConnectionStringVariable);

if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync(
        $"No connection string: set the {MigrationRunner.ConnectionStringVariable} environment variable.");

    return MigrationRunner.ExitCodeFailure;
}

return MigrationRunner.Run(connectionString, excluded);

using SaasBtp.Migrations;

// The connection string comes from the environment, never from a committed file
// (ADR-ARCH-019). It is never written to the output.
var connectionString = Environment.GetEnvironmentVariable(MigrationRunner.ConnectionStringVariable);

if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync(
        $"No connection string: set the {MigrationRunner.ConnectionStringVariable} environment variable.");

    return MigrationRunner.ExitCodeFailure;
}

return MigrationRunner.Run(connectionString);

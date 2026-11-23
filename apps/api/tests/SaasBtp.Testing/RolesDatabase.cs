using System.Security.Cryptography;
using Npgsql;
using Testcontainers.PostgreSql;

namespace SaasBtp.Testing;

/// <summary>
/// A PostgreSQL started from <see cref="PostgresImage"/>, on which <c>infra/supabase/roles.sql</c>
/// ran as the maintainer runs it on a project: the roles <c>saasbtp_migrator</c> and
/// <c>saasbtp_api</c> exist, with the attributes and grants of ADR-ARCH-027 and ADR-ARCH-028.
/// </summary>
public sealed class RolesDatabase : IAsyncDisposable
{
    /// <summary>The database <c>roles.sql</c> grants on; the container is created with it.</summary>
    public const string DatabaseName = "postgres";

    private const string RolesScriptPath = "/tmp/roles.sql";

    private readonly PostgreSqlContainer _container;

    private RolesDatabase(PostgreSqlContainer container, string migratorPassword, string apiPassword)
    {
        _container = container;
        SuperuserConnectionString = container.GetConnectionString();
        MigratorConnectionString = As("saasbtp_migrator", migratorPassword);
        ApiConnectionString = As("saasbtp_api", apiPassword);
    }

    /// <summary>The container's superuser, which bypasses row level security: for setup and observation only.</summary>
    public string SuperuserConnectionString { get; }

    /// <summary>The runner's role, <c>saasbtp_migrator</c> (ADR-ARCH-027).</summary>
    public string MigratorConnectionString { get; }

    /// <summary>The API's role, <c>saasbtp_api</c> (ADR-ARCH-028).</summary>
    public string ApiConnectionString { get; }

    /// <summary>
    /// Starts the container, runs <c>roles.sql</c> with <c>psql</c> in one transaction that stops at
    /// the first error, then gives each role a password generated for this database alone.
    /// </summary>
    /// <param name="cancellationToken">Stops the start.</param>
    /// <returns>The started database; dispose it to remove the container.</returns>
    public static async Task<RolesDatabase> StartAsync(CancellationToken cancellationToken)
    {
        var container = PostgresImage.CreateBuilder().WithDatabase(DatabaseName).Build();
        try
        {
            await container.StartAsync(cancellationToken);
            await ApplyRolesAsync(container, cancellationToken);

            var migratorPassword = NewPassword();
            var apiPassword = NewPassword();
            await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
            {
                await connection.OpenAsync(cancellationToken);

                // A password cannot be a parameter of ALTER ROLE; these are hexadecimal digits only.
                await using var command = new NpgsqlCommand(
                    $"alter role saasbtp_migrator password '{migratorPassword}'; alter role saasbtp_api password '{apiPassword}'",
                    connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return new RolesDatabase(container, migratorPassword, apiPassword);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Runs the migration runner on this database as <c>saasbtp_migrator</c>, excluding the
    /// auth-coupled scripts (<see cref="AuthCoupledScripts"/>).
    /// </summary>
    /// <param name="cancellationToken">Stops the wait for the runner.</param>
    /// <returns>The runner's exit code and output.</returns>
    public Task<RunnerResult> MigrateAsync(CancellationToken cancellationToken) =>
        Runner.RunAsync(MigratorConnectionString, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private static async Task ApplyRolesAsync(PostgreSqlContainer container, CancellationToken cancellationToken)
    {
        byte[] script;
        await using (var resource = typeof(RolesDatabase).Assembly.GetManifestResourceStream("roles.sql")
            ?? throw new InvalidOperationException("roles.sql is not embedded in SaasBtp.Testing."))
        using (var buffer = new MemoryStream())
        {
            await resource.CopyToAsync(buffer, cancellationToken);
            script = buffer.ToArray();
        }

        await container.CopyAsync(script, RolesScriptPath, ct: cancellationToken);

        // The command roles.sql documents, as the container's superuser over its local socket.
        var superuser = new NpgsqlConnectionStringBuilder(container.GetConnectionString()).Username
            ?? throw new InvalidOperationException("The container's connection string names no user.");
        var result = await container.ExecAsync(
            [
                "psql", "--username", superuser, "--dbname", DatabaseName,
                "--single-transaction", "--set", "ON_ERROR_STOP=1", "--file", RolesScriptPath,
            ],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"roles.sql failed with exit code {result.ExitCode}: {result.Stderr}");
        }
    }

    private static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private string As(string role, string password) =>
        new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Username = role, Password = password }.ConnectionString;
}

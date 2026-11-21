namespace SaasBtp.Testing;

/// <summary>
/// A <see cref="RolesDatabase"/> the migration runner migrated as <c>saasbtp_migrator</c>, the
/// database every module's integration tests run on (ADR-ARCH-032 §6, ADR-ARCH-034 §6).
/// </summary>
public sealed class MigratedDatabase : IAsyncDisposable
{
    private MigratedDatabase(RolesDatabase database) => Database = database;

    /// <summary>The database, its roles and their connection strings.</summary>
    public RolesDatabase Database { get; }

    /// <summary>Starts a <see cref="RolesDatabase"/> and runs the runner on it.</summary>
    /// <param name="cancellationToken">Stops the start and the migration.</param>
    /// <returns>The migrated database; dispose it to remove the container.</returns>
    /// <exception cref="InvalidOperationException">The runner did not exit with 0; the message holds its output.</exception>
    public static async Task<MigratedDatabase> StartAsync(CancellationToken cancellationToken)
    {
        var database = await RolesDatabase.StartAsync(cancellationToken);
        try
        {
            var result = await database.MigrateAsync(cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"The runner exited with {result.ExitCode}: {result.Output}");
            }

            return new MigratedDatabase(database);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Database.DisposeAsync();
}

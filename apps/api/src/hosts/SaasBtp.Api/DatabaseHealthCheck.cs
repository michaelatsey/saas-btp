using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace SaasBtp.Api;

/// <summary>
/// The readiness probe of the database: it opens a connection and runs a trivial query.
/// </summary>
/// <remarks>
/// The connection string is read when the probe runs, never when the application starts: a
/// start, and the build-time OpenAPI generation that performs one, need neither a database nor
/// a secret (ADR-ARCH-018). A failure of any kind, a missing connection string included, is
/// reported by the health check service as unhealthy; the response carries the status only.
/// </remarks>
internal sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public const string Name = "database";

    public const string ReadinessTag = "ready";

    public const string ConnectionStringName = "SaasBtp";

    /// <summary>
    /// How long the probe may take. The health check service cancels the probe after this
    /// delay, but a connection attempt that is waiting for the server does not end on
    /// cancellation: the same delay is therefore given to the driver, which then gives up by
    /// itself.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"No connection string named '{ConnectionStringName}' is configured.");
        }

        NpgsqlConnectionStringBuilder probe;
        try
        {
            probe = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = (int)Timeout.TotalSeconds,
                CommandTimeout = (int)Timeout.TotalSeconds,
            };
        }
        catch (Exception)
        {
            // The driver quotes what it cannot read in the message of its exception,
            // credentials included, and the health check service logs any exception a probe
            // throws: this one stops here.
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"The connection string named '{ConnectionStringName}' is not an Npgsql connection string.");
        }

        await using var connection = new NpgsqlConnection(probe.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);

        return HealthCheckResult.Healthy();
    }
}

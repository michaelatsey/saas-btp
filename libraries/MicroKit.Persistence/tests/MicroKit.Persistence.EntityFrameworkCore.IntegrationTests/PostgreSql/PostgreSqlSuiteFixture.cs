using Npgsql;
using SaasBtp.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.PostgreSql;

/// <summary>
/// One PostgreSQL container, started once and shared by every test in the
/// <see cref="PostgreSqlSuite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Rewritten here (ADR-ARCH-035 §7): MicroKit's adapter drove the PostgreSqlFixture of
/// MicroKit.Persistence.Testing.PostgreSql, which is not copied, and asked a Docker probe so that
/// the tests skipped without Docker. This one starts the container with Testcontainers directly,
/// on the image every test of this repository starts (<see cref="PostgresImage"/>), and a missing
/// Docker fails the suite: the repository's test script requires Docker.
/// </para>
/// <para>
/// Shared rather than per-class, so the image is pulled and the server started once, and so the
/// Docker-backed tests run serially among themselves while the SQLite tests carry on.
/// </para>
/// </remarks>
public sealed class PostgreSqlSuiteFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgresImage.CreateBuilder().Build();

    /// <summary>Gets the connection string of the server's default database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Gets a connection string to another database of the shared server, typically one per test.
    /// The database is not created: <c>EnsureCreated</c> or a migration creates it.
    /// </summary>
    public string GetConnectionString(string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;
    }

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// Groups the Docker-backed tests so they share one container and run serially among themselves.
/// </summary>
/// <remarks>
/// Named <c>…Suite</c> rather than <c>…Collection</c>: CA1711 reserves the <c>Collection</c>
/// suffix for types that actually are collections, and this one is an xunit grouping marker.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PostgreSqlSuite : ICollectionFixture<PostgreSqlSuiteFixture>
{
    /// <summary>The collection name shared by every Docker-backed test.</summary>
    public const string Name = "PostgreSql";
}

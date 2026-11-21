using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaasBtp.Testing;
using Testcontainers.PostgreSql;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests;

/// <summary>
/// One PostgreSQL container, started once from <see cref="PostgresImage"/> and shared by every
/// test of the project. A missing Docker fails the tests: the repository's test script requires it.
/// </summary>
public sealed class PostgreSqlServer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = PostgresImage.CreateBuilder().Build();

    /// <summary>Gets the connection string of the server's default database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Creates a database of its own for one test, with the schema of <see cref="SubjectDbContext"/>.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = "subject_" + Guid.NewGuid().ToString("N");
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;

        // Created by a context the provider does not register: the schema is the test's set-up,
        // not a command the interceptors judge.
        var options = new DbContextOptionsBuilder<SubjectDbContext>().UseNpgsql(connectionString).Options;
        await using (var context = new SubjectDbContext(options))
            await context.Database.EnsureCreatedAsync();

        return new TestDatabase(this, name, connectionString);
    }

    internal async Task DropAsync(string database)
    {
        // WITH (FORCE): the registered contexts' data source may still hold idle connections.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"drop database if exists \"{database}\" with (force)";
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>Groups the tests that share the <see cref="PostgreSqlServer"/>, and runs them serially.</summary>
[CollectionDefinition(Name)]
public sealed class PostgreSqlServerSuite : ICollectionFixture<PostgreSqlServer>
{
    /// <summary>The collection name shared by every test of the project.</summary>
    public const string Name = "PostgreSqlServer";
}

/// <summary>A database of one test, dropped on dispose.</summary>
public sealed class TestDatabase(PostgreSqlServer server, string name, string connectionString) : IAsyncDisposable
{
    /// <summary>Gets the connection string of the database.</summary>
    public string ConnectionString { get; } = connectionString;

    /// <summary>Counts the rows of <see cref="SubjectRow"/>, on a connection no context owns.</summary>
    public async Task<long> CountRowsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from subject_rows";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Runs a statement of the test's set-up, on a connection no context owns.</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // a constant statement of the test's own set-up
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await server.DropAsync(name);
}

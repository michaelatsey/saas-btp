using System.Net;
using System.Net.Sockets;
using Npgsql;
using SaasBtp.Testing;
using Testcontainers.PostgreSql;

namespace SaasBtp.Migrations.IntegrationTests;

public sealed class RunnerFailureTests
{
    private const string Password = "S3cr3t-Never-Printed";

    [Fact]
    public async Task WithoutAConnectionStringTheRunnerFails()
    {
        var result = await Runner.RunAsync(connectionString: null, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(Runner.ConnectionStringVariable, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgainstAnUnreachableDatabaseTheRunnerFailsWithoutPrintingItsConnectionString()
    {
        var connectionString = $"Host=127.0.0.1;Port={ClosedPort()};Database=postgres;Username=postgres;Password={Password}";

        var result = await Runner.RunAsync(connectionString, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(Password, result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(connectionString, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithAWrongPasswordTheRunnerFailsWithoutPrintingItsConnectionString()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using PostgreSqlContainer postgres = PostgresImage.CreateBuilder().Build();
        await postgres.StartAsync(cancellationToken);
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Password = Password }
            .ConnectionString;

        var result = await Runner.RunAsync(connectionString, cancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(Password, result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(connectionString, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithAMalformedConnectionStringTheRunnerFailsWithoutPrintingIt()
    {
        // A URI is not an Npgsql connection string, and the driver quotes what it cannot read.
        var connectionString = $"postgresql://postgres:{Password}@127.0.0.1:1/postgres?sslmode=require";

        var result = await Runner.RunAsync(connectionString, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(Password, result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(connectionString, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgainstADatabaseThatDoesNotExistTheRunnerFailsAndDoesNotCreateIt()
    {
        const string missingDatabase = "not_created_by_the_runner";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using PostgreSqlContainer postgres = PostgresImage.CreateBuilder().Build();
        await postgres.StartAsync(cancellationToken);
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = missingDatabase }
            .ConnectionString;

        var result = await Runner.RunAsync(connectionString, cancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select count(*) from pg_database where datname = @name", connection);
        command.Parameters.AddWithValue("name", missingDatabase);
        Assert.Equal(0L, await command.ExecuteScalarAsync(cancellationToken));
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}

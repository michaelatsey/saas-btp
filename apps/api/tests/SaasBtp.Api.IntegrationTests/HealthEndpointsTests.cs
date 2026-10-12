using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SaasBtp.Testing;
using Testcontainers.PostgreSql;

namespace SaasBtp.Api.IntegrationTests;

public sealed class HealthEndpointsTests
{
    private static readonly Uri Live = new("/health/live", UriKind.Relative);
    private static readonly Uri Ready = new("/health/ready", UriKind.Relative);

    [Fact]
    public async Task LiveReturns200WithoutADatabaseConfigured()
    {
        using var response = await GetAsync(Live, databaseConnectionString: "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LiveReturns200WhenTheDatabaseIsUnreachable()
    {
        using var response = await GetAsync(Live, ConnectionStringTo(ClosedPort()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyReturns503WithoutADatabaseConfigured()
    {
        using var response = await GetAsync(Ready, databaseConnectionString: "");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await ReadBodyAsync(response));
    }

    [Fact]
    public async Task ReadyReturns503WithoutDetailWhenTheDatabaseRefusesTheConnection()
    {
        using var response = await GetAsync(Ready, ConnectionStringTo(ClosedPort()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await ReadBodyAsync(response));
    }

    [Fact]
    public async Task ReadyReturns503AndLogsNoCredentialWhenTheConnectionStringIsMalformed()
    {
        // A URI is not an Npgsql connection string, and the driver quotes what it cannot read.
        const string password = "S3cr3t-Never-Logged";
        using var logs = new CapturedLogs();

        using var response = await GetAsync(
            Ready, $"postgresql://postgres:{password}@127.0.0.1:1/postgres?sslmode=require", logs);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await ReadBodyAsync(response));
        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(password, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadyReturns503WithinItsTimeoutWhenTheDatabaseNeverAnswers()
    {
        // Accepts the TCP connection, then stays silent: the probe has to give up by itself.
        using var silentServer = new TcpListener(IPAddress.Loopback, port: 0);
        silentServer.Start();
        var port = ((IPEndPoint)silentServer.LocalEndpoint).Port;

        var stopwatch = Stopwatch.StartNew();
        using var response = await GetAsync(Ready, ConnectionStringTo(port));
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await ReadBodyAsync(response));
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReadyReturns200WithAReachableDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using PostgreSqlContainer postgres = PostgresImage.CreateBuilder().Build();
        await postgres.StartAsync(cancellationToken);

        using var response = await GetAsync(Ready, postgres.GetConnectionString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> GetAsync(
        Uri path, string databaseConnectionString, CapturedLogs? logs = null)
    {
        await using var factory = new ApiFactory(databaseConnectionString, logs);
        using var client = factory.CreateClient();

        return await client.GetAsync(path, TestContext.Current.CancellationToken);
    }

    private static Task<string> ReadBodyAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    private static string ConnectionStringTo(int port) =>
        $"Host=127.0.0.1;Port={port};Database=postgres;Username=postgres;Password=unused";

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}

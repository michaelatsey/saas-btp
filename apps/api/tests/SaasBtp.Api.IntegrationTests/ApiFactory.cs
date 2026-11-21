using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// The API on a test server, with the database connection string each test chooses. An empty
/// string stands for an API started with no database configured, whatever the environment of
/// the machine running the tests.
/// </summary>
/// <remarks>
/// The access token settings are the test issuer, audience and JWKS address, and the JWKS is the
/// in-process <see cref="Jwks"/>: the host validates tokens on its production path, and no test
/// reaches the provider (ADR-ARCH-038 decision 9). The endpoints of <see cref="TestEndpoints"/>
/// are mapped behind the host's own authentication and authorization.
/// </remarks>
internal sealed class ApiFactory(string databaseConnectionString, ILoggerProvider? logs = null)
    : WebApplicationFactory<Program>
{
    /// <summary>The JWKS the host fetches its keys from.</summary>
    public TestJwks Jwks { get; init; } = new();

    /// <summary>Settings applied after the defaults, to replace one of them.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>The host environment, when a test needs another one than the test server's default.</summary>
    public string? Environment { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:SaasBtp", databaseConnectionString);
        builder.UseSetting("AccessToken:Issuer", TestTokens.Issuer);
        builder.UseSetting("AccessToken:Audience", TestTokens.Audience);
        builder.UseSetting("AccessToken:JwksUri", TestJwks.Address);

        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        if (Environment is not null)
        {
            builder.UseEnvironment(Environment);
        }

        builder.ConfigureServices(services =>
        {
            services.AddHttpClient(TestJwks.ClientName).ConfigurePrimaryHttpMessageHandler(Jwks.CreateHandler);
            services.AddSingleton<IStartupFilter, TestEndpoints>();
        });

        if (logs is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        }
    }
}

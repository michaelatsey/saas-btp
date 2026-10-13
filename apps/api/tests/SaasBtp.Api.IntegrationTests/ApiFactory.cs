using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// The API on a test server, with the database connection string each test chooses. An empty
/// string stands for an API started with no database configured, whatever the environment of
/// the machine running the tests.
/// </summary>
internal sealed class ApiFactory(string databaseConnectionString, ILoggerProvider? logs = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:SaasBtp", databaseConnectionString);

        if (logs is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        }
    }
}

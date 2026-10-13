using System.Net;
using System.Text.Json;

namespace SaasBtp.Api.IntegrationTests;

public sealed class PingEndpointTests
{
    [Fact]
    public async Task GetPingReturns200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(databaseConnectionString: "");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/ping", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
    }
}

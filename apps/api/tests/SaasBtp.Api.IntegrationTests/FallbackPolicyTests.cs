using System.Net;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// Authentication is the fallback policy of every endpoint; the anonymous ones say so
/// (ADR-ARCH-038 decision 1, ADR-ARCH-018).
/// </summary>
public sealed class FallbackPolicyTests : IDisposable
{
    private readonly TestKey _key = new("published");

    [Fact]
    public async Task AnEndpointThatDeclaresNoAuthorizationAnswers401WithoutAToken()
    {
        using var logs = new CapturedLogs();
        await using var api = new Api(_key, logs);

        using var response = await api.GetAsync(TestEndpoints.Protected, token: null);

        Refusal.AssertBare401(response);
        Refusal.AssertLogged(logs, "no bearer token", token: null);
    }

    [Fact]
    public async Task PingAnswers200WithoutAToken()
    {
        await using var api = new Api(_key);

        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync("/api/v1/ping", token: null));
    }

    [Fact]
    public async Task LiveAnswers200WithoutAToken()
    {
        await using var api = new Api(_key);

        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync("/health/live", token: null));
    }

    [Fact]
    public async Task ReadyAnswers503WithoutATokenAndWithoutADatabaseAsToday()
    {
        await using var api = new Api(_key);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, await api.StatusAsync("/health/ready", token: null));
    }

    public void Dispose() => _key.Dispose();
}

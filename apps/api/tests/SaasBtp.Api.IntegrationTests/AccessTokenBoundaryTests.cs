using System.Net;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// The boundaries of ADR-ARCH-038 decision 9: the 30 s skew, the one-hour lifetime, and the 90 s
/// bound of an operation that requires a recent token (decision 6).
/// </summary>
/// <remarks>
/// Claims are whole seconds. A token that must be accepted is built from the next whole second,
/// so that it is at most as old as stated plus the request time; one that must be refused, from
/// the current second, so that it is at least as old as stated. A warm-up request first fetches
/// the keys and compiles the pipeline, so that the measured request is short.
/// </remarks>
public sealed class AccessTokenBoundaryTests : IDisposable
{
    private readonly TestKey _key = new("published");

    [Fact]
    public async Task ATokenExpired29SecondsEarlierIsAccepted()
    {
        await using var api = await WarmApiAsync();

        var claims = TestTokens.Claims();
        claims["exp"] = TestTokens.Ceiling(DateTimeOffset.UtcNow) - 29;
        claims["iat"] = (long)claims["exp"] - 600;

        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Protected, TestTokens.Sign(claims, _key)));
    }

    [Fact]
    public async Task ATokenExpired31SecondsEarlierIsRefused()
    {
        using var logs = new CapturedLogs();
        await using var api = await WarmApiAsync(logs);

        var claims = TestTokens.Claims();
        claims["exp"] = TestTokens.Floor(DateTimeOffset.UtcNow) - 31;
        claims["iat"] = (long)claims["exp"] - 600;
        var token = TestTokens.Sign(claims, _key);

        using var response = await api.GetAsync(TestEndpoints.Protected, token);

        Refusal.AssertBare401(response);
        Refusal.AssertLogged(logs, "SecurityTokenExpiredException", token);
    }

    [Fact]
    public async Task ALifetimeOfExactlyOneHourIsAccepted()
    {
        await using var api = await WarmApiAsync();

        var claims = TestTokens.Claims();
        claims["exp"] = (long)claims["iat"] + 3600;

        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Protected, TestTokens.Sign(claims, _key)));
    }

    [Fact]
    public async Task ALifetimeOfOneHourAndOneSecondIsRefused()
    {
        using var logs = new CapturedLogs();
        await using var api = await WarmApiAsync(logs);

        var claims = TestTokens.Claims();
        claims["exp"] = (long)claims["iat"] + 3601;
        var token = TestTokens.Sign(claims, _key);

        using var response = await api.GetAsync(TestEndpoints.Protected, token);

        Refusal.AssertBare401(response);
        Refusal.AssertLogged(logs, "lifetime over one hour", token);
    }

    [Fact]
    public async Task ATokenIssued89SecondsEarlierIsAcceptedWhereARecentTokenIsRequired()
    {
        await using var api = await WarmApiAsync();

        var claims = TestTokens.Claims();
        claims["iat"] = TestTokens.Ceiling(DateTimeOffset.UtcNow) - 89;
        claims["exp"] = (long)claims["iat"] + 3600;

        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Recent, TestTokens.Sign(claims, _key)));
    }

    [Fact]
    public async Task ATokenIssued91SecondsEarlierIsAnswered401NotForbiddenWhereARecentTokenIsRequired()
    {
        using var logs = new CapturedLogs();
        await using var api = await WarmApiAsync(logs);

        var claims = TestTokens.Claims();
        claims["iat"] = TestTokens.Floor(DateTimeOffset.UtcNow) - 91;
        claims["exp"] = (long)claims["iat"] + 3600;
        var token = TestTokens.Sign(claims, _key);

        using var response = await api.GetAsync(TestEndpoints.Recent, token);

        Refusal.AssertBare401(response);
        Refusal.AssertLogged(logs, "token older than the bound", token);
    }

    [Fact]
    public async Task NoTokenIsAnswered401WhereARecentTokenIsRequired()
    {
        using var logs = new CapturedLogs();
        await using var api = new Api(_key, logs);

        using var response = await api.GetAsync(TestEndpoints.Recent, token: null);

        Refusal.AssertBare401(response);
        Refusal.AssertLogged(logs, "no bearer token", token: null);
    }

    public void Dispose() => _key.Dispose();

    /// <summary>An API whose keys are fetched and whose pipeline has served one request; its logs start empty.</summary>
    private async Task<Api> WarmApiAsync(CapturedLogs? logs = null)
    {
        var api = new Api(_key, logs);
        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Recent, TestTokens.Sign(_key)));
        logs?.Clear();

        return api;
    }
}

using System.Net;
using System.Net.Http.Headers;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// Each refusal of ADR-ARCH-038 decision 1, on an endpoint the test maps: 401 with a bare
/// <c>Bearer</c> challenge (decision 4), and one log entry naming the reason, with neither the
/// token nor its payload in any log entry.
/// </summary>
public sealed class AccessTokenAuthenticationTests : IDisposable
{
    private readonly TestKey _key = new("published");

    [Fact]
    public async Task AValidTokenIsAccepted()
    {
        using var logs = new CapturedLogs();
        await using var api = new Api(_key, logs);

        using var response = await api.GetAsync(TestEndpoints.Protected, TestTokens.Sign(_key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public Task NoTokenIsRefused() => AssertRefusedAsync(token: null, "no bearer token");

    [Fact]
    public Task AMalformedTokenIsRefused() => AssertRefusedAsync("not-a-jwt", "SecurityTokenMalformedException");

    [Fact]
    public Task AnUnsignedTokenIsRefused() =>
        AssertRefusedAsync(TestTokens.Unsigned(TestTokens.Claims()), "SecurityTokenInvalidSignatureException");

    [Fact]
    public Task AnHs256TokenIsRefused() =>
        AssertRefusedAsync(TestTokens.Hs256(TestTokens.Claims()), "SecurityTokenSignatureKeyNotFoundException");

    [Fact]
    public Task ATokenWithABadSignatureIsRefused()
    {
        // Signed by another key that claims the published key's identifier.
        using var impostor = new TestKey(_key.KeyId);

        return AssertRefusedAsync(TestTokens.Sign(impostor), "SecurityTokenInvalidSignatureException");
    }

    [Fact]
    public Task ATokenSignedByAnUnknownKeyIsRefused()
    {
        using var unknown = new TestKey("unknown");

        return AssertRefusedAsync(TestTokens.Sign(unknown), "SecurityTokenSignatureKeyNotFoundException");
    }

    [Fact]
    public Task ATokenFromAnotherIssuerIsRefused()
    {
        var claims = TestTokens.Claims();
        claims["iss"] = "https://another-issuer.test/auth/v1";

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "SecurityTokenInvalidIssuerException");
    }

    [Fact]
    public Task ATokenForAnotherAudienceIsRefused()
    {
        var claims = TestTokens.Claims();
        claims["aud"] = "anon";

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "SecurityTokenInvalidAudienceException");
    }

    [Fact]
    public Task ATokenWithoutExpIsRefused()
    {
        var claims = TestTokens.Claims();
        claims.Remove("exp");

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "SecurityTokenNoExpirationException");
    }

    [Fact]
    public Task ATokenWithoutIatIsRefused()
    {
        var claims = TestTokens.Claims();
        claims.Remove("iat");

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "no iat");
    }

    [Fact]
    public Task ATokenWithALifetimeOverOneHourIsRefused()
    {
        var claims = TestTokens.Claims();
        claims["exp"] = (long)claims["iat"] + 7200;

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "lifetime over one hour");
    }

    [Fact]
    public Task ATokenIssuedInTheFutureBeyondTheSkewIsRefused()
    {
        // Valid for an hour after its iat, which is a day ahead: without this check it would be
        // accepted for 25 hours, and count as recent for as long.
        var claims = TestTokens.Claims();
        claims["iat"] = TestTokens.Floor(DateTimeOffset.UtcNow) + 86400;
        claims["exp"] = (long)claims["iat"] + 3600;

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "iat in the future");
    }

    [Fact]
    public Task ATokenWhoseIatIsFarInThePastIsRefusedEvenWhenItsLifetimeOverflows()
    {
        var claims = TestTokens.Claims();
        claims["iat"] = long.MinValue + 1;

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "lifetime over one hour");
    }

    [Fact]
    public Task ATokenExpiredBeyondTheSkewIsRefused()
    {
        var claims = TestTokens.Claims();
        claims["exp"] = TestTokens.Floor(DateTimeOffset.UtcNow) - 120;
        claims["iat"] = (long)claims["exp"] - 3600;

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "SecurityTokenExpiredException");
    }

    [Fact]
    public Task ATokenWhoseSubIsNotAUuidIsRefused()
    {
        var claims = TestTokens.Claims();
        claims["sub"] = "not-a-uuid";

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "sub not a UUID");
    }

    [Fact]
    public Task AnAnonymousTokenIsRefused()
    {
        var claims = TestTokens.Claims();
        claims["is_anonymous"] = true;

        return AssertRefusedAsync(TestTokens.Sign(claims, _key), "anonymous session");
    }

    public void Dispose() => _key.Dispose();

    private async Task AssertRefusedAsync(string? token, string reason)
    {
        using var logs = new CapturedLogs();
        await using var api = new Api(_key, logs);

        using var response = await api.GetAsync(TestEndpoints.Protected, token);

        Refusal.AssertBare401(response);
        Refusal.AssertLogged(logs, reason, token);
    }
}

/// <summary>An API whose JWKS publishes one key, and a client of it.</summary>
internal sealed class Api : IAsyncDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public Api(TestKey key, CapturedLogs? logs = null)
        : this(new ApiFactory(databaseConnectionString: "", logs) { Jwks = new TestJwks(key) })
    {
    }

    public Api(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public ApiFactory Factory => _factory;

    public async Task<HttpResponseMessage> GetAsync(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async Task<HttpStatusCode> StatusAsync(string path, string? token)
    {
        using var response = await GetAsync(path, token);
        return response.StatusCode;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}

/// <summary>What every 401 of ADR-ARCH-038 decision 4 looks like, and how it is logged.</summary>
internal static class Refusal
{
    /// <summary>The entry the host writes for each 401, followed by its reason.</summary>
    public const string LogPrefix = "Access token refused";

    public static void AssertBare401(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Null(challenge.Parameter);
        Assert.Equal("Bearer", string.Join(", ", response.Headers.GetValues("WWW-Authenticate")));
    }

    public static void AssertLogged(CapturedLogs logs, string reason, string? token)
    {
        var refusals = logs.Entries.Where(entry => entry.StartsWith(LogPrefix, StringComparison.Ordinal)).ToList();
        var entry = Assert.Single(refusals);
        Assert.Contains(reason, entry, StringComparison.Ordinal);

        if (token is not null)
        {
            Assert.DoesNotContain(logs.Entries, logged => logged.Contains(token, StringComparison.Ordinal));
            if (token.Contains('.', StringComparison.Ordinal))
            {
                var payload = TestTokens.Payload(token);
                Assert.DoesNotContain(logs.Entries, logged => logged.Contains(payload, StringComparison.Ordinal));
            }
        }
    }
}

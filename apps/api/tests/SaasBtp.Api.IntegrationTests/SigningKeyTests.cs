using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// The keys and their rotation (ADR-ARCH-038 decision 2), against the in-process JWKS.
/// </summary>
/// <remarks>
/// Each test runs on its own host: only the first refresh a key manager is asked for runs at
/// once, later ones wait 5 minutes. A refresh runs in the background, so every assertion "after
/// the refresh" waits for the JWKS to be fetched again, then repeats the request until the
/// expected answer comes, within <see cref="Eventually.Deadline"/>.
/// </remarks>
public sealed class SigningKeyTests : IDisposable
{
    private readonly TestKey _first = new("first");
    private readonly TestKey _second = new("second");

    [Fact]
    public async Task AKeyAddedAfterStartIsAcceptedAfterTheRefreshOnAnUnknownKey()
    {
        var jwks = new TestJwks(_first);
        await using var api = new Api(new ApiFactory(databaseConnectionString: "") { Jwks = jwks });
        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Protected, TestTokens.Sign(_first)));
        var fetched = jwks.Fetches;

        jwks.Publish(_first, _second);
        var token = TestTokens.Sign(_second);

        Assert.Equal(HttpStatusCode.Unauthorized, await api.StatusAsync(TestEndpoints.Protected, token));
        await jwks.WaitForFetchesAsync(fetched + 1);
        await Eventually.StatusAsync(
            () => api.StatusAsync(TestEndpoints.Protected, token), HttpStatusCode.OK, "A token signed by the added key");
    }

    [Fact]
    public async Task ARemovedKeyIsRefusedAfterTheAutomaticRefresh()
    {
        var jwks = new TestJwks(_first, _second);
        var factory = new ApiFactory(databaseConnectionString: "") { Jwks = jwks };
        var clock = KeyManagerClock.Replace(factory);
        await using var api = new Api(factory);
        var token = TestTokens.Sign(_first);
        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Protected, token));
        var fetched = jwks.Fetches;

        jwks.Publish(_second);
        clock.Advance(KeyManagerClock.PastTheAutomaticRefresh);

        // The first request after the interval starts the refresh; it may still be served the old keys.
        await api.StatusAsync(TestEndpoints.Protected, token);
        await jwks.WaitForFetchesAsync(fetched + 1);
        await Eventually.StatusAsync(
            () => api.StatusAsync(TestEndpoints.Protected, token), HttpStatusCode.Unauthorized, "A token signed by the removed key");
        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Protected, TestTokens.Sign(_second)));
    }

    [Fact]
    public async Task AnEmptyKeySetReplacesTheKeysAtTheAutomaticRefresh()
    {
        // The JWKS of a project signing with the legacy secret holds no key (ADR-ARCH-038 decision 1):
        // a successful fetch, after which every token is refused.
        var jwks = new TestJwks(_first);
        var factory = new ApiFactory(databaseConnectionString: "") { Jwks = jwks };
        var clock = KeyManagerClock.Replace(factory);
        await using var api = new Api(factory);
        var token = TestTokens.Sign(_first);
        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Protected, token));
        var fetched = jwks.Fetches;

        jwks.Publish();
        clock.Advance(KeyManagerClock.PastTheAutomaticRefresh);

        await api.StatusAsync(TestEndpoints.Protected, token);
        await jwks.WaitForFetchesAsync(fetched + 1);
        await Eventually.StatusAsync(
            () => api.StatusAsync(TestEndpoints.Protected, token), HttpStatusCode.Unauthorized, "A token signed by the last key");
    }

    [Fact]
    public async Task AFailedRefreshKeepsTheLastKeys()
    {
        var jwks = new TestJwks(_first);
        await using var api = new Api(new ApiFactory(databaseConnectionString: "") { Jwks = jwks });
        var token = TestTokens.Sign(_first);
        Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Protected, token));
        var fetched = jwks.Fetches;

        jwks.Failing = true;
        using var unknown = new TestKey("unknown");
        Assert.Equal(HttpStatusCode.Unauthorized, await api.StatusAsync(TestEndpoints.Protected, TestTokens.Sign(unknown)));
        await jwks.WaitForFetchesAsync(fetched + 1);

        // The refresh was answered 500; for as long as the deadline, the first keys stay in use.
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < until)
        {
            Assert.Equal(HttpStatusCode.OK, await api.StatusAsync(TestEndpoints.Protected, token));
            await Task.Delay(Eventually.Interval, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task WithNoKeyEverFetchedARequestIsAnswered500()
    {
        // On Kestrel and in Production: the 500 is the server's answer to the handler's rethrow,
        // not the developer exception page nor the test server's rethrow.
        var jwks = new TestJwks(_first) { Failing = true };
        var factory = new ApiFactory(databaseConnectionString: "") { Jwks = jwks, Environment = "Production" };
        factory.UseKestrel(0);
        factory.StartServer();
        await using var api = new Api(factory);

        Assert.Equal(HttpStatusCode.InternalServerError, await api.StatusAsync(TestEndpoints.Protected, TestTokens.Sign(_first)));
        Assert.True(jwks.Fetches > 0, "The host never asked the JWKS for its keys.");
    }

    public void Dispose()
    {
        _first.Dispose();
        _second.Dispose();
    }
}

/// <summary>
/// The clock of the host's key manager, replaced so that a test moves past the automatic refresh
/// interval without waiting for it.
/// </summary>
/// <remarks>
/// IdentityModel's <see cref="ConfigurationManager{T}"/> reads time from an internal
/// <c>TimeProvider</c> field (8.19.2). It is reached through the host's
/// <see cref="JwtBearerOptions.ConfigurationManager"/>, which wraps one such manager, and set
/// before the first fetch. If either is gone, the test fails here, naming what it looked for.
/// </remarks>
internal static class KeyManagerClock
{
    /// <summary>The automatic refresh interval, 10 min, plus IdentityModel's jitter of at most 30 s.</summary>
    public static readonly TimeSpan PastTheAutomaticRefresh = TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(31);

    public static FakeTimeProvider Replace(ApiFactory factory)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var wrapper = options.ConfigurationManager
            ?? throw new InvalidOperationException("The host's JwtBearerOptions has no ConfigurationManager.");

        var manager = wrapper as ConfigurationManager<OpenIdConnectConfiguration>
            ?? wrapper.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(field => field.GetValue(wrapper))
                .OfType<ConfigurationManager<OpenIdConnectConfiguration>>()
                .SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"{wrapper.GetType()} holds no ConfigurationManager<OpenIdConnectConfiguration>.");

        var field = typeof(ConfigurationManager<OpenIdConnectConfiguration>)
            .GetField("TimeProvider", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "ConfigurationManager<T> has no internal TimeProvider field any more: the automatic refresh cannot be tested this way.");

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        field.SetValue(manager, clock);

        return clock;
    }
}

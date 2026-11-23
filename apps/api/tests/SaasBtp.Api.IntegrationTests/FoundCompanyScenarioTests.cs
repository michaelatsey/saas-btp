using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// BP-001's host proofs: BP001-AC-06 to BP001-AC-11 and BP001-AC-23 (contract.md §7, "Host"), the
/// founding's boundaries of ADR-ARCH-038 decision 9, and the host part of BP001-AC-17, which
/// contract.md §7 proves at the Integration level and the maintainer moved here from #218 (decision
/// of 2026-11-22, recorded on #218). Each runs through
/// <c>POST /api/v1/organizations</c> on the production validation path: the test issuer, audience and
/// JWKS address, tokens signed by a test ES256 key the in-process JWKS publishes, no call to the
/// provider (decision 9). A founding token is issued now and holds the level unless a case says
/// otherwise.
/// </summary>
/// <remarks>
/// The class has its own database, migrated by the runner, the API connecting as
/// <c>saasbtp_api</c>; xUnit runs its tests one after another, so the row counts of the Access
/// tables, read as the container's superuser, change only by the request under test: "nothing
/// written" is those counts unchanged.
/// </remarks>
public sealed class FoundCompanyScenarioTests(FoundingDatabase database) : IClassFixture<FoundingDatabase>, IDisposable
{
    private const string Path = "/api/v1/organizations";

    /// <summary>The source of the activity <c>HttpClient</c> starts for each request it sends over the network.</summary>
    private const string HttpClientSource = "System.Net.Http";

    /// <summary>Marks the requests of the test that runs, so that a listener counts only their outbound requests.</summary>
    private static readonly AsyncLocal<Guid> Flow = new();

    private readonly TestKey _key = new("published");

    private readonly CapturedLogs _logs = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every refusal of ADR-ARCH-038 decision 9 for BP001-AC-11, on the founding with a valid body:
    /// 401 with a bare <c>Bearer</c> challenge, no body, nothing written, and the reason of this case
    /// logged, so that a token refused by another check, such as the recent-token bound, fails it.
    /// </summary>
    [Theory]
    [InlineData("no token")]
    [InlineData("malformed")]
    [InlineData("unsigned")]
    [InlineData("HS256")]
    [InlineData("bad signature")]
    [InlineData("unknown key")]
    [InlineData("another issuer")]
    [InlineData("another audience")]
    [InlineData("no exp")]
    [InlineData("no iat")]
    [InlineData("lifetime over one hour")]
    [InlineData("expired beyond the skew")]
    [InlineData("sub not a UUID")]
    [InlineData("anonymous")]
    public async Task BP001_AC_11_ARefusedTokenIsAnswered401AndNothingIsWritten(string @case)
    {
        using var impostor = new TestKey(_key.KeyId);
        using var unknown = new TestKey("unknown");
        var claims = Claims(confirmed: true);
        var (token, reason) = @case switch
        {
            "no token" => (null, "no bearer token"),
            "malformed" => ("not-a-jwt", "SecurityTokenMalformedException"),
            "unsigned" => (TestTokens.Unsigned(claims), "SecurityTokenInvalidSignatureException"),
            "HS256" => (TestTokens.Hs256(claims), "SecurityTokenSignatureKeyNotFoundException"),
            "bad signature" => (TestTokens.Sign(claims, impostor), "SecurityTokenInvalidSignatureException"),
            "unknown key" => (TestTokens.Sign(claims, unknown), "SecurityTokenSignatureKeyNotFoundException"),
            "another issuer" => (Sign(With(claims, "iss", "https://another-issuer.test/auth/v1")), "SecurityTokenInvalidIssuerException"),
            "another audience" => (Sign(With(claims, "aud", "anon")), "SecurityTokenInvalidAudienceException"),
            "no exp" => (Sign(Without(claims, "exp")), "SecurityTokenNoExpirationException"),
            "no iat" => (Sign(Without(claims, "iat")), "no iat"),
            "lifetime over one hour" => (Sign(With(claims, "exp", (long)claims["iat"] + 7200)), "lifetime over one hour"),
            "expired beyond the skew" => (Sign(Expired(claims)), "SecurityTokenExpiredException"),
            "sub not a UUID" => (Sign(With(claims, "sub", "not-a-uuid")), "sub not a UUID"),
            "anonymous" => (Sign(With(claims, "is_anonymous", true)), "anonymous session"),
            _ => throw new ArgumentOutOfRangeException(nameof(@case), @case, "No such case."),
        };
        await using var api = Start();
        var before = await database.AccessRowsAsync();

        using var response = await api.PostAsync(Body(Guid.CreateVersion7(), "Kouassi BTP", "Siège"), token);

        Refusal.AssertBare401(response);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(before, await database.AccessRowsAsync());
        Refusal.AssertLogged(_logs, reason, token);
    }

    /// <summary>
    /// Authentication comes first (ADR-ARCH-038 decision 9): without a token, a body the founding
    /// answers 422 with a valid token is answered 401, nothing written.
    /// </summary>
    [Fact]
    public async Task BP001_AC_11_NoTokenIsAnswered401EvenWithABodyThatWouldBeAnswered422()
    {
        await using var api = Start();
        var body = Body(Guid.CreateVersion7(), "", "Siège");
        var before = await database.AccessRowsAsync();

        using var withoutToken = await api.PostAsync(body, token: null);

        Refusal.AssertBare401(withoutToken);
        Assert.Empty(await withoutToken.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(before, await database.AccessRowsAsync());

        using var withToken = await api.PostAsync(body, Sign(Claims(confirmed: true)));

        Assert.Equal(["organizationName"], await ValidationFieldsAsync(withToken));
    }

    /// <summary>
    /// An empty or blank company name is answered 422 naming <c>organizationName</c>, nothing
    /// written; with a token without the level too, since the body is checked before the level.
    /// </summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public async Task BP001_AC_07_AnEmptyOrBlankCompanyNameIsAnswered422NamingOrganizationName(string name, bool level)
    {
        await using var api = Start();
        var before = await database.AccessRowsAsync();

        using var response = await api.PostAsync(Body(Guid.CreateVersion7(), name, "Siège"), Sign(Claims(confirmed: level)));

        Assert.Equal(["organizationName"], await ValidationFieldsAsync(response));
        Assert.Equal(before, await database.AccessRowsAsync());
    }

    /// <summary>
    /// An empty workspace name is answered 422 naming <c>workspaceName</c>, nothing written; with a
    /// token without the level too.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BP001_AC_08_AnEmptyWorkspaceNameIsAnswered422NamingWorkspaceName(bool level)
    {
        await using var api = Start();
        var before = await database.AccessRowsAsync();

        using var response = await api.PostAsync(Body(Guid.CreateVersion7(), "Kouassi BTP", ""), Sign(Claims(confirmed: level)));

        Assert.Equal(["workspaceName"], await ValidationFieldsAsync(response));
        Assert.Equal(before, await database.AccessRowsAsync());
    }

    /// <summary>
    /// A token whose <c>contact_confirmed</c> is <c>false</c>, and one without the claim, are
    /// answered 403, nothing written; the answer is exactly the problem of ADR-ARCH-040 decision 5,
    /// which says only that the contact must be confirmed, and holds nothing of the caller's token or
    /// request (contract.md §2, the 403 row).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task BP001_AC_09_ACallerWithoutTheLevelIsAnswered403SayingOnlyThatTheContactMustBeConfirmed(bool? confirmed)
    {
        await using var api = Start();
        var claims = Claims(confirmed);
        var token = Sign(claims);
        var reference = Guid.CreateVersion7();
        var before = await database.AccessRowsAsync();

        using var response = await api.PostAsync(Body(reference, "Kouassi BTP", "Siège"), token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.ToString());
        var answer = await response.Content.ReadAsStringAsync(Token);
        Assert.Equal(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"The contact must be confirmed.","status":403}""",
            answer);
        foreach (var value in new[] { (string)claims["sub"], reference.ToString("D"), "Kouassi BTP", "Siège", TestTokens.Payload(token) })
        {
            Assert.DoesNotContain(value, answer, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(response.Headers, header => header.Key.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, await database.AccessRowsAsync());
    }

    /// <summary>
    /// One founding request sent five times by one person (ADR-ARCH-038 decision 9): a token holding
    /// the level whose <c>iat</c> is 91 seconds in the past, standing for one held through a wait, is answered
    /// 401, not 403; a recent token without the level 403; a recent token holding it 201; the same
    /// again 200; a recent token without the level 403, not 200, since the level is checked before
    /// the unit of work. Exactly one organization results.
    /// </summary>
    [Fact]
    public async Task BP001_AC_10_OneRequestSentFiveTimesFoundsOneOrganization()
    {
        await using var api = Start();
        var person = Guid.NewGuid();
        var reference = Guid.CreateVersion7();
        var body = Body(reference, "Kouassi BTP", "Siège");
        var before = await database.AccessRowsAsync();

        var held = Claims(confirmed: true, person);
        held["iat"] = TestTokens.Floor(DateTimeOffset.UtcNow) - 91;
        held["exp"] = (long)held["iat"] + 3600;
        using var stale = await api.PostAsync(body, Sign(held));

        Refusal.AssertBare401(stale);
        Assert.Equal(before, await database.AccessRowsAsync());

        using var unconfirmed = await api.PostAsync(body, Sign(Claims(confirmed: false, person)));

        Assert.Equal(HttpStatusCode.Forbidden, unconfirmed.StatusCode);
        Assert.Equal(before, await database.AccessRowsAsync());

        using var founded = await api.PostAsync(body, Sign(Claims(confirmed: true, person)));

        Assert.Equal(HttpStatusCode.Created, founded.StatusCode);
        var organization = await founded.Content.ReadAsStringAsync(Token);
        var foundedRows = await database.AccessRowsAsync();
        Assert.Equal(before[2] + 1, foundedRows[2]);

        using var replayed = await api.PostAsync(body, Sign(Claims(confirmed: true, person)));

        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(organization, await replayed.Content.ReadAsStringAsync(Token));
        Assert.Equal(foundedRows, await database.AccessRowsAsync());

        using var lost = await api.PostAsync(body, Sign(Claims(confirmed: false, person)));

        Assert.Equal(HttpStatusCode.Forbidden, lost.StatusCode);
        Assert.Equal(foundedRows, await database.AccessRowsAsync());
        Assert.Equal(1L, await database.OrganizationsAsync(reference));
    }

    /// <summary>
    /// A body carrying a <c>founderId</c> that names another person, who has a profile, is founded
    /// with the caller as owner, member and authorized person; the person it names has no relation to
    /// the organization (BP001-R2, contract.md §2: properties the operation does not define are ignored).
    /// </summary>
    [Theory]
    [InlineData("user")]
    [InlineData("profile")]
    public async Task BP001_AC_06_AFounderIdInTheBodyIsIgnored(string named)
    {
        await using var api = Start();
        var other = Guid.NewGuid();
        using var otherFounding = await api.PostAsync(
            Body(Guid.CreateVersion7(), "Autre BTP", "Siège"), Sign(Claims(confirmed: true, other)));
        Assert.Equal(HttpStatusCode.Created, otherFounding.StatusCode);
        var founderId = named == "user" ? other : await database.ProfileIdOfAsync(other);

        var caller = Guid.NewGuid();
        var reference = Guid.CreateVersion7();
        using var response = await api.PostAsync(
            Body(reference, "Kouassi BTP", "Siège", founderId), Sign(Claims(confirmed: true, caller)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var relations = await database.RelationsOfAsync(reference);
        Assert.Equal([caller], relations.CurrentOwner);
        Assert.Equal([caller], relations.Owners);
        Assert.Equal([caller], relations.Members);
        Assert.Equal([caller], relations.Authorized);
        Assert.Equal(relations.Workspaces, relations.AccessWorkspaces);
        Assert.Single(relations.Workspaces);
    }

    /// <summary>Each name one character over the limit of BP001-R7 is answered 422 naming its field, nothing written.</summary>
    [Theory]
    [InlineData("organizationName")]
    [InlineData("workspaceName")]
    public async Task BP001_AC_23_ANameOneCharacterOverTheLimitIsAnswered422NamingItsField(string field)
    {
        await using var api = Start();
        var tooLong = new string('a', 201);
        var before = await database.AccessRowsAsync();

        using var response = await api.PostAsync(
            Body(
                Guid.CreateVersion7(),
                field == "organizationName" ? tooLong : "Kouassi BTP",
                field == "workspaceName" ? tooLong : "Siège"),
            Sign(Claims(confirmed: true)));

        Assert.Equal([field], await ValidationFieldsAsync(response));
        Assert.Equal(before, await database.AccessRowsAsync());
    }

    /// <summary>
    /// For the founding, a token issued 89 seconds earlier is accepted (ADR-ARCH-038 decision 9,
    /// Boundaries). A warm-up founding first fetches the keys and compiles the pipeline, so that the
    /// measured request is processed within a second of its token's signature.
    /// </summary>
    [Fact]
    public async Task Adr038_AFoundingTokenIssued89SecondsEarlierIsAccepted()
    {
        await using var api = Start();
        using var warmUp = await api.PostAsync(Body(Guid.CreateVersion7(), "Kouassi BTP", "Siège"), Sign(Claims(confirmed: true)));
        Assert.Equal(HttpStatusCode.Created, warmUp.StatusCode);

        var claims = Claims(confirmed: true);
        claims["iat"] = TestTokens.Ceiling(DateTimeOffset.UtcNow) - 89;
        claims["exp"] = (long)claims["iat"] + 3600;
        var reference = Guid.CreateVersion7();
        using var response = await api.PostAsync(Body(reference, "Kouassi BTP", "Siège"), Sign(claims));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1L, await database.OrganizationsAsync(reference));
    }

    /// <summary>
    /// For the founding, a token issued 91 seconds earlier is answered 401, not 403, with a bare
    /// <c>Bearer</c> challenge, nothing written (ADR-ARCH-038 decisions 6 and 9).
    /// </summary>
    [Fact]
    public async Task Adr038_AFoundingTokenIssued91SecondsEarlierIsAnswered401WithABareBearerChallenge()
    {
        await using var api = Start();
        var claims = Claims(confirmed: true);
        claims["iat"] = TestTokens.Floor(DateTimeOffset.UtcNow) - 91;
        claims["exp"] = (long)claims["iat"] + 3600;
        var token = Sign(claims);
        var before = await database.AccessRowsAsync();

        using var response = await api.PostAsync(Body(Guid.CreateVersion7(), "Kouassi BTP", "Siège"), token);

        Refusal.AssertBare401(response);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(before, await database.AccessRowsAsync());
        Refusal.AssertLogged(_logs, "token older than the bound", token);
    }

    /// <summary>
    /// BP001-AC-17, host part: a founding through the endpoint whose transaction fails before it
    /// commits is answered 500 with no body, nothing written; the same request sent again is answered
    /// 201. While it processes both, the host sends no outbound request other than to the test JWKS,
    /// and none carries a credential.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure is raised by the database while the founding's one transaction commits, after its
    /// six inserts, by a deferred constraint trigger the test adds as the superuser (see
    /// <see cref="FoundingDatabase.FailCommitOfAsync"/>): the host and the module run unchanged. It is
    /// not the integration test's interceptor, which fails before <c>COMMIT</c> is sent
    /// (<c>FoundingStoreTests</c>); the host's tests cannot reach the module's context to attach one.
    /// </para>
    /// <para>
    /// Outbound requests are watched twice. Every client of the host's <see cref="IHttpClientFactory"/>
    /// records its requests, so that a client a module registered would be seen. An
    /// <see cref="HttpClient"/> built outside the factory sends over the network, which starts an
    /// activity of <see cref="HttpClientSource"/>: a listener samples and counts only those started in
    /// this test's flow, which the test server preserves into the request it processes, so that other
    /// tests' requests are left untraced. Both watch the requests' processing: work the host would
    /// start outside it, after answering or on a flow that does not carry the test's, is not seen.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task BP001_AC_17_AFailureBeforeCommitIsAnswered500WithNothingWrittenAndTheSameRequestThenSucceeds()
    {
        var sent = new ConcurrentQueue<OutboundRequest>();
        var network = new ConcurrentQueue<string>();
        var marker = Guid.NewGuid();
        Flow.Value = marker;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HttpClientSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                Flow.Value == marker ? ActivitySamplingResult.AllData : ActivitySamplingResult.None,
            ActivityStarted = activity =>
            {
                if (Flow.Value == marker)
                {
                    network.Enqueue(activity.OperationName);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        await AssertTheListenerSeesARequestAsync(network);

        await using var api = Start(services => services.ConfigureHttpClientDefaults(client =>
            client.AddHttpMessageHandler(() => new RecordingHandler(sent))), preserveExecutionContext: true);
        var claims = Claims(confirmed: true);
        var reference = Guid.CreateVersion7();
        var body = Body(reference, "Kouassi BTP", "Siège");
        var before = await database.AccessRowsAsync();

        await using (var failure = await database.FailCommitOfAsync(reference))
        {
            using var failed = await api.PostAsync(body, Sign(claims));

            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.Empty(await failed.Content.ReadAsByteArrayAsync(Token));
            Assert.Equal(before, await database.AccessRowsAsync());
            Assert.True(await failure.FiredAsync(), "The 500 did not come from the failure forced at commit.");
            Assert.Contains(_logs.Entries, entry => entry.Contains("Founding failed, answered 500:", StringComparison.Ordinal));
        }

        using var founded = await api.PostAsync(body, Sign(claims));

        Assert.Equal(HttpStatusCode.Created, founded.StatusCode);
        Assert.Equal(1L, await database.OrganizationsAsync(reference));

        // The host started cold: its first request fetched the keys, so the recorder is seen working.
        Assert.NotEmpty(sent);
        Assert.All(sent, request =>
        {
            Assert.Equal(TestJwks.Address, request.Uri);
            Assert.False(request.HasCredential, "A request to the JWKS carried an Authorization or apikey header.");
        });
        Assert.Empty(network);
    }

    /// <summary>
    /// BP001-AC-17, host part (ADR-ARCH-038, Consequences: the API holds no provider credential), read
    /// from what the host composes:
    /// <list type="bullet">
    /// <item>the options of its own it binds with <c>OptionsBuilder.Bind</c> are
    /// <c>AccessToken:Issuer</c>, <c>Audience</c> and <c>JwksUri</c> only, none a secret;</item>
    /// <item>it configures one HTTP client, the JWKS one, which sends no default header, so it holds
    /// no client of the provider;</item>
    /// <item>it validates tokens with no key of its own: the keys come from the JWKS only;</item>
    /// <item>and it founds a company with nothing more than those three settings.</item>
    /// </list>
    /// The requests the founding sends, and their headers, are the other AC-17 test's.
    /// Options bound with <c>BindConfiguration</c>, and direct configuration reads, are not seen here and are checked in review.
    /// </summary>
    [Fact]
    public async Task BP001_AC_17_TheHostHoldsNoIdentityProviderCredential()
    {
        List<ServiceDescriptor> composed = [];
        await using var api = Start(services => composed.AddRange(services));

        // Options the host's own code binds from configuration (OptionsBuilder.Bind registers a
        // NamedConfigureFromConfigurationOptions), and the properties each one holds.
        var bound = composed
            .Where(service => service.ServiceType.IsGenericType
                && service.ServiceType.GetGenericTypeDefinition() == typeof(IConfigureOptions<>)
                && service.ImplementationInstance?.GetType() is { IsGenericType: true } instance
                && instance.GetGenericTypeDefinition() == typeof(NamedConfigureFromConfigurationOptions<>))
            .Select(service => service.ServiceType.GetGenericArguments()[0])
            .Where(options => options.Namespace?.StartsWith("SaasBtp", StringComparison.Ordinal) == true)
            .Distinct()
            .ToDictionary(
                options => options.FullName!,
                options => options.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        var accessToken = Assert.Single(bound);
        Assert.Equal("SaasBtp.Api.Authentication.AccessTokenOptions", accessToken.Key);
        Assert.Equal(["Audience", "Issuer", "JwksUri"], accessToken.Value);

        // Every HTTP client the host configures, by name (ConfigureHttpClient and
        // ConfigurePrimaryHttpMessageHandler register named HttpClientFactoryOptions).
        var clients = composed
            .Where(service => service.ServiceType == typeof(IConfigureOptions<HttpClientFactoryOptions>))
            .Select(service => (service.ImplementationInstance as ConfigureNamedOptions<HttpClientFactoryOptions>)?.Name)
            .Distinct()
            .ToArray();
        Assert.Equal([TestJwks.ClientName], clients);

        var parameters = api.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        Assert.Null(parameters.IssuerSigningKey);
        Assert.Empty(parameters.IssuerSigningKeys ?? []);
        Assert.Null(parameters.IssuerSigningKeyResolver);
        Assert.Null(parameters.TokenDecryptionKey);
        Assert.Empty(parameters.TokenDecryptionKeys ?? []);
        using var jwks = api.Services.GetRequiredService<IHttpClientFactory>().CreateClient(TestJwks.ClientName);
        Assert.Empty(jwks.DefaultRequestHeaders);

        using var response = await api.PostAsync(Body(Guid.CreateVersion7(), "Kouassi BTP", "Siège"), Sign(Claims(confirmed: true)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public void Dispose()
    {
        _key.Dispose();
        _logs.Dispose();
    }

    /// <summary>
    /// Sends, in this test's flow, one request through a handler that refuses to connect, and checks
    /// that the listener counted it: an empty count afterwards then means that nothing was sent.
    /// </summary>
    private static async Task AssertTheListenerSeesARequestAsync(ConcurrentQueue<string> network)
    {
        using var probe = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = (_, _) => throw new IOException("The test's probe opens no connection."),
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => probe.GetAsync(new Uri("http://probe.test/"), Token));

        Assert.Single(network);
        network.Clear();
    }

    /// <summary>The claims of a valid token issued now, with <c>contact_confirmed</c> as given, or without it when <see langword="null"/>.</summary>
    private static Dictionary<string, object> Claims(bool? confirmed, Guid? person = null)
    {
        var claims = TestTokens.Claims();
        if (person is { } subject)
        {
            claims["sub"] = subject.ToString("D");
        }

        if (confirmed is { } level)
        {
            claims["contact_confirmed"] = level;
        }

        return claims;
    }

    private static Dictionary<string, object> With(Dictionary<string, object> claims, string name, object value)
    {
        claims[name] = value;
        return claims;
    }

    private static Dictionary<string, object> Without(Dictionary<string, object> claims, string name)
    {
        claims.Remove(name);
        return claims;
    }

    /// <summary>Expired two minutes ago, beyond the 30-second skew, after a lifetime of one hour.</summary>
    private static Dictionary<string, object> Expired(Dictionary<string, object> claims)
    {
        claims["exp"] = TestTokens.Floor(DateTimeOffset.UtcNow) - 120;
        claims["iat"] = (long)claims["exp"] - 3600;
        return claims;
    }

    private static string Body(Guid reference, string organizationName, string workspaceName, Guid? founderId = null)
    {
        var body = new Dictionary<string, object>
        {
            ["organizationId"] = reference,
            ["organizationName"] = organizationName,
            ["workspaceName"] = workspaceName,
        };
        if (founderId is { } founder)
        {
            body["founderId"] = founder;
        }

        return JsonSerializer.Serialize(body);
    }

    /// <summary>The fields a 422 names, in its order (ADR-ARCH-040).</summary>
    private static async Task<IReadOnlyList<string>> ValidationFieldsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return [.. problem.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name)];
    }

    private string Sign(Dictionary<string, object> claims) => TestTokens.Sign(claims, _key);

    /// <summary>
    /// The API on a test server; <paramref name="services"/>, when given, runs on the host's service
    /// collection once the host and the factory have registered theirs.
    /// </summary>
    private FoundingHost Start(Action<IServiceCollection>? services = null, bool preserveExecutionContext = false)
    {
        var factory = new ApiFactory(database.Api, _logs) { Jwks = new TestJwks(_key) };
        WebApplicationFactory<Program> server = services is null
            ? factory
            : factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services));
        server.Server.PreserveExecutionContext = preserveExecutionContext;
        return new FoundingHost(factory, server);
    }

    /// <summary>One request the host sent through a client of its factory.</summary>
    private sealed record OutboundRequest(string? Uri, bool HasCredential);

    /// <summary>Records each request of a client of the host's factory, then passes it on.</summary>
    private sealed class RecordingHandler(ConcurrentQueue<OutboundRequest> sent) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sent.Enqueue(new OutboundRequest(
                request.RequestUri?.AbsoluteUri,
                request.Headers.Authorization is not null || request.Headers.Contains("apikey")));
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>The API on a test server, posting to the founding operation.</summary>
    private sealed class FoundingHost(ApiFactory factory, WebApplicationFactory<Program> server) : IAsyncDisposable
    {
        private readonly HttpClient _client = server.CreateClient();

        public IServiceProvider Services => server.Services;

        public async Task<HttpResponseMessage> PostAsync(string body, string? token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Path, UriKind.Relative))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return await _client.SendAsync(request, Token);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            if (!ReferenceEquals(server, factory))
            {
                await server.DisposeAsync();
            }

            await factory.DisposeAsync();
        }
    }
}

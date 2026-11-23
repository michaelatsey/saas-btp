using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SaasBtp.Testing;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// <c>POST /api/v1/organizations</c> on the host (contract.md §2, ADR-ARCH-040): the 400 answers of
/// the body, the order of the checks, the bodies of 422 and 403, and 400, 401 and 500 without
/// body; every refusal logged with its class and without the token. Each runs on the production
/// validation path with a recent ES256 token (ADR-ARCH-038 decision 9), against a database
/// migrated by the runner, the API connected as <c>saasbtp_api</c>; rows are counted as the
/// container's superuser.
/// </summary>
/// <remarks>
/// BP-001's host proofs (BP001-AC-06 to BP001-AC-11 and BP001-AC-23, contract.md §7), and the host
/// part of BP001-AC-17 moved from #218, are <see cref="FoundCompanyScenarioTests"/>.
/// </remarks>
public sealed class FoundCompanyEndpointTests(FoundingDatabase database) : IClassFixture<FoundingDatabase>, IDisposable
{
    private const string Path = "/api/v1/organizations";

    private const string ProblemJson = "application/problem+json";

    /// <summary>The whole <c>Content-Type</c> of a problem details answer.</summary>
    private const string ProblemContentType = "application/problem+json";

    private readonly TestKey _key = new("published");

    private readonly CapturedLogs _logs = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("{\"organizationId\":")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task ABodyThatCannotBeReadIsAnswered400WithNoBodyAndNothingWritten(string body)
    {
        await using var api = Start();
        var claims = Claims(confirmed: true);

        using var response = await api.PostAsync(body, Sign(claims));

        await AssertNoBodyAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(0L, await database.ProfilesOfAsync(Subject(claims)));
        Assert.Contains(_logs.Entries, entry => entry.Contains("answered 400: body unreadable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABodyThatIsNotJsonIsAnswered400WithNoBodyAndNothingWritten()
    {
        await using var api = Start();
        var claims = Claims(confirmed: true);

        using var response = await api.PostAsync(
            Body(Guid.CreateVersion7(), "Kouassi BTP", "Siège"), Sign(claims), mediaType: "text/plain");

        await AssertNoBodyAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(0L, await database.ProfilesOfAsync(Subject(claims)));
        Assert.Contains(_logs.Entries, entry => entry.Contains("answered 400: body not JSON", StringComparison.Ordinal));
    }

    /// <summary>
    /// Authentication comes before the body whatever its content type (ADR-ARCH-038 decision 1): a
    /// body sent as <c>text/plain</c> without a token, or with a founding token issued 91 seconds
    /// before (decision 6), is answered 401 with no body, never the 400 of the body, nor a 415.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ABodyNotSentAsJsonWithoutAValidRecentTokenIsAnswered401WithNoBody(bool staleToken)
    {
        await using var api = Start();
        string? token = null;
        if (staleToken)
        {
            var claims = Claims(confirmed: true);
            var issuedAt = TestTokens.Floor(DateTimeOffset.UtcNow) - 91;
            claims["iat"] = issuedAt;
            claims["exp"] = issuedAt + 3600;
            token = Sign(claims);
        }

        var reference = Guid.CreateVersion7();
        using var response = await api.PostAsync(Body(reference, "Kouassi BTP", "Siège"), token, mediaType: "text/plain");

        await AssertNoBodyAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(0L, await database.OrganizationsAsync(reference));
    }

    [Theory]
    [InlineData("organizationName", "42")]
    [InlineData("workspaceName", "true")]
    [InlineData("organizationName", "{\"value\":\"Kouassi BTP\"}")]
    [InlineData("workspaceName", "[\"Siège\"]")]
    public async Task ANameOfTheWrongJsonTypeIsAnswered400WithNoBodyAndNothingWritten(string field, string value)
    {
        await using var api = Start();
        var claims = Claims(confirmed: true);
        var reference = Guid.CreateVersion7();
        var organizationName = field == "organizationName" ? value : "\"Kouassi BTP\"";
        var workspaceName = field == "workspaceName" ? value : "\"Siège\"";

        using var response = await api.PostAsync(
            $$"""{"organizationId":"{{reference:D}}","organizationName":{{organizationName}},"workspaceName":{{workspaceName}}}""", Sign(claims));

        await AssertNoBodyAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(0L, await database.OrganizationsAsync(reference));
        Assert.Equal(0L, await database.ProfilesOfAsync(Subject(claims)));
        Assert.Contains(_logs.Entries, entry => entry.Contains("answered 400: body not bound", StringComparison.Ordinal));
    }

    /// <summary>
    /// A body the server fails to read below JSON, as Kestrel reports a body over its size limit or a
    /// malformed chunk (<c>BadHttpRequestException</c>) or a transport failure (<c>IOException</c>):
    /// the test server enforces neither, so the request body is replaced by one that throws.
    /// </summary>
    [Theory]
    [InlineData(FaultyBody.BadRequest)]
    [InlineData(FaultyBody.Io)]
    public async Task ABodyTheServerFailsToReadIsAnswered400WithNoBody(string fault)
    {
        await using var api = Start(faultyBody: true);
        var claims = Claims(confirmed: true);

        using var response = await api.PostAsync(Body(Guid.CreateVersion7(), "Kouassi BTP", "Siège"), Sign(claims), fault: fault);

        await AssertNoBodyAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(0L, await database.ProfilesOfAsync(Subject(claims)));
        Assert.Contains(_logs.Entries, entry => entry.Contains("answered 400: body unreadable", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Entries, entry => entry.Contains("answered 500", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"not-a-uuid\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task AnOrganizationIdThatIsNotAUuidIsAnswered400WithNoBodyAndNothingWritten(string? organizationId)
    {
        await using var api = Start();
        var claims = Claims(confirmed: true);
        var body = organizationId is null
            ? """{"organizationName":"Kouassi BTP","workspaceName":"Siège"}"""
            : $$"""{"organizationId":{{organizationId}},"organizationName":"Kouassi BTP","workspaceName":"Siège"}""";

        using var response = await api.PostAsync(body, Sign(claims));

        await AssertNoBodyAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(0L, await database.ProfilesOfAsync(Subject(claims)));
        Assert.Contains(_logs.Entries, entry => entry.Contains("answered 400: organizationId not a UUID", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AUuidThatIsNotVersion7IsAnswered422NamingOnlyOrganizationId()
    {
        await using var api = Start();
        var claims = Claims(confirmed: true);
        var reference = Guid.NewGuid();

        using var response = await api.PostAsync(Body(reference, "Kouassi BTP", "Siège"), Sign(claims));

        var errors = await AssertValidationProblemAsync(response);
        Assert.Equal(["organizationId"], errors.EnumerateObject().Select(field => field.Name));
        Assert.Equal(0L, await database.OrganizationsAsync(reference));
        Assert.Equal(0L, await database.ProfilesOfAsync(Subject(claims)));
        Assert.Contains(_logs.Entries, entry => entry.Contains("answered 422: organizationId", StringComparison.Ordinal));
    }

    /// <summary>The 422 of each field, byte for byte as ADR-ARCH-040 decision 4 writes it.</summary>
    [Theory]
    [InlineData("organizationId", "The reference must be a UUIDv7.")]
    [InlineData("organizationName", "The company name must hold 1 to 200 characters.")]
    [InlineData("workspaceName", "The first workspace name must hold 1 to 200 characters.")]
    public async Task The422BodyIsExactlyTheOneOfADR040(string field, string message)
    {
        await using var api = Start();
        var body = Body(
            field == "organizationId" ? Guid.NewGuid() : Guid.CreateVersion7(),
            field == "organizationName" ? "" : "Kouassi BTP",
            field == "workspaceName" ? "" : "Siège");

        using var response = await api.PostAsync(body, Sign(Claims(confirmed: true)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ProblemContentType, response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            $$$"""{"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"One or more validation errors occurred.","status":422,"errors":{"{{{field}}}":["{{{message}}}"]}}""",
            await response.Content.ReadAsStringAsync(Token));
    }

    /// <summary>The 403, byte for byte as ADR-ARCH-040 decision 5 writes it.</summary>
    [Fact]
    public async Task The403BodyIsExactlyTheOneOfADR040()
    {
        await using var api = Start();

        using var response = await api.PostAsync(Body(Guid.CreateVersion7(), "Kouassi BTP", "Siège"), Sign(Claims(confirmed: false)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemContentType, response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"The contact must be confirmed.","status":403}""",
            await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task AMissingNameAndANameTooLongGetTheSameBodyNamingOnlyTheField()
    {
        await using var api = Start();
        var claims = Claims(confirmed: true);

        using var missing = await api.PostAsync(Body(Guid.CreateVersion7(), "   ", "Siège"), Sign(claims));
        using var tooLong = await api.PostAsync(Body(Guid.CreateVersion7(), new string('a', 201), "Siège"), Sign(claims));

        var missingErrors = await AssertValidationProblemAsync(missing);
        var tooLongErrors = await AssertValidationProblemAsync(tooLong);
        Assert.Equal(["organizationName"], missingErrors.EnumerateObject().Select(field => field.Name));
        Assert.Equal(missingErrors.GetRawText(), tooLongErrors.GetRawText());

        var message = Assert.Single(missingErrors.GetProperty("organizationName").EnumerateArray()).GetString()!;
        Assert.DoesNotContain("missing", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("too long", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0L, await database.ProfilesOfAsync(Subject(claims)));
    }

    [Fact]
    public async Task ACallerWithoutTheLevelIsAnswered403SayingOnlyThatTheContactMustBeConfirmed()
    {
        await using var api = Start();
        var claims = Claims(confirmed: false);
        var reference = Guid.CreateVersion7();

        using var response = await api.PostAsync(Body(reference, "Kouassi BTP", "Siège"), Sign(claims));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("The contact must be confirmed.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(403, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(["type", "title", "status"], problem.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(0L, await database.OrganizationsAsync(reference));
        Assert.Equal(0L, await database.ProfilesOfAsync(Subject(claims)));
        Assert.Contains(_logs.Entries, entry => entry.Contains("answered 403: contact not confirmed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABodyCheckedBeforeTheLevelIsAnswered422WithoutTheLevel()
    {
        await using var api = Start();

        using var response = await api.PostAsync(Body(Guid.CreateVersion7(), "", "Siège"), Sign(Claims(confirmed: false)));

        Assert.Equal(["organizationName"], (await AssertValidationProblemAsync(response)).EnumerateObject().Select(field => field.Name));
    }

    /// <summary>
    /// The founding is bound to the host's recent-token policy (ADR-ARCH-038 decision 6): a token
    /// valid for an hour but issued more than 90 seconds before is answered 401, never 403, even by a
    /// caller holding the level, and nothing is written.
    /// </summary>
    [Fact]
    public async Task AFoundingTokenIssuedMoreThan90SecondsBeforeIsAnswered401WithNoBody()
    {
        await using var api = Start();
        var claims = Claims(confirmed: true);
        var issuedAt = TestTokens.Floor(DateTimeOffset.UtcNow) - 91;
        claims["iat"] = issuedAt;
        claims["exp"] = issuedAt + 3600;
        var reference = Guid.CreateVersion7();

        using var response = await api.PostAsync(Body(reference, "Kouassi BTP", "Siège"), Sign(claims));

        await AssertNoBodyAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(0L, await database.OrganizationsAsync(reference));
        Assert.Equal(0L, await database.ProfilesOfAsync(Subject(claims)));
        Assert.Contains(_logs.Entries, entry => entry.Contains("token older than the bound", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoTokenIsAnswered401WithNoBody()
    {
        await using var api = Start();

        using var response = await api.PostAsync(Body(Guid.CreateVersion7(), "Kouassi BTP", "Siège"), token: null);

        await AssertNoBodyAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task AFoundingIsAnswered201WithTheOrganizationAndItsLocation()
    {
        await using var api = Start();
        var claims = Claims(confirmed: true);
        var reference = Guid.CreateVersion7();

        using var response = await api.PostAsync(Body(reference, "  Kouassi BTP ", "Siège"), Sign(claims));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{Path}/{reference:D}", response.Headers.Location?.ToString());
        using var organization = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(reference, organization.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Kouassi BTP", organization.RootElement.GetProperty("name").GetString());
        Assert.Equal("declared", organization.RootElement.GetProperty("status").GetString());
        Assert.Equal(3, organization.RootElement.EnumerateObject().Count());
        Assert.Equal(1L, await database.OrganizationsAsync(reference));
        Assert.Equal(1L, await database.ProfilesOfAsync(Subject(claims)));
    }

    [Theory]
    [InlineData("/api/v1/organizations/")]
    [InlineData("/API/V1/Organizations")]
    public async Task TheLocationNamesTheOrganizationWhateverThePathMatched(string path)
    {
        await using var api = Start();
        var reference = Guid.CreateVersion7();

        using var response = await api.PostAsync(Body(reference, "Kouassi BTP", "Siège"), Sign(Claims(confirmed: true)), path);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{Path}/{reference:D}", response.Headers.Location?.ToString());
    }

    /// <summary>
    /// Any other failure (contract.md §2): the API pointed at a database it cannot reach, a Unix socket
    /// directory that does not exist, answers 500 with no body, logged with its class.
    /// </summary>
    [Fact]
    public async Task AFailureIsAnswered500WithNoBodyAndLoggedWithItsClass()
    {
        var factory = new ApiFactory(UnreachableDatabase(), _logs) { Jwks = new TestJwks(_key) };
        await using var api = new FoundingApi(factory, factory);
        var reference = Guid.CreateVersion7();

        using var failed = await api.PostAsync(Body(reference, "Kouassi BTP", "Siège"), Sign(Claims(confirmed: true)));

        await AssertNoBodyAsync(failed, HttpStatusCode.InternalServerError);
        var logged = Assert.Single(_logs.Entries, entry => entry.Contains("answered 500:", StringComparison.Ordinal));
        Assert.Contains("PersistenceException", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoRefusalLogHoldsTheToken()
    {
        await using var api = Start();
        var token = Sign(Claims(confirmed: false));

        using var unreadable = await api.PostAsync("not json", token);
        using var invalid = await api.PostAsync(Body(Guid.NewGuid(), "", ""), token);
        using var forbidden = await api.PostAsync(Body(Guid.CreateVersion7(), "Kouassi BTP", "Siège"), token);

        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Forbidden],
            [unreadable.StatusCode, invalid.StatusCode, forbidden.StatusCode]);
        Assert.Contains(_logs.Entries, entry => entry.Contains("answered 422: organizationId, organizationName, workspaceName", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Entries, entry => entry.Contains(TestTokens.Payload(token), StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _key.Dispose();
        _logs.Dispose();
    }

    private static Dictionary<string, object> Claims(bool confirmed)
    {
        var claims = TestTokens.Claims();
        claims["contact_confirmed"] = confirmed;
        return claims;
    }

    private static Guid Subject(Dictionary<string, object> claims) => Guid.Parse((string)claims["sub"]);

    private static string Body(Guid reference, string organizationName, string workspaceName) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["organizationId"] = reference,
            ["organizationName"] = organizationName,
            ["workspaceName"] = workspaceName,
        });

    private static async Task AssertNoBodyAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
    }

    /// <summary>A 422 in validation problem details (ADR-ARCH-040); returns its <c>errors</c>.</summary>
    private static async Task<JsonElement> AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(422, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("https://tools.ietf.org/html/rfc4918#section-11.2", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("One or more validation errors occurred.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(["type", "title", "status", "errors"], problem.RootElement.EnumerateObject().Select(property => property.Name));
        return problem.RootElement.GetProperty("errors").Clone();
    }

    /// <summary>
    /// A connection string to a Unix socket in a directory that does not exist: no process can serve
    /// it, so the connection fails at once, whatever runs beside the test.
    /// </summary>
    private static string UnreachableDatabase() =>
        $"Host={System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"no-database-{Guid.NewGuid():N}")};Database=postgres;Username=saasbtp_api;Password=unused;Timeout=2";

    private string Sign(Dictionary<string, object> claims) => TestTokens.Sign(claims, _key);

    private FoundingApi Start(bool faultyBody = false)
    {
        var factory = new ApiFactory(database.Api, _logs) { Jwks = new TestJwks(_key) };
        return faultyBody
            ? new(factory, factory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, FaultyBody>())))
            : new(factory, factory);
    }

    /// <summary>The API on a test server, posting to the founding operation.</summary>
    private sealed class FoundingApi(ApiFactory factory, WebApplicationFactory<Program> server) : IAsyncDisposable
    {
        private readonly HttpClient _client = server.CreateClient();

        public async Task<HttpResponseMessage> PostAsync(
            string body, string? token, string path = Path, string? fault = null, string mediaType = "application/json")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType),
            };
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            if (fault is not null)
            {
                request.Headers.Add(FaultyBody.Header, fault);
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

    /// <summary>
    /// Replaces the body of a request carrying <see cref="Header"/> by a stream that throws as
    /// Kestrel does when it cannot read a body, before the host's pipeline runs.
    /// </summary>
    private sealed class FaultyBody : IStartupFilter
    {
        public const string Header = "X-Test-Body-Fault";

        public const string BadRequest = "bad-request";

        public const string Io = "io";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, inner) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var fault))
                {
                    context.Request.Body = new ThrowingStream(fault.ToString() == BadRequest
                        ? new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge)
                        : new IOException("The connection was reset."));
                }

                await inner(context);
            });
            next(app);
        };
    }

    private sealed class ThrowingStream(Exception failure) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw failure;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(failure);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(failure);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>One database migrated by the runner for the founding's host tests.</summary>
public sealed class FoundingDatabase : IAsyncLifetime
{
    private MigratedDatabase? _migrated;

    private RolesDatabase Database =>
        _migrated?.Database ?? throw new InvalidOperationException("The database is not started.");

    /// <summary>The connection string of <c>saasbtp_api</c>, the API's role.</summary>
    internal string Api => Database.ApiConnectionString;

    public async ValueTask InitializeAsync() =>
        _migrated = await MigratedDatabase.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _migrated?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>The organizations under <paramref name="reference"/>, counted past row level security.</summary>
    internal Task<long> OrganizationsAsync(Guid reference) =>
        CountAsync("select count(*) from access.organizations where id = @id", reference);

    /// <summary>The profiles of <paramref name="user"/>, counted past row level security.</summary>
    internal Task<long> ProfilesOfAsync(Guid user) =>
        CountAsync("select count(*) from access.profiles where user_id = @id", user);

    /// <summary>
    /// The rows of each of the six Access tables, in the order profiles, ownerships, organizations,
    /// memberships, workspace accesses, workspaces, counted past row level security: equal before
    /// and after a request, they show that it wrote nothing, on a database no other test writes to
    /// meanwhile.
    /// </summary>
    internal async Task<long[]> AccessRowsAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select (select count(*) from access.profiles),
                   (select count(*) from access.ownerships),
                   (select count(*) from access.organizations),
                   (select count(*) from access.memberships),
                   (select count(*) from access.workspace_accesses),
                   (select count(*) from access.workspaces)
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetInt64)];
    }

    /// <summary>The identifier of the profile of <paramref name="user"/>, read past row level security.</summary>
    internal async Task<Guid> ProfileIdOfAsync(Guid user) =>
        Assert.Single(await GuidsAsync("select id from access.profiles where user_id = @id", user));

    /// <summary>
    /// The people related to the organization <paramref name="reference"/>, each as the provider's
    /// user identifier of their profile, read past row level security.
    /// </summary>
    internal async Task<OrganizationRelations> RelationsOfAsync(Guid reference) => new(
        CurrentOwner: await GuidsAsync(
            """
            select p.user_id from access.organizations o
            join access.ownerships w on w.id = o.current_ownership_id
            join access.profiles p on p.id = w.profile_id
            where o.id = @id
            """, reference),
        Owners: await GuidsAsync(
            "select p.user_id from access.ownerships w join access.profiles p on p.id = w.profile_id where w.organization_id = @id",
            reference),
        Members: await GuidsAsync(
            "select p.user_id from access.memberships m join access.profiles p on p.id = m.profile_id where m.organization_id = @id",
            reference),
        Authorized: await GuidsAsync(
            "select p.user_id from access.workspace_accesses a join access.profiles p on p.id = a.profile_id where a.organization_id = @id",
            reference),
        AccessWorkspaces: await GuidsAsync("select workspace_id from access.workspace_accesses where organization_id = @id", reference),
        Workspaces: await GuidsAsync("select id from access.workspaces where organization_id = @id", reference));

    /// <summary>
    /// Makes the commit of any transaction that inserts the first workspace of
    /// <paramref name="reference"/> fail: a deferred constraint trigger on <c>access.workspaces</c>,
    /// created as the container's superuser, raises an exception when the transaction commits,
    /// after its inserts, so that the transaction rolls back whole. Before it raises, it advances a
    /// sequence, which a rollback does not undo, so that the test can tell that this failure, and no
    /// earlier one, failed the transaction. The function runs as its owner, the superuser, so that
    /// <c>saasbtp_api</c> needs no grant on the sequence. Disposing the result drops the trigger,
    /// its function and the sequence.
    /// </summary>
    internal async Task<ForcedCommitFailure> FailCommitOfAsync(Guid reference)
    {
        var function = $"public.fail_commit_{reference:N}";
        var fired = $"public.fail_commit_{reference:N}_fired";
        await ExecuteAsync(
            $$"""
            create sequence {{fired}};
            create function {{function}}() returns trigger language plpgsql
              security definer set search_path = pg_catalog as $$
            begin
              perform nextval('{{fired}}');
              raise exception 'A failure forced before commit by the test.';
            end
            $$;
            create constraint trigger fail_commit_{{reference:N}}
              after insert on access.workspaces
              deferrable initially deferred
              for each row when (new.organization_id = '{{reference:D}}'::uuid)
              execute function {{function}}();
            """);

        return new ForcedCommitFailure(
            async () => (bool)(await ScalarAsync($"select is_called from {fired}"))!,
            () => ExecuteAsync($"drop function {function}() cascade; drop sequence {fired};"));
    }

    private async Task<long> CountAsync(string sql, Guid id)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<List<Guid>> GuidsAsync(string sql, Guid id)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<Guid>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetGuid(0));
        }

        return values;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Database.SuperuserConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}

/// <summary>The failure <see cref="FoundingDatabase.FailCommitOfAsync"/> forces; disposing it removes it.</summary>
internal sealed class ForcedCommitFailure(Func<Task<bool>> fired, Func<Task> drop) : IAsyncDisposable
{
    /// <summary>Whether the trigger has raised its failure at least once.</summary>
    public Task<bool> FiredAsync() => fired();

    public async ValueTask DisposeAsync() => await drop();
}

/// <summary>The people related to one organization, each as their user identifier, and its workspaces.</summary>
internal sealed record OrganizationRelations(
    IReadOnlyList<Guid> CurrentOwner,
    IReadOnlyList<Guid> Owners,
    IReadOnlyList<Guid> Members,
    IReadOnlyList<Guid> Authorized,
    IReadOnlyList<Guid> AccessWorkspaces,
    IReadOnlyList<Guid> Workspaces);

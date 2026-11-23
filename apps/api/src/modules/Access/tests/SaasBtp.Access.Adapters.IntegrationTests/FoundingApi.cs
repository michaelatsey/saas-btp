using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The Access module's endpoints on a test server, connected as <c>saasbtp_api</c> to the migrated
/// database, so that a founding and the reads are answered by the module's own endpoints, handlers
/// and adapters.
/// </summary>
/// <remarks>
/// What the API host provides around the module is stood in for, and only that: the caller is
/// authenticated by a test scheme whose principal carries <c>sub</c> and the boolean
/// <c>contact_confirmed</c>, the claims the caller adapter reads (ADR-ARCH-038 decision 5); the
/// <c>RecentToken</c> policy requires an authenticated caller; the subject port reads <c>sub</c>
/// from the request, as the host's does. Validating the provider's tokens is the host's, proven by
/// its own tests.
/// </remarks>
internal sealed class FoundingApi : IAsyncDisposable
{
    public const string Path = "/api/v1/organizations";

    private readonly WebApplication _app;

    private readonly HttpClient _client;

    private FoundingApi(WebApplication app, EndpointLogs logs)
    {
        _app = app;
        _client = app.GetTestClient();
        Logs = logs;
    }

    /// <summary>What the module logged, in order.</summary>
    public EndpointLogs Logs { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Starts the module on <paramref name="connectionString"/>; <paramref name="configure"/> adds to it, such as an interceptor.</summary>
    public static async Task<FoundingApi> StartAsync(string connectionString, Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SaasBtp"] = connectionString });

        var logs = new EndpointLogs();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);

        builder.Services.AddAuthentication(TestCaller.SchemeName).AddScheme<AuthenticationSchemeOptions, TestCaller>(TestCaller.SchemeName, _ => { });
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(TestCaller.SchemeName).RequireAuthenticatedUser().Build())
            .AddPolicy("RecentToken", policy => policy.AddAuthenticationSchemes(TestCaller.SchemeName).RequireAuthenticatedUser());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IUnitOfWorkSubject, RequestSubject>();
        builder.Services.AddAccessModule(builder.Configuration);
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("/api/v1").MapAccessEndpoints();
        await app.StartAsync(Token);
        return new FoundingApi(app, logs);
    }

    /// <summary>Submits a founding request as <paramref name="caller"/>, who holds the confirmed-contact level.</summary>
    public async Task<Answer> FoundAsync(Guid caller, Guid reference, string organizationName, string workspaceName)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["organizationId"] = reference,
            ["organizationName"] = organizationName,
            ["workspaceName"] = workspaceName,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Path, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TestCaller.SubjectHeader, caller.ToString("D"));

        return await SendAsync(request);
    }

    /// <summary>Sends <c>GET</c> on <paramref name="path"/> as <paramref name="caller"/>.</summary>
    public async Task<Answer> GetAsync(Guid caller, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add(TestCaller.SubjectHeader, caller.ToString("D"));
        return await SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<Answer> SendAsync(HttpRequestMessage request)
    {
        using var response = await _client.SendAsync(request, Token);
        return new Answer(
            response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            await response.Content.ReadAsStringAsync(Token));
    }
}

/// <summary>An answer of the founding operation.</summary>
internal sealed record Answer(HttpStatusCode Status, string? ContentType, string Body)
{
    /// <summary>The organization the body represents: <c>id</c>, <c>name</c>, <c>status</c>, and nothing else.</summary>
    public (Guid Id, string Name, string Status) Organization
    {
        get
        {
            using var document = JsonDocument.Parse(Body);
            var root = document.RootElement;
            Assert.Equal(["id", "name", "status"], root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            return (root.GetProperty("id").GetGuid(), root.GetProperty("name").GetString()!, root.GetProperty("status").GetString()!);
        }
    }

    /// <summary>The workspaces the body lists, in its order: each <c>id</c>, <c>name</c>, <c>organizationId</c>, and nothing else.</summary>
    public IReadOnlyList<(Guid Id, string Name, Guid OrganizationId)> Workspaces
    {
        get
        {
            using var document = JsonDocument.Parse(Body);
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
            return
            [
                .. document.RootElement.EnumerateArray().Select(workspace =>
                {
                    Assert.Equal(["id", "name", "organizationId"], workspace.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
                    return (workspace.GetProperty("id").GetGuid(), workspace.GetProperty("name").GetString()!, workspace.GetProperty("organizationId").GetGuid());
                }),
            ];
        }
    }
}

/// <summary>
/// Authenticates the caller named by <see cref="SubjectHeader"/> with the confirmed-contact level, as
/// the host does from a validated token: <c>sub</c>, and <c>contact_confirmed</c> as a boolean.
/// </summary>
internal sealed class TestCaller(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    public const string SubjectHeader = "X-Test-Subject";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubjectHeader, out var subject))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim("sub", subject.ToString()), new Claim("contact_confirmed", "true", ClaimValueTypes.Boolean)],
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

/// <summary>The subject port, from the <c>sub</c> of the authenticated request, as the host reads it.</summary>
internal sealed class RequestSubject(IHttpContextAccessor requests) : IUnitOfWorkSubject
{
    public Guid? Current =>
        requests.HttpContext?.User is { Identity.IsAuthenticated: true } user
        && Guid.TryParseExact(user.FindFirst("sub")?.Value, "D", out var subject)
            ? subject
            : null;
}

/// <summary>Keeps the message of every entry the module logs, for a test to read.</summary>
internal sealed class EndpointLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(_entries, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> entries, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (category.StartsWith("SaasBtp.", StringComparison.Ordinal))
            {
                entries.Enqueue($"{formatter(state, exception)} {exception}");
            }
        }
    }
}

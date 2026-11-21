using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SaasBtp.Api.Authentication;

/// <summary>
/// Fetches the JWKS document over https, through the named <see cref="ClientName"/> client, a
/// new client from the factory at each fetch.
/// </summary>
internal sealed class JwksDocumentRetriever(IHttpClientFactory clients) : IDocumentRetriever
{
    public const string ClientName = "Jwks";

    /// <summary>
    /// The bound on one HTTP request to the JWKS address.
    /// </summary>
    /// <remarks>
    /// It bounds a request, not the wait of the caller. Before the first successful fetch, the
    /// fetch runs on the request path, one at a time: IdentityModel's manager serializes them
    /// behind one lock, and its document retriever repeats a request answered 408 or 503 once. With
    /// a JWKS address that does not answer, each request that carries a token waits for the fetches
    /// of the requests queued before it, then for its own, before its 500.
    /// </remarks>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>A key set is a few kilobytes; anything near this is not one.</summary>
    public const long MaxDocumentSize = 1024 * 1024;

    public Task<string> GetDocumentAsync(string address, CancellationToken cancel) =>
        new HttpDocumentRetriever(clients.CreateClient(ClientName)) { RequireHttps = true }
            .GetDocumentAsync(address, cancel);
}

/// <summary>
/// Turns the JWKS document into the configuration the JWT bearer handler reads: the configured
/// issuer and the signing keys of the set (ADR-ARCH-038 decision 2). No discovery document is
/// read.
/// </summary>
/// <remarks>
/// <para>
/// Only a failure to fetch the document, or a document that is not a JSON object with a
/// <c>keys</c> array, fails the fetch: before the first successful fetch the request is then
/// answered 500, and after it the last keys stay in use.
/// </para>
/// <para>
/// A key set that parses is a successful fetch even when it holds no signing key, or none the
/// host accepts: it replaces the keys held, and every token is then refused 401. That is the
/// JWKS of a project still signing with the legacy secret, which Supabase publishes empty
/// (decision 1), and the way a revoked last key leaves at the first successful refresh.
/// </para>
/// <para>
/// The format is Supabase's ("JSON Web Token (JWT)" and "JWT Signing Keys", read 2026-11-21): a
/// <c>{"keys": [...]}</c> set whose keys carry <c>kid</c>, <c>alg</c>, <c>kty</c>,
/// <c>key_ops</c> and their public fields.
/// </para>
/// </remarks>
internal sealed class JwksConfigurationRetriever(string issuer) : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var document = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);

        if (!HoldsAKeyArray(document))
        {
            throw new InvalidDataException("The JWKS document is not a JSON object with a \"keys\" array.");
        }

        var keySet = new JsonWebKeySet(document);
        var configuration = new OpenIdConnectConfiguration { Issuer = issuer, JsonWebKeySet = keySet };
        foreach (var key in keySet.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }

    private static bool HoldsAKeyArray(string document)
    {
        try
        {
            using var json = JsonDocument.Parse(document);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("keys", out var keys)
                && keys.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// The JWT bearer handler's configuration manager: IdentityModel's
/// <see cref="ConfigurationManager{T}"/> over the JWKS, refreshed every 10 minutes and, on a
/// token signed by an unknown key, at most once every 5 minutes (ADR-ARCH-038 decision 2).
/// </summary>
/// <remarks>
/// <para>
/// It wraps the manager instead of being one, and that is its only purpose. When the handler's
/// manager is a <see cref="BaseConfigurationManager"/>, the handler passes it to the token
/// handler, which catches a failure to obtain the keys and goes on without them: a host that has
/// never fetched a key would answer 401, as if the token were invalid. Given a manager that is
/// not one, the handler obtains the keys itself, the failure reaches it, and it rethrows: the
/// request is answered 500, the host not having been able to validate the token (decision 2;
/// dotnet/aspnetcore v10.0.12 <c>JwtBearerHandler</c>, IdentityModel 8.19.2
/// <c>JsonWebTokenHandler.ValidateTokenAsync</c>).
/// </para>
/// <para>
/// The authentication middleware authenticates every request that carries a token, anonymous
/// endpoints included, so before the first fetch a token sent to <c>/api/v1/ping</c> or to a
/// health endpoint is answered 500 too. A request without a token is never affected.
/// </para>
/// <para>
/// IdentityModel adds to each automatic refresh a random delay of at most one twentieth of the
/// interval, 30 seconds: keys are held at most 10 min 30 s after a fetch. A refresh, automatic or
/// requested, runs in the background; the keys held serve the requests until it ends.
/// </para>
/// </remarks>
internal sealed class JwksConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    public static readonly TimeSpan AutomaticRefreshInterval = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ConfigurationManager<OpenIdConnectConfiguration> _keys;

    public JwksConfigurationManager(AccessTokenOptions options, IDocumentRetriever documents) =>
        _keys = new ConfigurationManager<OpenIdConnectConfiguration>(
            options.JwksUri, new JwksConfigurationRetriever(options.Issuer), documents)
        {
            AutomaticRefreshInterval = AutomaticRefreshInterval,
            RefreshInterval = RefreshInterval,
        };

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
        _keys.GetConfigurationAsync(cancel);

    public void RequestRefresh() => _keys.RequestRefresh();
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// The in-process JWKS of ADR-ARCH-038 decision 9: the key set the host fetches, published at
/// <see cref="Address"/> by an <see cref="HttpMessageHandler"/> that stands in for the network.
/// A test changes the published keys, makes the fetches fail, and counts them.
/// </summary>
internal sealed class TestJwks
{
    /// <summary>An https address, so that the host's https rule holds in the tests too.</summary>
    public const string Address = "https://jwks.test/auth/v1/.well-known/jwks.json";

    /// <summary>The name of the host's JWKS client, whose primary handler the tests replace.</summary>
    public const string ClientName = "Jwks";

    private readonly Lock _gate = new();
    private TestKey[] _published;
    private volatile bool _failing;
    private int _fetches;

    public TestJwks(params TestKey[] published) => _published = published;

    /// <summary>The number of requests the host has sent to the JWKS address.</summary>
    public int Fetches => Volatile.Read(ref _fetches);

    /// <summary>When set, every fetch is answered 500.</summary>
    public bool Failing
    {
        get => _failing;
        set => _failing = value;
    }

    public void Publish(params TestKey[] keys)
    {
        lock (_gate)
        {
            _published = keys;
        }
    }

    public HttpMessageHandler CreateHandler() => new Handler(this);

    /// <summary>Waits until the host has fetched the JWKS at least <paramref name="count"/> times.</summary>
    public async Task WaitForFetchesAsync(int count)
    {
        var deadline = DateTime.UtcNow + Eventually.Deadline;
        while (Fetches < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"The host fetched the JWKS {Fetches} time(s), not {count}, within {Eventually.Deadline}.");
            }

            await Task.Delay(Eventually.Interval, TestContext.Current.CancellationToken);
        }
    }

    private string Document()
    {
        TestKey[] keys;
        lock (_gate)
        {
            keys = _published;
        }

        return JsonSerializer.Serialize(new { keys = keys.Select(key => key.PublicJwk()).ToArray() });
    }

    private sealed class Handler(TestJwks jwks) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref jwks._fetches);

            if (jwks.Failing || request.RequestUri?.AbsoluteUri != Address)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jwks.Document(), Encoding.UTF8, "application/json"),
            });
        }
    }
}

/// <summary>A test ES256 key: the private key signs tokens, the public one is published.</summary>
internal sealed class TestKey : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public TestKey(string keyId) => KeyId = keyId;

    public string KeyId { get; }

    public SigningCredentials SigningCredentials =>
        new(new ECDsaSecurityKey(_key) { KeyId = KeyId }, SecurityAlgorithms.EcdsaSha256);

    /// <summary>
    /// The public key as the provider publishes it (Supabase, "JSON Web Token (JWT)", read
    /// 2026-11-21): <c>kid</c>, <c>alg</c>, <c>kty</c>, <c>key_ops</c> and the public key fields.
    /// </summary>
    public Dictionary<string, object> PublicJwk()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);

        return new Dictionary<string, object>
        {
            ["kid"] = KeyId,
            ["alg"] = SecurityAlgorithms.EcdsaSha256,
            ["kty"] = "EC",
            ["key_ops"] = new[] { "verify" },
            ["crv"] = "P-256",
            ["x"] = Base64UrlEncoder.Encode(parameters.Q.X),
            ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y),
        };
    }

    public void Dispose() => _key.Dispose();
}

/// <summary>The bound and the pace of the assertions that wait for a background refresh.</summary>
internal static class Eventually
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    /// <summary>Repeats <paramref name="send"/> until it answers <paramref name="expected"/>, within the deadline.</summary>
    public static async Task<HttpStatusCode> StatusAsync(Func<Task<HttpStatusCode>> send, HttpStatusCode expected, string what)
    {
        var deadline = DateTime.UtcNow + Deadline;
        while (true)
        {
            var status = await send();
            if (status == expected)
            {
                return status;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"{what}: still {(int)status}, not {(int)expected}, after {Deadline}.");
            }

            await Task.Delay(Interval, TestContext.Current.CancellationToken);
        }
    }
}

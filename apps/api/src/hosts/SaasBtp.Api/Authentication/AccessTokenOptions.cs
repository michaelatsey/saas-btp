namespace SaasBtp.Api.Authentication;

/// <summary>
/// What the host needs to validate the provider's access tokens, configuration of each
/// environment and not secrets (ADR-ARCH-038 decision 8): environment variables
/// <c>AccessToken__Issuer</c>, <c>AccessToken__Audience</c> and <c>AccessToken__JwksUri</c>.
/// </summary>
/// <remarks>
/// <para>
/// The section is not ASP.NET Core's <c>Authentication:Schemes:Bearer</c>, which the JWT bearer
/// handler would bind by itself, signing keys included.
/// </para>
/// <para>
/// All three are checked when the JWT bearer handler first reads its options, which the
/// authentication middleware does for every request, anonymous ones included: with a setting
/// missing or invalid, every request is answered 500 and the failure, naming the setting, is
/// logged, so the readiness probe reports the container unhealthy. They are not checked when the
/// host starts: the build starts the host to write the OpenAPI document (ADR-ARCH-018) without
/// these settings, and the way to tell that run apart reads the entry assembly, which
/// <c>SaasBtp.Architecture.Tests</c> forbids in a host.
/// </para>
/// </remarks>
internal sealed class AccessTokenOptions
{
    public const string Section = "AccessToken";

    /// <summary>The <c>iss</c> a token must carry: <c>https://&lt;project-ref&gt;.supabase.co/auth/v1</c>.</summary>
    public string Issuer { get; set; } = "";

    /// <summary>The <c>aud</c> a token must carry: <c>authenticated</c>.</summary>
    public string Audience { get; set; } = "";

    /// <summary>
    /// The JWKS address, read directly rather than through a discovery document (decision 2):
    /// <c>https://&lt;project-ref&gt;.supabase.co/auth/v1/.well-known/jwks.json</c>.
    /// </summary>
    public string JwksUri { get; set; } = "";

    public static bool IsHttpsUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

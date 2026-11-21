using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// Builds the access tokens the tests send, claim by claim. The payload is written as JSON, so a
/// test controls every claim, its absence included, and <c>iat</c> and <c>exp</c> are the whole
/// seconds it chooses.
/// </summary>
internal static class TestTokens
{
    public const string Issuer = "https://issuer.test/auth/v1";

    public const string Audience = "authenticated";

    /// <summary>The current time in whole seconds, rounded down: a token built from it is at least as old as stated.</summary>
    public static long Floor(DateTimeOffset now) => now.ToUnixTimeSeconds();

    /// <summary>The next whole second: a token built from it is at most as old as stated, plus the request time.</summary>
    public static long Ceiling(DateTimeOffset now) =>
        now.ToUnixTimeMilliseconds() % 1000 == 0 ? now.ToUnixTimeSeconds() : now.ToUnixTimeSeconds() + 1;

    /// <summary>The claims of a valid user access token, issued now, for one hour.</summary>
    public static Dictionary<string, object> Claims()
    {
        var issuedAt = Floor(DateTimeOffset.UtcNow);

        return new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = Guid.NewGuid().ToString("D"),
            ["iat"] = issuedAt,
            ["exp"] = issuedAt + 3600,
            ["role"] = "authenticated",
            ["is_anonymous"] = false,
        };
    }

    public static string Sign(TestKey key) => Sign(Claims(), key.SigningCredentials);

    public static string Sign(Dictionary<string, object> claims, TestKey key) => Sign(claims, key.SigningCredentials);

    public static string Sign(Dictionary<string, object> claims, SigningCredentials credentials) =>
        new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(claims), credentials);

    /// <summary>A token with <c>alg</c> <c>none</c> and an empty signature.</summary>
    public static string Unsigned(Dictionary<string, object> claims) =>
        $"{Encode("""{"alg":"none","typ":"JWT"}""")}.{Encode(JsonSerializer.Serialize(claims))}.";

    /// <summary>A token signed with a shared secret, HS256.</summary>
    public static string Hs256(Dictionary<string, object> claims) =>
        Sign(claims, new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-shared-secret-of-at-least-32-bytes!")) { KeyId = "hs256" },
            SecurityAlgorithms.HmacSha256));

    /// <summary>The payload segment of a compact token, which no log may contain either.</summary>
    public static string Payload(string token) => token.Split('.')[1];

    private static string Encode(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
}

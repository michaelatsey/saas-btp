using System.Net;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// The issuer, the audience and the JWKS address are configuration of each environment
/// (ADR-ARCH-038 decision 8). With one of them missing, or an issuer or a JWKS address that is not
/// an absolute https URI, every request is answered 500, liveness included, and the failure names
/// the setting.
/// </summary>
public sealed class AccessTokenOptionsTests
{
    [Theory]
    [InlineData("AccessToken:Issuer", "")]
    [InlineData("AccessToken:Audience", "")]
    [InlineData("AccessToken:JwksUri", "")]
    [InlineData("AccessToken:Issuer", "http://issuer.test/auth/v1")]
    [InlineData("AccessToken:JwksUri", "http://jwks.test/auth/v1/.well-known/jwks.json")]
    [InlineData("AccessToken:JwksUri", "/auth/v1/.well-known/jwks.json")]
    public async Task EveryRequestIsAnswered500WithAnInvalidSetting(string key, string value)
    {
        using var logs = new CapturedLogs();
        await using var api = new Api(new ApiFactory(databaseConnectionString: "", logs)
        {
            Settings = new Dictionary<string, string> { [key] = value },
        });

        Assert.Equal(HttpStatusCode.InternalServerError, await api.StatusAsync("/health/live", token: null));
        Assert.Contains(logs.Entries, entry =>
            entry.Contains("OptionsValidationException", StringComparison.Ordinal)
            && entry.Contains(key, StringComparison.Ordinal));
    }
}

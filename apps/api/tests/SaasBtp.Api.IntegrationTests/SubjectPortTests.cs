using System.Net;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// The host's implementation of the subject port (ADR-ARCH-035 §8, ADR-ARCH-033 §4): the
/// <c>sub</c> of the authenticated principal (ADR-ARCH-038 decision 3), and none without one.
/// </summary>
public sealed class SubjectPortTests : IDisposable
{
    private readonly TestKey _key = new("published");

    [Fact]
    public async Task TheSubjectIsTheSubOfTheAuthenticatedPrincipal()
    {
        await using var api = new Api(_key);
        var claims = TestTokens.Claims();

        using var response = await api.GetAsync(TestEndpoints.Subject, TestTokens.Sign(claims, _key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((string)claims["sub"], await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ThereIsNoSubjectWithoutAnAuthenticatedPrincipal()
    {
        await using var api = new Api(_key);

        using var response = await api.GetAsync(TestEndpoints.AnonymousSubject, token: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestEndpoints.NoSubject, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose() => _key.Dispose();
}

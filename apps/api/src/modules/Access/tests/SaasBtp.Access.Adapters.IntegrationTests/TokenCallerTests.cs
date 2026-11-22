using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SaasBtp.Access.Adapters.Caller;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The caller adapter (ADR-ARCH-038 decision 9, "The ports"): the subject comes from <c>sub</c>, and
/// the level is held only when <c>contact_confirmed</c> is <c>true</c>. The principals are built
/// as the host's JWT bearer handler builds them, claim types unmapped and a JSON boolean carried
/// as the value <c>true</c> of type boolean; the host's tests prove the same on signed tokens.
/// </summary>
public sealed class TokenCallerTests
{
    private const string Scheme = "Bearer";

    [Fact]
    public void TheSubjectComesFromSub()
    {
        var subject = Guid.NewGuid();

        var caller = TokenCaller.From(Principal(subject.ToString("D"), Confirmed()));

        Assert.Equal(new UserId(subject), Present(caller).Subject);
    }

    [Fact]
    public void TheLevelIsHeldWhenContactConfirmedIsTrue()
    {
        Assert.True(Present(TokenCaller.From(Principal(Guid.NewGuid().ToString("D"), Confirmed()))).HasConfirmedContact);
    }

    [Fact]
    public void TheLevelIsNotHeldWhenContactConfirmedIsFalse()
    {
        var claim = new Claim("contact_confirmed", "false", ClaimValueTypes.Boolean);

        Assert.False(Present(TokenCaller.From(Principal(Guid.NewGuid().ToString("D"), claim))).HasConfirmedContact);
    }

    [Fact]
    public void TheLevelIsNotHeldWithoutContactConfirmed()
    {
        Assert.False(Present(TokenCaller.From(Principal(Guid.NewGuid().ToString("D")))).HasConfirmedContact);
    }

    [Fact]
    public void TheLevelIsNotHeldWhenContactConfirmedIsTheStringTrue()
    {
        // A JSON string "true", not the boolean the hook sets (ADR-ARCH-038 decision 5).
        var claim = new Claim("contact_confirmed", "true", ClaimValueTypes.String);

        Assert.False(Present(TokenCaller.From(Principal(Guid.NewGuid().ToString("D"), claim))).HasConfirmedContact);
    }

    [Fact]
    public void ThereIsNoCallerWithoutAnAuthenticatedPrincipal()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString("D")), Confirmed()]));

        Assert.Null(TokenCaller.From(anonymous));
        Assert.Null(TokenCaller.From(null));
    }

    [Fact]
    public void ThereIsNoCallerWhenSubIsNotAUuid()
    {
        Assert.Null(TokenCaller.From(Principal("not-a-uuid", Confirmed())));
    }

    [Fact]
    public void TheAdapterReadsThePrincipalOfTheCurrentRequest()
    {
        var subject = Guid.NewGuid();
        var requests = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Principal(subject.ToString("D"), Confirmed()) } };

        var caller = new TokenCaller(requests).Current;

        Assert.Equal(new Features.Ports.Caller(new UserId(subject), HasConfirmedContact: true), caller);

        // The accessor's current request is shared by the async flow: cleared, there is no caller.
        requests.HttpContext = null;
        Assert.Null(new TokenCaller(requests).Current);
    }

    private static Features.Ports.Caller Present(Features.Ports.Caller? caller)
    {
        Assert.NotNull(caller);
        return caller;
    }

    private static Claim Confirmed() => new("contact_confirmed", "true", ClaimValueTypes.Boolean);

    private static ClaimsPrincipal Principal(string sub, params Claim[] claims) =>
        new(new ClaimsIdentity([new Claim("sub", sub), .. claims], Scheme, "sub", "role"));
}

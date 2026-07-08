using System.Security.Claims;
using System.Text.Encodings.Web;
using MicroKit.Auth;
using MicroKit.Result;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SaasBtp.Access.Infrastructure.Authentication;

namespace SaasBtp.Access.UnitTests;

public sealed class SupabaseAuthenticationHandlerTests
{
    private static async Task<SupabaseAuthenticationHandler> CreateHandlerAsync(
        IJwtValidator validator,
        HttpContext context)
    {
        var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        options.CurrentValue.Returns(new AuthenticationSchemeOptions());

        var handler = new SupabaseAuthenticationHandler(
            options, NullLoggerFactory.Instance, UrlEncoder.Default, validator);

        var scheme = new AuthenticationScheme(
            SupabaseAuthenticationHandler.SchemeName, displayName: null, typeof(SupabaseAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return handler;
    }

    private static IJwtValidator ValidatorReturning(Result<ClaimsPrincipal> outcome)
    {
        var validator = Substitute.For<IJwtValidator>();
        validator.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<ClaimsPrincipal>>(outcome));
        return validator;
    }

    [Fact]
    public async Task Authenticate_WithNoAuthorizationHeader_ReturnsNoResult()
    {
        var context = new DefaultHttpContext();
        var handler = await CreateHandlerAsync(
            ValidatorReturning(Result<ClaimsPrincipal>.Success(new ClaimsPrincipal())), context);

        var result = await handler.AuthenticateAsync();

        result.None.ShouldBeTrue();
    }

    [Fact]
    public async Task Authenticate_WithNonBearerHeader_ReturnsNoResult()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";
        var handler = await CreateHandlerAsync(
            ValidatorReturning(Result<ClaimsPrincipal>.Success(new ClaimsPrincipal())), context);

        var result = await handler.AuthenticateAsync();

        result.None.ShouldBeTrue();
    }

    [Fact]
    public async Task Authenticate_WithValidBearerToken_Succeeds()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString())], SupabaseAuthenticationHandler.SchemeName));
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer good.token.value";
        var handler = await CreateHandlerAsync(
            ValidatorReturning(Result<ClaimsPrincipal>.Success(principal)), context);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.ShouldBeTrue();
        result.Principal.ShouldBeSameAs(principal);
    }

    [Fact]
    public async Task Authenticate_WhenValidatorFails_Fails()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer bad.token";
        var handler = await CreateHandlerAsync(
            ValidatorReturning(Result<ClaimsPrincipal>.Failure(new InvalidTestTokenError())), context);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
    }

    private sealed record InvalidTestTokenError()
        : Error(ErrorCode.From("TEST.TOKEN.INVALID"), "invalid token")
    {
        public override ErrorCategory Category => ErrorCategory.Unauthorized;
    }
}

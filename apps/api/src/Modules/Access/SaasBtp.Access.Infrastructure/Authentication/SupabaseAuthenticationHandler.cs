using System.Text.Encodings.Web;
using MicroKit.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SaasBtp.Access.Infrastructure.Authentication;

/// <summary>
/// ASP.NET Core authentication scheme that bridges Supabase JWTs to MicroKit.Auth.
/// </summary>
/// <remarks>
/// MicroKit.Auth.Supabase ships the validator (<c>SupabaseJwtValidator</c>, registered as
/// <see cref="IJwtValidator"/>) but no ASP.NET Core scheme, so this thin handler delegates
/// bearer-token validation to it. On success the extracted <c>ClaimsPrincipal</c> becomes
/// <c>HttpContext.User</c>; <c>UseMicroKitAuth()</c> then maps it into
/// <see cref="ICurrentUserAccessor"/> via the Supabase claims mapper. This is the integration
/// gap recorded as a finding for the MicroKit.Auth.Supabase package.
/// </remarks>
public sealed class SupabaseAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The name of the authentication scheme registered for Supabase JWTs.</summary>
    public const string SchemeName = "Supabase";

    private const string BearerPrefix = "Bearer ";

    private readonly IJwtValidator _jwtValidator;

    /// <summary>Creates the handler.</summary>
    /// <param name="options">Authentication scheme options monitor.</param>
    /// <param name="logger">Logger factory supplied by the authentication infrastructure.</param>
    /// <param name="encoder">URL encoder supplied by the authentication infrastructure.</param>
    /// <param name="jwtValidator">The MicroKit Supabase JWT validator resolved from DI.</param>
    public SupabaseAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IJwtValidator jwtValidator)
        : base(options, logger, encoder)
    {
        _jwtValidator = jwtValidator;
    }

    /// <inheritdoc/>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authorization = Request.Headers.Authorization;

        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization[BearerPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
            return AuthenticateResult.NoResult();

        var validation = await _jwtValidator
            .ValidateAsync(token, Context.RequestAborted)
            .ConfigureAwait(false);

        if (validation.IsFailure)
            return AuthenticateResult.Fail(validation.Error.Message);

        var ticket = new AuthenticationTicket(validation.Value, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}

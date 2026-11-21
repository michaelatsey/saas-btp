using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SaasBtp.Api.Authentication;

/// <summary>
/// The host's authentication of the provider's access tokens (ADR-ARCH-038 decisions 1 to 4 and
/// 6), its fallback policy, and its implementation of the subject port (ADR-ARCH-035 §8).
/// </summary>
internal static class AccessTokenAuthentication
{
    /// <summary>The policy of an operation that requires a recent token (decision 6).</summary>
    /// <remarks>A name, so that a module's endpoint declares it without referencing the host.</remarks>
    public const string RecentTokenPolicy = "RecentToken";

    /// <summary>The clock skew of decision 1.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>The longest lifetime, <c>exp</c> minus <c>iat</c>, the host accepts (decision 1).</summary>
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(1);

    public static IServiceCollection AddAccessTokenAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessTokenOptions>()
            .Bind(configuration.GetSection(AccessTokenOptions.Section))
            .Validate(options => AccessTokenOptions.IsHttpsUri(options.Issuer),
                "AccessToken:Issuer must be an absolute https URI.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience),
                "AccessToken:Audience is required.")
            .Validate(options => AccessTokenOptions.IsHttpsUri(options.JwksUri),
                "AccessToken:JwksUri must be an absolute https URI.");

        services.AddHttpClient(JwksDocumentRetriever.ClientName, client =>
        {
            client.Timeout = JwksDocumentRetriever.Timeout;
            client.MaxResponseContentBufferSize = JwksDocumentRetriever.MaxDocumentSize;
        });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Runs after the handler's own configuration from Authentication:Schemes:Bearer and replaces
        // what it reads: the token validation parameters and the events are set here alone.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AccessTokenOptions>, IHttpClientFactory>((bearer, accessToken, clients) =>
            {
                var settings = accessToken.Value;

                bearer.MapInboundClaims = false;
                bearer.IncludeErrorDetails = false;
                bearer.RefreshOnIssuerKeyNotFound = true;
                bearer.ConfigurationManager = new JwksConfigurationManager(settings, new JwksDocumentRetriever(clients));
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256],
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = ClockSkew,
                    NameClaimType = AccessTokenClaims.Subject,
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = AccessTokenClaims.CheckAsync,
                    OnChallenge = AccessTokenRefusal.LogAsync,
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(RecentTokenPolicy, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new RecentTokenRequirement()));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAuthorizationHandler, RecentTokenHandler>();

        // AddAuthorization registers the default result handler with TryAdd; this one takes its
        // place, so that there is one registration and none wins by order.
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, RecentTokenResultHandler>());

        // A singleton that reads the current request at each call, as a pooled context requires
        // (IUnitOfWorkSubject remarks). Nothing else registers the port.
        services.AddHttpContextAccessor();
        services.AddSingleton<IUnitOfWorkSubject, TokenUnitOfWorkSubject>();

        return services;
    }
}

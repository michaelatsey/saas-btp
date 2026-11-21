using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace SaasBtp.Api.Authentication;

/// <summary>
/// An operation that requires a level accepts only a recent token: its <c>iat</c> at most 60
/// seconds before the request is processed, 90 with the skew (ADR-ARCH-038 decision 6).
/// </summary>
internal sealed class RecentTokenRequirement : IAuthorizationRequirement
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(60) + AccessTokenAuthentication.ClockSkew;

    public const string Refusal = "token older than the bound";
}

internal sealed class RecentTokenHandler(TimeProvider clock) : AuthorizationHandler<RecentTokenRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RecentTokenRequirement requirement)
    {
        if (AccessTokenClaims.IssuedAtOf(context.User) is { } issuedAt
            && clock.GetUtcNow() - issuedAt <= RecentTokenRequirement.Bound)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Answers an authenticated caller whose token is too old for the operation 401, with the bare
/// challenge of every 401, in place of the scheme's forbid, which would answer 403: contract.md
/// §2 keeps 403 for the identity level (ADR-ARCH-038 decision 6). Every other result is the
/// framework's.
/// </summary>
internal sealed class RecentTokenResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<RecentTokenRequirement>().Any() == true)
        {
            return context.ChallengeAsync(
                JwtBearerDefaults.AuthenticationScheme, AccessTokenRefusal.WithReason(RecentTokenRequirement.Refusal));
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

using MicroKit.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SaasBtp.Site.Domain.Context;
using SaasBtp.Site.Domain.Context.Ports;
using SaasBtp.Site.Infrastructure.Context;

namespace SaasBtp.Site.Infrastructure.Middleware;

/// <summary>
/// ASP.NET Core middleware that resolves the current site once per request from the client-provided
/// <c>X-Site-Id</c> header and populates <see cref="CurrentSiteContext"/>. It only RESOLVES — it
/// never returns 403; the driving adapter maps a <see cref="SiteResolution.Rejected"/> outcome to a
/// status code. Requests always proceed (mirrors MicroKit's tenant resolution middleware), so
/// site-agnostic endpoints such as <c>GET /me</c> are unaffected by any <c>X-Site-Id</c> value.
/// </summary>
/// <remarks>
/// The header is client-provided and never trusted: the site is validated against the current
/// tenant's available sites (via <see cref="ISiteScopeProvider"/>) on every request. Must run AFTER
/// tenant resolution so <see cref="ITenantContext.CurrentTenant"/> is populated (the Host orders
/// <c>UseSiteModule</c> after <c>UseAccessModule</c>). With no tenant resolved there is nothing to
/// validate against, so the context is left empty (<see cref="SiteResolution.NotRequested"/>).
/// </remarks>
public sealed partial class CurrentSiteMiddleware(RequestDelegate next)
{
    /// <summary>The client-provided header carrying the requested site identifier.</summary>
    public const string SiteHeaderName = "X-Site-Id";

    /// <summary>Resolves the current site for the request and invokes the next middleware.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="tenantContext">The tenant resolved for the current request.</param>
    /// <param name="siteScopeProvider">Provider of the current tenant's available sites.</param>
    /// <param name="currentSiteContext">The scoped site context to populate.</param>
    /// <param name="logger">Logger for observable diagnostics.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous middleware execution.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        ISiteScopeProvider siteScopeProvider,
        CurrentSiteContext currentSiteContext,
        ILogger<CurrentSiteMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentSiteContext);

        var tenant = tenantContext.CurrentTenant;
        if (tenant is not null)
        {
            var availableSites = await siteScopeProvider
                .GetAvailableSitesAsync(tenant.Id.Value, context.RequestAborted)
                .ConfigureAwait(false);

            var (resolution, currentSite) = Resolve(context, availableSites, logger);
            currentSiteContext.Populate(resolution, currentSite, availableSites);
        }

        await next(context).ConfigureAwait(false);
    }

    private static (SiteResolution Resolution, SiteScope? CurrentSite) Resolve(
        HttpContext context,
        IReadOnlyList<SiteScope> availableSites,
        ILogger logger)
    {
        var header = context.Request.Headers[SiteHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(header))
            return (SiteResolution.NotRequested, null);

        if (!Guid.TryParse(header, out var requestedId))
        {
            LogSiteRejected(logger, header, context.Request.Path.ToString());
            return (SiteResolution.Rejected, null);
        }

        var match = availableSites.FirstOrDefault(site => site.SiteId.Value == requestedId);
        if (match is null)
        {
            LogSiteRejected(logger, header, context.Request.Path.ToString());
            return (SiteResolution.Rejected, null);
        }

        return (SiteResolution.Resolved, match);
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Requested site '{RequestedSiteId}' is not in the current tenant for request '{Path}'. Rejecting site scope.")]
    private static partial void LogSiteRejected(ILogger logger, string requestedSiteId, string path);
}

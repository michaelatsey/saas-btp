using Microsoft.Extensions.Options;
using SaasBtp.Site.Domain.Context;
using SaasBtp.Site.Domain.Context.Ports;

namespace SaasBtp.Site.Infrastructure.Context;

/// <summary>
/// Provisional <see cref="ISiteScopeProvider"/> loaded from <see cref="SiteCatalogueOptions"/>
/// (mirrors MicroKit's <c>ConfigurationTenantStore</c>).
/// </summary>
/// <remarks>
/// PROVISIONAL Story A source. It returns tenant-catalogue availability — the sites whose tenant
/// matches the requested tenant — NOT a user's membership list. This is the single seam Story B
/// replaces with a membership-backed <see cref="ISiteScopeProvider"/> (adding per-user filtering)
/// without changing the <c>GET /context</c> contract. There is deliberately no user parameter
/// (ADR-ARCH-004): within a tenant, availability is the same for every authenticated user until
/// Membership ships.
/// </remarks>
public sealed class ConfigurationSiteScopeProvider : ISiteScopeProvider
{
    private readonly IReadOnlyList<SiteScope> _sites;

    /// <summary>Initializes the provider from the configured site catalogue.</summary>
    /// <param name="options">The site catalogue options.</param>
    public ConfigurationSiteScopeProvider(IOptions<SiteCatalogueOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _sites = [.. options.Value.Sites.Select(record => record.ToScope())];
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<SiteScope>> GetAvailableSitesAsync(Guid tenantId, CancellationToken ct = default)
    {
        IReadOnlyList<SiteScope> available = [.. _sites.Where(site => site.TenantId == tenantId)];
        return ValueTask.FromResult(available);
    }
}

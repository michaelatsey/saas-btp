using MicroKit.Tenancy;
using Microsoft.Extensions.Options;
using SaasBtp.Site.Domain.Context;
using SaasBtp.Site.Infrastructure.Context;

namespace SaasBtp.Site.UnitTests;

public sealed class ConfigurationSiteScopeProviderTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static ConfigurationSiteScopeProvider ProviderWith(params SiteRecord[] sites) =>
        new(Options.Create(new SiteCatalogueOptions { Sites = [.. sites] }));

    private static SiteRecord Site(Guid tenantId, string name) =>
        new() { Id = SiteId.NewId(), TenantId = new TenantId(tenantId), Name = name };

    [Fact]
    public async Task GetAvailableSites_ReturnsSitesOfRequestedTenant()
    {
        var provider = ProviderWith(Site(TenantA, "A1"), Site(TenantA, "A2"), Site(TenantB, "B1"));

        var sites = await provider.GetAvailableSitesAsync(TenantA);

        sites.Select(site => site.Name).ShouldBe(new[] { "A1", "A2" }, ignoreOrder: true);
    }

    [Fact]
    public async Task GetAvailableSites_IsolatesOtherTenants()
    {
        var provider = ProviderWith(Site(TenantA, "A1"), Site(TenantB, "B1"));

        var sites = await provider.GetAvailableSitesAsync(TenantA);

        sites.ShouldAllBe(site => site.TenantId == TenantA);
        sites.Select(site => site.Name).ShouldNotContain("B1");
    }

    [Fact]
    public async Task GetAvailableSites_ReturnsEmptyWhenTenantHasNoSites()
    {
        var provider = ProviderWith(Site(TenantB, "B1"));

        var sites = await provider.GetAvailableSitesAsync(TenantA);

        sites.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAvailableSites_DependsOnlyOnTenant_NotOnAnyUser()
    {
        // The port takes only a tenant id (no user parameter), so the catalogue is identical for
        // every caller of the same tenant: repeated calls yield the same sites. This is the
        // provisional over-permission Story A accepts (tightened by Membership / Story B).
        var provider = ProviderWith(Site(TenantA, "A1"), Site(TenantA, "A2"));

        var first = await provider.GetAvailableSitesAsync(TenantA);
        var second = await provider.GetAvailableSitesAsync(TenantA);

        first.Count.ShouldBe(2);
        first.Select(site => site.SiteId.Value).ShouldBe(second.Select(site => site.SiteId.Value));
    }
}

using MicroKit.Result;
using Microsoft.AspNetCore.Http;
using SaasBtp.Site.Application.Features.ResolveCurrentSite;
using SaasBtp.Site.Infrastructure.Context;
using SaasBtp.Site.Infrastructure.Endpoints;

namespace SaasBtp.Site.UnitTests;

public sealed class ResolveContextEndpointTests
{
    private static CurrentSiteResult SampleContext(SiteView? currentSite = null)
    {
        IReadOnlyList<SiteView> available = currentSite is null ? [] : [currentSite];
        return new CurrentSiteResult(
            new UserView(Guid.NewGuid(), "chef@chantier.fr"),
            new TenantView(Guid.NewGuid()),
            ["auditor"],
            currentSite,
            available);
    }

    private static int StatusOf(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 0;

    [Fact]
    public void Map_WhenSuccessAndSiteNotRequested_Returns200WithNullSite()
    {
        var result = ResolveContextEndpoint.MapContextResult(
            Result<CurrentSiteResult>.Success(SampleContext(currentSite: null)),
            SiteResolution.NotRequested);

        StatusOf(result).ShouldBe(200);
        var value = ((IValueHttpResult)result).Value.ShouldBeOfType<CurrentSiteResult>();
        value.CurrentSite.ShouldBeNull();
    }

    [Fact]
    public void Map_WhenSuccessAndSiteResolved_Returns200WithSite()
    {
        var site = new SiteView(Guid.NewGuid(), "Chantier A");

        var result = ResolveContextEndpoint.MapContextResult(
            Result<CurrentSiteResult>.Success(SampleContext(site)),
            SiteResolution.Resolved);

        StatusOf(result).ShouldBe(200);
        var value = ((IValueHttpResult)result).Value.ShouldBeOfType<CurrentSiteResult>();
        value.CurrentSite.ShouldNotBeNull();
        value.CurrentSite!.Name.ShouldBe("Chantier A");
    }

    [Fact]
    public void Map_WhenSiteRejected_Returns403()
    {
        var result = ResolveContextEndpoint.MapContextResult(
            Result<CurrentSiteResult>.Success(SampleContext()),
            SiteResolution.Rejected);

        StatusOf(result).ShouldBe(403);
    }

    [Fact]
    public void Map_WhenNotAuthenticated_Returns401()
    {
        var result = ResolveContextEndpoint.MapContextResult(
            Result<CurrentSiteResult>.Failure(ResolveCurrentSiteErrors.NotAuthenticated),
            SiteResolution.NotRequested);

        StatusOf(result).ShouldBe(401);
    }

    [Fact]
    public void Map_WhenTenantNotResolved_Returns403()
    {
        var result = ResolveContextEndpoint.MapContextResult(
            Result<CurrentSiteResult>.Failure(ResolveCurrentSiteErrors.TenantNotResolved),
            SiteResolution.NotRequested);

        StatusOf(result).ShouldBe(403);
    }
}

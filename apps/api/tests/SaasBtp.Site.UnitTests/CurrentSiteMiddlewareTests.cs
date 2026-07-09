using MicroKit.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SaasBtp.Site.Domain.Context;
using SaasBtp.Site.Domain.Context.Ports;
using SaasBtp.Site.Infrastructure.Context;
using SaasBtp.Site.Infrastructure.Middleware;

namespace SaasBtp.Site.UnitTests;

public sealed class CurrentSiteMiddlewareTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static ITenantContext TenantContextReturning(ITenantInfo? tenant)
    {
        var context = Substitute.For<ITenantContext>();
        context.CurrentTenant.Returns(tenant);
        return context;
    }

    private static ITenantInfo Tenant(Guid id)
    {
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns(new TenantId(id));
        return tenant;
    }

    private static ISiteScopeProvider ProviderReturning(params SiteScope[] sites)
    {
        var provider = Substitute.For<ISiteScopeProvider>();
        provider.GetAvailableSitesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<SiteScope>>(sites));
        return provider;
    }

    private static async Task<(CurrentSiteContext Context, bool NextCalled)> InvokeAsync(
        HttpContext httpContext, ITenantContext tenantContext, ISiteScopeProvider provider)
    {
        var nextCalled = false;
        var middleware = new CurrentSiteMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new CurrentSiteContext();

        await middleware.InvokeAsync(
            httpContext, tenantContext, provider, context, NullLogger<CurrentSiteMiddleware>.Instance);

        return (context, nextCalled);
    }

    [Fact]
    public async Task Invoke_WhenNoTenant_LeavesContextEmptyAndProceeds()
    {
        var (context, next) = await InvokeAsync(
            new DefaultHttpContext(), TenantContextReturning(null), ProviderReturning());

        context.Resolution.ShouldBe(SiteResolution.NotRequested);
        context.CurrentSite.ShouldBeNull();
        context.AvailableSites.ShouldBeEmpty();
        next.ShouldBeTrue();
    }

    [Fact]
    public async Task Invoke_WhenTenantAndNoHeader_PopulatesAvailableSitesOnly()
    {
        var scope = new SiteScope(SiteId.NewId(), TenantId, "A");

        var (context, next) = await InvokeAsync(
            new DefaultHttpContext(), TenantContextReturning(Tenant(TenantId)), ProviderReturning(scope));

        context.Resolution.ShouldBe(SiteResolution.NotRequested);
        context.CurrentSite.ShouldBeNull();
        context.AvailableSites.Count.ShouldBe(1);
        next.ShouldBeTrue();
    }

    [Fact]
    public async Task Invoke_WhenValidHeader_ResolvesCurrentSite()
    {
        var scope = new SiteScope(SiteId.NewId(), TenantId, "A");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[CurrentSiteMiddleware.SiteHeaderName] = scope.SiteId.Value.ToString();

        var (context, next) = await InvokeAsync(
            httpContext, TenantContextReturning(Tenant(TenantId)), ProviderReturning(scope));

        context.Resolution.ShouldBe(SiteResolution.Resolved);
        context.CurrentSite.ShouldNotBeNull();
        context.CurrentSite!.SiteId.ShouldBe(scope.SiteId);
        next.ShouldBeTrue();
    }

    [Fact]
    public async Task Invoke_WhenForgedHeader_RejectsWithoutShortCircuiting()
    {
        var scope = new SiteScope(SiteId.NewId(), TenantId, "A");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[CurrentSiteMiddleware.SiteHeaderName] = Guid.NewGuid().ToString();

        var (context, next) = await InvokeAsync(
            httpContext, TenantContextReturning(Tenant(TenantId)), ProviderReturning(scope));

        context.Resolution.ShouldBe(SiteResolution.Rejected);
        context.CurrentSite.ShouldBeNull();
        context.AvailableSites.Count.ShouldBe(1);
        next.ShouldBeTrue(); // the middleware never returns 403 itself
    }

    [Fact]
    public async Task Invoke_WhenHeaderNotAGuid_Rejects()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[CurrentSiteMiddleware.SiteHeaderName] = "not-a-guid";

        var (context, next) = await InvokeAsync(
            httpContext, TenantContextReturning(Tenant(TenantId)), ProviderReturning());

        context.Resolution.ShouldBe(SiteResolution.Rejected);
        context.CurrentSite.ShouldBeNull();
        next.ShouldBeTrue();
    }
}

using MicroKit.Auth;
using MicroKit.Auth.Testing.Fakes;
using MicroKit.Tenancy;
using NSubstitute;
using SaasBtp.Site.Application.Features.ResolveCurrentSite;
using SaasBtp.Site.Domain.Context;
using SaasBtp.Site.Infrastructure.Context;

namespace SaasBtp.Site.UnitTests;

public sealed class ResolveCurrentSiteHandlerTests
{
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

    private static CurrentSiteContext SiteContext(
        SiteResolution resolution, SiteScope? currentSite, params SiteScope[] available)
    {
        var context = new CurrentSiteContext();
        context.Populate(resolution, currentSite, available);
        return context;
    }

    [Fact]
    public void Handle_WhenAuthenticatedTenantAndSiteResolved_ReturnsPopulatedContext()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        var accessor = new FakeCurrentUserAccessor();
        accessor.Set(FakeCurrentUserBuilder.Create()
            .WithUserId(userId)
            .WithEmail("chef@chantier.fr")
            .WithTenantId(tenantId)
            .WithRole(Role.Of("auditor"))
            .WithRole(Role.Of("admin"))
            .Build());

        var scope = new SiteScope(new SiteId(siteId), tenantId, "Chantier A");
        var handler = new ResolveCurrentSiteHandler(
            accessor, TenantContextReturning(Tenant(tenantId)), SiteContext(SiteResolution.Resolved, scope, scope));

        var result = handler.Handle(new ResolveCurrentSiteQuery());

        result.IsSuccess.ShouldBeTrue();
        result.Value.User.UserId.ShouldBe(userId);
        result.Value.User.Email.ShouldBe("chef@chantier.fr");
        result.Value.Tenant.TenantId.ShouldBe(tenantId);
        result.Value.Roles.ShouldBe(new[] { "auditor", "admin" });
        result.Value.CurrentSite.ShouldNotBeNull();
        result.Value.CurrentSite!.SiteId.ShouldBe(siteId);
        result.Value.CurrentSite.Name.ShouldBe("Chantier A");
        result.Value.AvailableSites.Select(site => site.SiteId).ShouldBe(new[] { siteId });
    }

    [Fact]
    public void Handle_WhenNoSiteRequested_ReturnsNullSiteWithAvailableCatalogue()
    {
        var tenantId = Guid.NewGuid();
        var accessor = new FakeCurrentUserAccessor();
        accessor.Set(FakeCurrentUserBuilder.Create().WithUserId(Guid.NewGuid()).WithTenantId(tenantId).Build());

        var scopeA = new SiteScope(SiteId.NewId(), tenantId, "A");
        var scopeB = new SiteScope(SiteId.NewId(), tenantId, "B");
        var handler = new ResolveCurrentSiteHandler(
            accessor, TenantContextReturning(Tenant(tenantId)),
            SiteContext(SiteResolution.NotRequested, currentSite: null, scopeA, scopeB));

        var result = handler.Handle(new ResolveCurrentSiteQuery());

        result.IsSuccess.ShouldBeTrue();
        result.Value.CurrentSite.ShouldBeNull();
        result.Value.AvailableSites.Count.ShouldBe(2);
    }

    [Fact]
    public void Handle_WhenNoUser_ReturnsNotAuthenticated()
    {
        var accessor = new FakeCurrentUserAccessor(); // nothing set -> Get() returns null
        var handler = new ResolveCurrentSiteHandler(
            accessor, TenantContextReturning(Tenant(Guid.NewGuid())), new CurrentSiteContext());

        var result = handler.Handle(new ResolveCurrentSiteQuery());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ResolveCurrentSiteErrors.NotAuthenticated);
    }

    [Fact]
    public void Handle_WhenUserNotAuthenticated_ReturnsNotAuthenticated()
    {
        var accessor = new FakeCurrentUserAccessor();
        accessor.Set(FakeCurrentUserBuilder.Create().AsUnauthenticated().Build());
        var handler = new ResolveCurrentSiteHandler(
            accessor, TenantContextReturning(Tenant(Guid.NewGuid())), new CurrentSiteContext());

        var result = handler.Handle(new ResolveCurrentSiteQuery());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ResolveCurrentSiteErrors.NotAuthenticated);
    }

    [Fact]
    public void Handle_WhenTenantNotResolved_ReturnsTenantNotResolved()
    {
        var accessor = new FakeCurrentUserAccessor();
        accessor.Set(FakeCurrentUserBuilder.Create().WithUserId(Guid.NewGuid()).Build());
        var handler = new ResolveCurrentSiteHandler(
            accessor, TenantContextReturning(null), new CurrentSiteContext());

        var result = handler.Handle(new ResolveCurrentSiteQuery());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ResolveCurrentSiteErrors.TenantNotResolved);
    }
}

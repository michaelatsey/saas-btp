using MicroKit.Auth;
using MicroKit.Auth.Testing.Fakes;
using MicroKit.Tenancy;
using NSubstitute;
using SaasBtp.Access.Application.Features.ResolveCurrentContext;

namespace SaasBtp.Access.UnitTests;

public sealed class ResolveCurrentContextHandlerTests
{
    private static ITenantContext TenantContextReturning(ITenantInfo? tenant)
    {
        var context = Substitute.For<ITenantContext>();
        context.CurrentTenant.Returns(tenant);
        return context;
    }

    private static ITenantInfo Tenant(Guid id) =>
        new TenantRecord { Id = new TenantId(id), Name = "Test Tenant" };

    [Fact]
    public void Handle_WhenAuthenticatedAndTenantResolved_ReturnsContext()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var accessor = new FakeCurrentUserAccessor();
        accessor.Set(FakeCurrentUserBuilder.Create()
            .WithUserId(userId)
            .WithEmail("chef@chantier.fr")
            .WithTenantId(tenantId)
            .WithRole(Role.Of("auditor"))
            .WithRole(Role.Of("admin"))
            .Build());

        var handler = new ResolveCurrentContextHandler(accessor, TenantContextReturning(Tenant(tenantId)));

        var result = handler.Handle(new ResolveCurrentContextQuery());

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(userId);
        result.Value.Email.ShouldBe("chef@chantier.fr");
        result.Value.TenantId.ShouldBe(tenantId);
        result.Value.Roles.ShouldBe(new[] { "auditor", "admin" });
    }

    [Fact]
    public void Handle_WhenNoUser_ReturnsNotAuthenticated()
    {
        var accessor = new FakeCurrentUserAccessor(); // nothing set -> Get() returns null
        var handler = new ResolveCurrentContextHandler(accessor, TenantContextReturning(Tenant(Guid.NewGuid())));

        var result = handler.Handle(new ResolveCurrentContextQuery());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ResolveCurrentContextErrors.NotAuthenticated);
    }

    [Fact]
    public void Handle_WhenUserNotAuthenticated_ReturnsNotAuthenticated()
    {
        var accessor = new FakeCurrentUserAccessor();
        accessor.Set(FakeCurrentUserBuilder.Create().AsUnauthenticated().Build());
        var handler = new ResolveCurrentContextHandler(accessor, TenantContextReturning(Tenant(Guid.NewGuid())));

        var result = handler.Handle(new ResolveCurrentContextQuery());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ResolveCurrentContextErrors.NotAuthenticated);
    }

    [Fact]
    public void Handle_WhenTenantNotResolved_ReturnsTenantNotResolved()
    {
        var accessor = new FakeCurrentUserAccessor();
        accessor.Set(FakeCurrentUserBuilder.Create().WithUserId(Guid.NewGuid()).Build());
        var handler = new ResolveCurrentContextHandler(accessor, TenantContextReturning(null));

        var result = handler.Handle(new ResolveCurrentContextQuery());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ResolveCurrentContextErrors.TenantNotResolved);
    }
}

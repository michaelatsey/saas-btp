using Npgsql;
using SaasBtp.Database.Migrator;
using SaasBtp.Site.Domain.Context.Ports;
using SaasBtp.Site.Infrastructure.Authorization;
using Testcontainers.PostgreSql;

namespace SaasBtp.Site.IntegrationTests;

/// <summary>
/// Behavioural proof (ADR-ARCH-009) that <see cref="SiteMembershipScopeProvider"/> resolves site scope
/// from the TWO authorization edges, OR-ed, against a schema migrated by DbUp on an ephemeral PostgreSQL
/// (portable subset 0001/0002/0004 — same single DDL source as deployment). The mandatory cases below
/// distinguish a real implementation from a stub: an OWNER with no site membership is allowed (branch b),
/// and an EXTERNAL person with a site membership but no tenant membership is allowed (branch a). Requires
/// Docker. A fresh, freshly-migrated container per test keeps assertions isolated.
/// </summary>
public sealed class SiteMembershipScopeProviderTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresTestImage.Name)
        .Build();

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Reuse the migrator's own runner + embedded scripts (single DDL source). Portable subset only —
        // 0004 (site.*) and 0002 (access.*) are portable; the auth-coupled 0003 is excluded.
        var result = MigrationRunner.Run(_postgres.GetConnectionString(), MigrationRunner.IsPortableScript);
        result.Successful.ShouldBeTrue(result.Error?.ToString());

        // Owner-role connection (the container superuser bypasses RLS, as the .NET owner does in prod).
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private ISiteScopeProvider NewProvider() =>
        new SiteMembershipScopeProvider(new SiteScopeDataSource(_dataSource));

    // ------------------------------------------------------------------------------------------------
    // Mandatory cases (a stub returning false, or one ignoring the window/is_active, fails these).
    // ------------------------------------------------------------------------------------------------

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task OwnerTenantMembership_NoSiteMembership_IsAllowed()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedTenantAsync(tenantId);
        await SeedProfileAsync(userId);
        await SeedTenantMembershipAsync(tenantId, userId, "owner", isActive: true);
        await SeedSiteAsync(siteId, tenantId);
        // Deliberately NO site membership: the owner sees the site via the cross-site (branch b) role.

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeTrue();
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task MemberTenantMembership_NoSiteMembership_IsDenied()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedTenantAsync(tenantId);
        await SeedProfileAsync(userId);
        await SeedTenantMembershipAsync(tenantId, userId, "member", isActive: true);
        await SeedSiteAsync(siteId, tenantId);
        // 'member' is not a cross-site role, and there is no site membership.

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeFalse();
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task ExternalUser_ActiveSiteMembership_NoTenantMembership_IsAllowed()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedSiteAsync(siteId, tenantId);
        // No access.membership for this user in this tenant: they are EXTERNAL. They come from another
        // organization (origin_tenant_id differs), yet the active, window-valid site edge alone authorizes.
        await SeedSiteMembershipAsync(
            siteId, tenantId, userId, role: "member", isActive: true,
            validFrom: DaysFromNow(-1), validUntil: null, originTenantId: NewId());

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeTrue();
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMembership_WithValidUntilInPast_IsDenied()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedSiteAsync(siteId, tenantId);
        // Window closed: valid_until is in the past (valid_from earlier still, so the CHECK holds).
        await SeedSiteMembershipAsync(
            siteId, tenantId, userId, role: "member", isActive: true,
            validFrom: DaysFromNow(-2), validUntil: DaysFromNow(-1), originTenantId: null);

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeFalse();
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMembership_Inactive_IsDenied()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedSiteAsync(siteId, tenantId);
        // Revoked: is_active = false, even though the window is current.
        await SeedSiteMembershipAsync(
            siteId, tenantId, userId, role: "site_manager", isActive: false,
            validFrom: DaysFromNow(-1), validUntil: null, originTenantId: tenantId);

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeFalse();
    }

    // ------------------------------------------------------------------------------------------------
    // Additional coverage.
    // ------------------------------------------------------------------------------------------------

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task AdminTenantMembership_NoSiteMembership_IsDenied()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedTenantAsync(tenantId);
        await SeedProfileAsync(userId);
        await SeedTenantMembershipAsync(tenantId, userId, "admin", isActive: true);
        await SeedSiteAsync(siteId, tenantId);
        // Only 'owner' confers cross-site scope today (CrossSiteScopeRoles); 'admin' does not.

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeFalse();
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task InactiveOwnerMembership_IsDenied()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedTenantAsync(tenantId);
        await SeedProfileAsync(userId);
        await SeedTenantMembershipAsync(tenantId, userId, "owner", isActive: false);
        await SeedSiteAsync(siteId, tenantId);
        // Branch (b) still requires is_active: a revoked owner is not authorized.

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeFalse();
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMembership_WithValidFromInFuture_IsDenied()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedSiteAsync(siteId, tenantId);
        // Not yet started: valid_from is in the future (lower bound of [valid_from, valid_until)).
        await SeedSiteMembershipAsync(
            siteId, tenantId, userId, role: "member", isActive: true,
            validFrom: DaysFromNow(1), validUntil: DaysFromNow(30), originTenantId: tenantId);

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeFalse();
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task InternalUser_ActiveSiteMembership_IsAllowed()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedSiteAsync(siteId, tenantId);
        // Internal (origin = tenant), open-ended window, active: authorized via branch (a).
        await SeedSiteMembershipAsync(
            siteId, tenantId, userId, role: "site_manager", isActive: true,
            validFrom: DaysFromNow(-10), validUntil: null, originTenantId: tenantId);

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeTrue();
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site + access.tenants/memberships schema is dormant — scripts removed on the clean slate. Re-enable when Site returns.")]
    public async Task NoEdgeAtAll_IsDenied()
    {
        var (tenantId, userId, siteId) = (NewId(), NewId(), NewId());
        await SeedSiteAsync(siteId, tenantId);
        // No tenant membership and no site membership: nothing authorizes.

        (await NewProvider().CanActInSiteScopeAsync(userId, tenantId, siteId)).ShouldBeFalse();
    }

    // ------------------------------------------------------------------------------------------------
    // Seeding helpers (raw Npgsql on the owner connection; domain-stamped timestamps, no DB defaults).
    // ------------------------------------------------------------------------------------------------

    private static Guid NewId() => Guid.CreateVersion7();

    private static DateTime DaysFromNow(int days) => DateTime.UtcNow.AddDays(days);

    private async Task SeedTenantAsync(Guid tenantId) =>
        await ExecuteAsync(
            "INSERT INTO access.tenants (id, name, status, created_at) VALUES (@id, @name, 'active', now());",
            ("id", tenantId), ("name", $"Tenant {tenantId:N}"));

    private async Task SeedProfileAsync(Guid userId) =>
        await ExecuteAsync(
            "INSERT INTO access.profiles (user_id, email, full_name, created_at) VALUES (@uid, @email, @name, now());",
            ("uid", userId), ("email", $"{userId:N}@test.local"), ("name", "Test User"));

    private async Task SeedTenantMembershipAsync(Guid tenantId, Guid userId, string role, bool isActive) =>
        await ExecuteAsync(
            """
            INSERT INTO access.memberships (id, tenant_id, user_id, role, is_active, created_at)
            VALUES (@id, @tid, @uid, @role, @active, now());
            """,
            ("id", NewId()), ("tid", tenantId), ("uid", userId), ("role", role), ("active", isActive));

    private async Task SeedSiteAsync(Guid siteId, Guid tenantId) =>
        await ExecuteAsync(
            "INSERT INTO site.sites (id, tenant_id, name, status, created_at) VALUES (@id, @tid, @name, 'active', now());",
            ("id", siteId), ("tid", tenantId), ("name", $"Site {siteId:N}"));

    private async Task SeedSiteMembershipAsync(
        Guid siteId, Guid tenantId, Guid userId, string role, bool isActive,
        DateTime validFrom, DateTime? validUntil, Guid? originTenantId) =>
        await ExecuteAsync(
            """
            INSERT INTO site.site_memberships
                (id, site_id, tenant_id, user_id, origin_tenant_id, role, is_active, valid_from, valid_until, created_at)
            VALUES
                (@id, @sid, @tid, @uid, @origin, @role, @active, @from, @until, now());
            """,
            ("id", NewId()), ("sid", siteId), ("tid", tenantId), ("uid", userId),
            ("origin", (object?)originTenantId ?? DBNull.Value), ("role", role), ("active", isActive),
            ("from", validFrom), ("until", (object?)validUntil ?? DBNull.Value));

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = _dataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}

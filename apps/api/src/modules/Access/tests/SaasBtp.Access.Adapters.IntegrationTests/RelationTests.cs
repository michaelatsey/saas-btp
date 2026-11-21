using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// INV-ACCESS-05, 06 and 07: three distinct relations, one current membership per organization and
/// one current access per workspace, the unique keys restricted to rows without an end
/// (contract.md §5, §7), and <c>access.may_act_in</c> reading the access to that one workspace
/// (ADR-ARCH-032 §2, ADR-ARCH-033 §5).
/// </summary>
public sealed class RelationTests(AccessDatabase database)
{
    [Fact]
    public async Task INV_ACCESS_05_AnOwnershipAndAMembershipWithoutAnAccessLeaveMayActInFalse()
    {
        // The person holds a current access elsewhere, so a check that read "any access" would be true.
        var founder = await Founding.CommitAsync(database);
        var organizationId = Guid.CreateVersion7();
        var ownershipId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        await using (var session = await database.AsAsync(founder.UserId))
        {
            await session.InsertOwnershipAsync(ownershipId, founder.ProfileId, organizationId);
            await session.InsertOrganizationAsync(organizationId, $"Owned {Guid.NewGuid():N}", ownershipId);
            await session.InsertMembershipAsync(Guid.CreateVersion7(), founder.ProfileId, organizationId);

            // Without RETURNING: with no access, the owner may not see the workspace (ADR-ARCH-033 §5).
            await session.InsertWorkspaceAsync(workspaceId, organizationId, "No access", returning: false);
            await session.CommitAsync();
        }

        await using var reader = await database.AsAsync(founder.UserId);
        Assert.False(await reader.MayActInAsync(workspaceId));
        Assert.True(await reader.MayActInAsync(founder.WorkspaceId));
        Assert.Equal(0L, await reader.CountAsync("select count(*) from access.workspaces where id = @id", ("id", workspaceId)));
    }

    [Fact]
    public async Task INV_ACCESS_06_ASecondCurrentMembershipIsRefusedAndAdmittedOnceTheFirstEnds()
    {
        var founder = await Founding.CommitAsync(database);
        var second = Guid.CreateVersion7();

        Assert.Equal(PostgresErrorCodes.UniqueViolation, await Session.RefusalAsync(database.Api, founder.UserId, session =>
            session.InsertMembershipAsync(second, founder.ProfileId, founder.OrganizationId)));

        await EndAsync("memberships", founder.MembershipId);

        await using var session = await database.AsAsync(founder.UserId);
        await session.InsertMembershipAsync(second, founder.ProfileId, founder.OrganizationId);
        await session.CommitAsync();
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.memberships where profile_id = @profile and organization_id = @organization and ended_at is null",
            ("profile", founder.ProfileId), ("organization", founder.OrganizationId)));
    }

    [Fact]
    public async Task INV_ACCESS_07_ASecondCurrentAccessIsRefusedAndAdmittedOnceTheFirstEnds()
    {
        var founder = await Founding.CommitAsync(database);
        var elsewhere = await founder.CommitAnotherAsync(database);
        var second = Guid.CreateVersion7();

        Assert.Equal(PostgresErrorCodes.UniqueViolation, await Session.RefusalAsync(database.Api, founder.UserId, session =>
            session.InsertAccessAsync(second, founder.ProfileId, founder.WorkspaceId, founder.OrganizationId)));

        await EndAsync("workspace_accesses", founder.AccessId);

        await using (var ended = await database.AsAsync(founder.UserId))
        {
            Assert.False(await ended.MayActInAsync(founder.WorkspaceId));
            Assert.True(await ended.MayActInAsync(elsewhere.WorkspaceId));
            Assert.Equal(0L, await ended.CountAsync("select count(*) from access.workspaces where id = @id", ("id", founder.WorkspaceId)));
        }

        await using (var session = await database.AsAsync(founder.UserId))
        {
            await session.InsertAccessAsync(second, founder.ProfileId, founder.WorkspaceId, founder.OrganizationId);
            await session.CommitAsync();
        }

        await using var restored = await database.AsAsync(founder.UserId);
        Assert.True(await restored.MayActInAsync(founder.WorkspaceId));
    }

    [Fact]
    public async Task Adr032_MayActInIsTrueOnlyForTheWorkspaceOfTheAccess()
    {
        var founder = await Founding.CommitAsync(database);
        var other = Guid.CreateVersion7();

        // A second workspace of the same organization, which no process of BP-001 creates.
        await database.AsSuperuserAsync(
            """
            insert into access.workspaces (id, organization_id, name, created_at, created_by, aggregate_version)
            values (@id, @organization, @name, now(), 'integration-test', 1)
            """,
            ("id", other), ("organization", founder.OrganizationId), ("name", $"Second {Guid.NewGuid():N}"));

        await using var session = await database.AsAsync(founder.UserId);
        Assert.True(await session.MayActInAsync(founder.WorkspaceId));
        Assert.False(await session.MayActInAsync(other));
        Assert.Equal(0L, await session.CountAsync("select count(*) from access.workspaces where id = @id", ("id", other)));
    }

    /// <summary>
    /// Records the end of a relation, as the container's superuser: no process of BP-001 ends one,
    /// so no policy lets <c>saasbtp_api</c> do it (contract.md §5).
    /// </summary>
    private async Task EndAsync(string table, Guid id) =>
        Assert.Equal(1, await database.AsSuperuserAsync($"update access.{table} set ended_at = now() where id = @id", ("id", id)));
}

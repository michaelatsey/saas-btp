using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// INV-ACCESS-09 and BP001-AC-06: another person sees nothing of an organization, and no one
/// creates a record naming someone else (contract.md §5, §7). Both people hold a complete founding,
/// so that a policy widened to "any organization the subject owns" or "any workspace the subject
/// has an access to" is caught.
/// </summary>
public sealed class IsolationTests(AccessDatabase database)
{
    [Fact]
    public async Task INV_ACCESS_09_AnotherSubjectReadsNoRowOfTheOrganizationEvenByIdentifier()
    {
        var owner = await Founding.CommitAsync(database);
        var other = await Founding.CommitAsync(database);
        await Rows.AssertExistAsync(database, owner);

        await using var session = await database.AsAsync(other.UserId);

        Assert.Empty(await Rows.SeenAsync(session, owner));
        Assert.Equal(0L, await session.CountAsync(
            """
            select (select count(*) from access.ownerships where organization_id = @organization)
                 + (select count(*) from access.memberships where organization_id = @organization)
                 + (select count(*) from access.workspace_accesses where organization_id = @organization)
                 + (select count(*) from access.workspaces where organization_id = @organization)
            """,
            ("organization", owner.OrganizationId)));
        Assert.False(await session.MayActInAsync(owner.WorkspaceId));
        Assert.Equal(Rows.Of(other).Select(row => row.Table), await Rows.SeenAsync(session, other));
    }

    [Fact]
    public async Task BP001_AC_06_ARecordNamingAnotherPersonIsRefused()
    {
        var founder = await Founding.CommitAsync(database);
        var other = await Founding.CommitAsync(database);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, founder.UserId, session =>
            session.InsertProfileAsync(Guid.CreateVersion7(), other.UserId)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, founder.UserId, session =>
            session.InsertOwnershipAsync(Guid.CreateVersion7(), other.ProfileId, founder.OrganizationId)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, founder.UserId, session =>
            session.InsertMembershipAsync(Guid.CreateVersion7(), other.ProfileId, founder.OrganizationId)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, founder.UserId, session =>
            session.InsertAccessAsync(Guid.CreateVersion7(), other.ProfileId, founder.WorkspaceId, founder.OrganizationId)));
    }
}

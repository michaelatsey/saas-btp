using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// INV-ACCESS-04: the unique key on organization and name, and no policy that lets a workspace
/// change organization (contract.md §7).
/// </summary>
public sealed class WorkspaceTests(AccessDatabase database)
{
    [Fact]
    public async Task INV_ACCESS_04_TwoWorkspacesOfOneOrganizationCannotShareAName()
    {
        var founder = await Founding.CommitAsync(database);

        var refusal = await Session.RefusalAsync(database.Api, founder.UserId, session =>
            session.InsertWorkspaceAsync(Guid.CreateVersion7(), founder.OrganizationId, founder.WorkspaceName, returning: false));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal);
    }

    [Fact]
    public async Task INV_ACCESS_04_TwoOrganizationsMayShareAWorkspaceName()
    {
        // The same company name too: no unique key on it (BP001-R5).
        var name = $"Shared {Guid.NewGuid():N}";
        var first = await Founding.CommitAsync(database, organizationName: name, workspaceName: name);

        var second = await Founding.CommitAsync(database, organizationName: name, workspaceName: name);

        Assert.Equal(first.WorkspaceName, second.WorkspaceName);
        await Rows.AssertExistAsync(database, second);
    }

    [Fact]
    public async Task INV_ACCESS_04_NoPolicyLetsAWorkspaceChangeOrganization()
    {
        var founder = await Founding.CommitAsync(database);
        var another = await founder.CommitAnotherAsync(database);

        var refusal = await Session.RefusalAsync(database.Api, founder.UserId, session => session.ExecuteAsync(
            "update access.workspaces set organization_id = @organization where id = @id",
            ("organization", another.OrganizationId), ("id", founder.WorkspaceId)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refusal);
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.workspaces where id = @id and organization_id = @organization",
            ("id", founder.WorkspaceId), ("organization", founder.OrganizationId)));
        Assert.Equal(0L, await database.CountAsSuperuserAsync(
            "select count(*) from pg_policy where polrelid = 'access.workspaces'::regclass and polcmd in ('w', '*')"));
    }
}

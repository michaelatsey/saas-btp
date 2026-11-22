using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Access.Adapters.Persistence.Founding;
using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The Integration proofs contract.md §7 sets for BP001-AC-03, BP001-AC-04 and BP001-AC-23: the
/// founding written by the module's unit of work as <c>saasbtp_api</c>, the port the founding's
/// handler calls.
/// </summary>
public sealed class FoundingScenarioTests(AccessDatabase database)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BP001_AC_23_BothNamesAreWrittenTrimmed()
    {
        // Tab and U+00A0 NO-BREAK SPACE are removed; U+200B ZERO WIDTH SPACE is kept (BP001-R7).
        var request = FoundingRequest.New() with
        {
            OrganizationName = Organization("\t Kouassi BTP \t"),
            WorkspaceName = Workspace(" \tSiège​\t"),
        };

        await FoundAsync(request);

        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.organizations where id = @id and name = @name",
            ("id", request.Reference.Value), ("name", "Kouassi BTP")));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.workspaces where organization_id = @id and name = @name",
            ("id", request.Reference.Value), ("name", "Siège​")));
    }

    [Fact]
    public async Task BP001_AC_03_AnOwnerFoundsASecondCompanyAndKeepsTheFirst()
    {
        var first = await Founding.CommitAsync(database);
        var second = FoundingRequest.New() with { User = first.UserId };

        var founded = await FoundAsync(second);

        // The same profile owns both, each with its own first workspace.
        Assert.Equal(first.ProfileId, founded.Profile.Id.Value);
        Assert.Equal(2L, await OwnedByAsync(first.ProfileId, first.OrganizationId, second.Reference.Value));
        Assert.Equal(1L, await WorkspacesOfAsync(first.OrganizationId));
        Assert.Equal(1L, await WorkspacesOfAsync(second.Reference.Value));
        Assert.NotEqual(first.WorkspaceId, founded.Workspace.Id.Value);

        // What they can do in the first is unchanged: its rows are there, never updated, and they
        // still act in its workspace.
        await Rows.AssertExistAsync(database, first);
        await AssertNeverUpdatedAsync(first);
        await using var founder = await database.AsAsync(first.UserId);
        Assert.True(await founder.MayActInAsync(first.WorkspaceId));
        Assert.True(await founder.MayActInAsync(founded.Workspace.Id.Value));
    }

    /// <summary>
    /// No process of BP-001 lets a person belong to an organization without owning it, so the
    /// membership this scenario starts from is inserted as the container's superuser, outside
    /// <c>saasbtp_api</c>; the founding itself runs as <c>saasbtp_api</c>.
    /// </summary>
    [Fact]
    public async Task BP001_AC_04_AMemberFoundsTheirOwnCompanyAndKeepsTheirMembership()
    {
        var other = await Founding.CommitAsync(database);
        var member = Guid.NewGuid();
        var profileId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();
        await using (var setup = await Session.BeginAsync(database.Superuser, subject: null))
        {
            await setup.InsertProfileAsync(profileId, member, returning: false);
            await setup.InsertMembershipAsync(membershipId, profileId, other.OrganizationId, returning: false);
            await setup.CommitAsync();
        }

        var request = FoundingRequest.New() with { User = member };
        var founded = await FoundAsync(request);

        // They own the company they founded, with the profile they had, and not the other one.
        Assert.Equal(profileId, founded.Profile.Id.Value);
        Assert.Equal(1L, await OwnedByAsync(profileId, request.Reference.Value));
        Assert.Equal(0L, await OwnedByAsync(profileId, other.OrganizationId));

        // Their place in the other company is unchanged: the same membership, current, never
        // updated, still theirs to read, and still no access to its workspace.
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            """
            select count(*) from access.memberships
            where id = @id and profile_id = @profile and organization_id = @organization
              and ended_at is null and updated_at is null
            """,
            ("id", membershipId), ("profile", profileId), ("organization", other.OrganizationId)));
        await using var person = await database.AsAsync(member);
        Assert.Equal(1L, await person.CountAsync("select count(*) from access.memberships where id = @id", ("id", membershipId)));
        Assert.False(await person.MayActInAsync(other.WorkspaceId));
        Assert.True(await person.MayActInAsync(founded.Workspace.Id.Value));
    }

    private static OrganizationName Organization(string declared) =>
        OrganizationName.TryCreate(declared, out var name, out var defect) ? name : throw new InvalidOperationException(defect.ToString());

    private static WorkspaceName Workspace(string declared) =>
        WorkspaceName.TryCreate(declared, out var name, out var defect) ? name : throw new InvalidOperationException(defect.ToString());

    private async Task<FoundedCompany> FoundAsync(FoundingRequest request)
    {
        await using var module = ModuleServices.Start(database.Api);
        module.Subject.Current = request.User;
        await using var scope = module.Scope();
        return await scope.ServiceProvider.GetRequiredService<FoundingUnitOfWork>()
            .FoundCompanyAsync(request.Reference, request.OrganizationName, request.WorkspaceName, request.Founder, Token);
    }

    /// <summary>How many of <paramref name="organizations"/> have the profile's ownership as their current one.</summary>
    private Task<long> OwnedByAsync(Guid profileId, params Guid[] organizations) =>
        database.CountAsSuperuserAsync(
            """
            select count(*) from access.organizations o
            join access.ownerships w on w.id = o.current_ownership_id and w.organization_id = o.id
            where w.profile_id = @profile and o.id = any(@organizations)
            """,
            ("profile", profileId), ("organizations", organizations));

    private Task<long> WorkspacesOfAsync(Guid organizationId) =>
        database.CountAsSuperuserAsync(
            "select count(*) from access.workspaces where organization_id = @id", ("id", organizationId));

    private async Task AssertNeverUpdatedAsync(Founding founding)
    {
        foreach (var (table, id) in Rows.Of(founding))
        {
            Assert.Equal(1L, await database.CountAsSuperuserAsync(
                $"select count(*) from access.{table} where id = @id and updated_at is null and aggregate_version = 1", ("id", id)));
        }
    }
}

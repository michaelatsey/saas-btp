using System.Net;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The Integration proofs contract.md §7 sets for BP001-AC-01, 05, 19 and 20 and INV-ACCESS-09 on
/// the two reads of contract.md §2, <c>GET /api/v1/workspaces</c> and
/// <c>GET /api/v1/organizations/{id}</c>, answered by the module's endpoints as <c>saasbtp_api</c>.
/// Every company is founded through <c>POST /api/v1/organizations</c>.
/// </summary>
/// <remarks>
/// The database is shared by the whole assembly, so every assertion is limited to the identifiers
/// the test created; the listing of a person created by the test holds only what that person may enter.
/// </remarks>
public sealed class ReadingTests(AccessDatabase database)
{
    private const string WorkspacesPath = "/api/v1/workspaces";

    private const string Company = "Kouassi BTP";

    private const string Site = "Siège Abidjan";

    private const string Declared = "declared";

    [Fact]
    public async Task BP001_AC_01_AFounderReadsTheirDeclaredCompanyAndEntersItsOnlyWorkspace()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var founded = await FoundAsync(api, Guid.NewGuid());

        var organization = await api.GetAsync(founded.Person, OrganizationPath(founded.Organization));
        Assert.Equal(HttpStatusCode.OK, organization.Status);
        Assert.Equal((founded.Organization, Company, Declared), organization.Organization);

        // It holds exactly that workspace.
        Assert.Equal(
            [founded.Workspace],
            await database.IdentifiersAsSuperuserAsync(
                "select id from access.workspaces where organization_id = @id and name = @name",
                ("id", founded.Organization), ("name", Site)));

        // The person is its owner and its only member. The founder sees only their own membership
        // under the select policies, so both are read as the container's superuser, outside
        // saasbtp_api.
        var profile = await ProfileOfAsync(founded.Person);
        Assert.Equal([profile], await CurrentOwnerProfilesAsync(founded.Organization));
        Assert.Equal(
            [profile],
            await database.IdentifiersAsSuperuserAsync(
                "select profile_id from access.memberships where organization_id = @id", ("id", founded.Organization)));

        var listing = await api.GetAsync(founded.Person, WorkspacesPath);
        Assert.Equal(HttpStatusCode.OK, listing.Status);
        Assert.Equal([(founded.Workspace, Site, founded.Organization)], listing.Workspaces);
    }

    /// <summary>
    /// A second workspace of the founder's organization, which no process of BP-001 creates, is
    /// inserted as the container's superuser with no access (BP001-R4); another person founds their
    /// own company.
    /// </summary>
    [Fact]
    public async Task BP001_AC_05_TheListingHoldsExactlyTheFirstWorkspace()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var founded = await FoundAsync(api, Guid.NewGuid());
        await FoundAsync(api, Guid.NewGuid());
        var second = await InsertWorkspaceAsync(founded.Organization, "Chantier Cocody");

        var listing = await api.GetAsync(founded.Person, WorkspacesPath);

        Assert.Equal(HttpStatusCode.OK, listing.Status);
        Assert.Equal([(founded.Workspace, Site, founded.Organization)], listing.Workspaces);
        Assert.Equal(1L, await database.CountAsSuperuserAsync("select count(*) from access.workspaces where id = @id", ("id", second)));
    }

    /// <summary>
    /// contract.md §8: on one data set, the listing returns exactly the workspaces for which
    /// <c>access.may_act_in</c> is true for the caller. The data set is every workspace this test
    /// creates: the founder's two first workspaces, a workspace of their organization without access,
    /// one whose access is ended, and another person's first workspace. The rows no process of BP-001
    /// creates are inserted as the container's superuser.
    /// </summary>
    [Fact]
    public async Task BP001_AC_05_WorkspaceListMatchesMayActIn()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var person = Guid.NewGuid();
        var founded = await FoundAsync(api, person);
        var another = await FoundAsync(api, person, "Kouassi Maintenance", "Atelier Yopougon");
        var other = await FoundAsync(api, Guid.NewGuid());
        var withoutAccess = await InsertWorkspaceAsync(founded.Organization, "Chantier Cocody");
        var endedAccess = await InsertWorkspaceAsync(founded.Organization, "Chantier Plateau");
        await InsertEndedAccessAsync(await ProfileOfAsync(person), endedAccess, founded.Organization);
        Guid[] data = [founded.Workspace, another.Workspace, other.Workspace, withoutAccess, endedAccess];

        var mayActIn = new HashSet<Guid>();
        await using (var session = await database.AsAsync(person))
        {
            foreach (var workspace in data)
            {
                if (await session.MayActInAsync(workspace))
                {
                    mayActIn.Add(workspace);
                }
            }
        }

        var listing = await api.GetAsync(person, WorkspacesPath);

        Assert.Equal(HttpStatusCode.OK, listing.Status);
        Assert.NotEmpty(mayActIn);
        Assert.True(mayActIn.Count < data.Length, "access.may_act_in is true for every workspace of the data set: the test tells nothing apart.");
        Assert.Equal(mayActIn.Order(), listing.Workspaces.Select(workspace => workspace.Id).Order());
    }

    [Fact]
    public async Task BP001_AC_19_TwoFoundersOfTheSameNameEachSeeOnlyTheirOwnCompany()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var one = await FoundAsync(api, Guid.NewGuid());
        var two = await FoundAsync(api, Guid.NewGuid());

        Assert.NotEqual(one.Organization, two.Organization);
        Assert.Equal(2L, await database.CountAsSuperuserAsync(
            "select count(*) from access.organizations where id = any(@ids) and name = @name",
            ("ids", new[] { one.Organization, two.Organization }), ("name", Company)));

        foreach (var (theirs, others) in new[] { (one, two), (two, one) })
        {
            var own = await api.GetAsync(theirs.Person, OrganizationPath(theirs.Organization));
            Assert.Equal(HttpStatusCode.OK, own.Status);
            Assert.Equal((theirs.Organization, Company, Declared), own.Organization);

            var other = await api.GetAsync(theirs.Person, OrganizationPath(others.Organization));
            Assert.Equal(HttpStatusCode.NotFound, other.Status);

            var listing = await api.GetAsync(theirs.Person, WorkspacesPath);
            Assert.Equal(HttpStatusCode.OK, listing.Status);
            Assert.Equal([(theirs.Workspace, Site, theirs.Organization)], listing.Workspaces);
        }
    }

    /// <summary>
    /// Two other signed-in people: one with no profile, and one who owns another company. Each reads
    /// the organization with the same answer as an identifier that exists nowhere, a UUIDv7 or not.
    /// </summary>
    [Fact]
    public async Task BP001_AC_20_AnotherPersonReadsTheCompanyAsNotFoundAndListsNoneOfItsWorkspaces()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var founded = await FoundAsync(api, Guid.NewGuid());
        var stranger = Guid.NewGuid();
        var owner = await FoundAsync(api, Guid.NewGuid());

        foreach (var person in new[] { stranger, owner.Person })
        {
            var hidden = await api.GetAsync(person, OrganizationPath(founded.Organization));
            var nowhere = await api.GetAsync(person, OrganizationPath(Guid.CreateVersion7()));
            var notVersion7 = await api.GetAsync(person, OrganizationPath(Guid.NewGuid()));

            Assert.Equal(HttpStatusCode.NotFound, hidden.Status);
            Assert.Equal(nowhere, hidden);
            Assert.Equal(nowhere, notVersion7);

            var listing = await api.GetAsync(person, WorkspacesPath);
            Assert.Equal(HttpStatusCode.OK, listing.Status);
            Assert.DoesNotContain(founded.Workspace, listing.Workspaces.Select(workspace => workspace.Id));
            Assert.DoesNotContain(founded.Organization, listing.Workspaces.Select(workspace => workspace.OrganizationId));
        }

        Assert.Empty((await api.GetAsync(stranger, WorkspacesPath)).Workspaces);
    }

    /// <summary>
    /// The answers of both operations, to the founder and to another person, carry only the
    /// properties of contract.md §2 (each reader asserts it), a 404 carries no body, and none names
    /// the founder, their profile, the ownership or the membership.
    /// </summary>
    [Fact]
    public async Task INV_ACCESS_09_NoAnswerOfTheReadsNamesAnOwnerOrAMember()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var founded = await FoundAsync(api, Guid.NewGuid());
        var stranger = Guid.NewGuid();
        var profile = await ProfileOfAsync(founded.Person);
        var ownership = Assert.Single(await database.IdentifiersAsSuperuserAsync(
            "select current_ownership_id from access.organizations where id = @id", ("id", founded.Organization)));
        var membership = Assert.Single(await database.IdentifiersAsSuperuserAsync(
            "select id from access.memberships where organization_id = @id", ("id", founded.Organization)));

        var organization = await api.GetAsync(founded.Person, OrganizationPath(founded.Organization));
        var listing = await api.GetAsync(founded.Person, WorkspacesPath);
        var hidden = await api.GetAsync(stranger, OrganizationPath(founded.Organization));
        var strangerListing = await api.GetAsync(stranger, WorkspacesPath);

        Assert.Equal((founded.Organization, Company, Declared), organization.Organization);
        Assert.Equal([(founded.Workspace, Site, founded.Organization)], listing.Workspaces);
        Assert.Equal(HttpStatusCode.NotFound, hidden.Status);
        Assert.Empty(hidden.Body);
        Assert.Empty(strangerListing.Workspaces);

        foreach (var answer in new[] { organization, listing, hidden, strangerListing })
        {
            foreach (var identifier in new[] { founded.Person, profile, ownership, membership })
            {
                Assert.DoesNotContain(identifier.ToString("D"), answer.Body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string OrganizationPath(Guid id) => $"{FoundingApi.Path}/{id:D}";

    /// <summary>Founds a company through the API as <paramref name="person"/>, and reads back its first workspace.</summary>
    private async Task<Founded> FoundAsync(FoundingApi api, Guid person, string company = Company, string site = Site)
    {
        var reference = Guid.CreateVersion7();
        var answer = await api.FoundAsync(person, reference, company, site);
        Assert.Equal(HttpStatusCode.Created, answer.Status);

        var workspace = Assert.Single(await database.IdentifiersAsSuperuserAsync(
            "select id from access.workspaces where organization_id = @id", ("id", reference)));
        return new Founded(person, reference, workspace);
    }

    private async Task<Guid> ProfileOfAsync(Guid person) =>
        Assert.Single(await database.IdentifiersAsSuperuserAsync(
            "select id from access.profiles where user_id = @user", ("user", person)));

    private Task<IReadOnlyList<Guid>> CurrentOwnerProfilesAsync(Guid organization) =>
        database.IdentifiersAsSuperuserAsync(
            """
            select w.profile_id from access.organizations o
            join access.ownerships w on w.id = o.current_ownership_id and w.organization_id = o.id
            where o.id = @id
            """,
            ("id", organization));

    /// <summary>Inserts a workspace of <paramref name="organization"/> with no access, as the container's superuser.</summary>
    private async Task<Guid> InsertWorkspaceAsync(Guid organization, string name)
    {
        var workspace = Guid.CreateVersion7();
        await using var setup = await Session.BeginAsync(database.Superuser, subject: null);
        await setup.InsertWorkspaceAsync(workspace, organization, name, returning: false);
        await setup.CommitAsync();
        return workspace;
    }

    /// <summary>Inserts an access of <paramref name="profile"/> to the workspace, then ends it, as the container's superuser.</summary>
    private async Task InsertEndedAccessAsync(Guid profile, Guid workspace, Guid organization)
    {
        var access = Guid.CreateVersion7();
        await using var setup = await Session.BeginAsync(database.Superuser, subject: null);
        await setup.InsertAccessAsync(access, profile, workspace, organization, returning: false);
        await setup.ExecuteAsync("update access.workspace_accesses set ended_at = created_at where id = @id", ("id", access));
        await setup.CommitAsync();
    }

    /// <summary>A company founded by <see cref="Person"/>: its organization and its first workspace.</summary>
    private sealed record Founded(Guid Person, Guid Organization, Guid Workspace);
}

using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SaasBtp.Access.Adapters.Persistence;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The Integration proofs contract.md §7 sets for BP001-AC-13, 14, 15, 16, 21 and 22 and
/// INV-ACCESS-09, and the profile retry of contract.md §4: a founding submitted again, at once or
/// later, and a reference that identifies another organization, answered by the module's endpoint
/// as <c>saasbtp_api</c> (contract.md §3).
/// </summary>
public sealed class FoundingReplayTests(AccessDatabase database)
{
    /// <summary>The 409, byte for byte: only that the reference is in use (INV-ACCESS-09, ADR-ARCH-040).</summary>
    private const string InUse =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"The reference is in use.","status":409}""";

    private const string ProblemContentType = "application/problem+json";

    private const string Company = "Kouassi BTP";

    private const string Site = "Siège";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BP001_AC_13_ARequestSubmittedTwiceInSequenceFoundsOneOrganization()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var founder = Guid.NewGuid();
        var reference = Guid.CreateVersion7();

        var first = await api.FoundAsync(founder, reference, Company, Site);
        var again = await api.FoundAsync(founder, reference, Company, Site);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal((reference, Company, "declared"), first.Organization);
        Assert.Equal(first.Organization, again.Organization);
        await AssertOneFoundingAsync(founder, reference);
    }

    /// <summary>
    /// The first submission is held at its commit; the second is released only once it waits on a key
    /// the first holds: the profile's unique key for a founder without a profile, the organization's
    /// primary key for one who has a profile (contract.md §4).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BP001_AC_14_ARequestSubmittedTwiceInParallelFoundsOneOrganization(bool hasProfile)
    {
        var founder = hasProfile ? (await Founding.CommitAsync(database)).UserId : Guid.NewGuid();
        var gate = new CommitGate();
        await using var api = await FoundingApi.StartAsync(database.Api, Intercepted(gate));
        var reference = Guid.CreateVersion7();

        var first = api.FoundAsync(founder, reference, Company, Site);
        var holder = await gate.Held.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var second = api.FoundAsync(founder, reference, Company, Site);
        await CommitGate.WaitUntilBlockedByAsync(database, holder);
        gate.Release();
        var answers = await Task.WhenAll(first, second);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.OK], answers.Select(answer => answer.Status));
        Assert.All(answers, answer => Assert.Equal((reference, Company, "declared"), answer.Organization));
        await AssertOneFoundingAsync(founder, reference);
    }

    [Fact]
    public async Task BP001_AC_14_TwoFirstFoundingsOfOnePersonAtOnceFoundTwoOrganizationsUnderOneProfile()
    {
        var founder = Guid.NewGuid();
        var gate = new CommitGate();
        await using var api = await FoundingApi.StartAsync(database.Api, Intercepted(gate));
        var one = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();

        var first = api.FoundAsync(founder, one, Company, Site);
        var holder = await gate.Held.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var second = api.FoundAsync(founder, other, "Kouassi Génie Civil", Site);
        await CommitGate.WaitUntilBlockedByAsync(database, holder);
        gate.Release();
        var answers = await Task.WhenAll(first, second);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created], answers.Select(answer => answer.Status));
        Assert.Equal([one, other], answers.Select(answer => answer.Organization.Id));
        var profile = await ProfileOfAsync(founder);
        Assert.Equal(1L, await CountAsync("select count(*) from access.profiles where user_id = @id", founder));
        Assert.Equal(2L, await database.CountAsSuperuserAsync(
            """
            select count(*) from access.organizations o
            join access.ownerships w on w.id = o.current_ownership_id and w.organization_id = o.id
            where w.profile_id = @profile and o.id = any(@organizations)
            """,
            ("profile", profile), ("organizations", new[] { one, other })));
    }

    /// <summary>The answer to the first submission is lost: the request is submitted again later, to an API started again.</summary>
    [Fact]
    public async Task BP001_AC_15_ARequestFoundedAndSubmittedAgainIsAnswered200WithThatOrganization()
    {
        var founder = Guid.NewGuid();
        var reference = Guid.CreateVersion7();
        Answer first;
        await using (var api = await FoundingApi.StartAsync(database.Api))
        {
            first = await api.FoundAsync(founder, reference, Company, Site);
        }

        await using var later = await FoundingApi.StartAsync(database.Api);
        var again = await later.FoundAsync(founder, reference, Company, Site);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(first.Organization, again.Organization);
        await AssertOneFoundingAsync(founder, reference);
    }

    [Fact]
    public async Task BP001_AC_16_AReplayWithOtherNamesIsAnswered200WithTheOriginalOrganization()
    {
        var (_, reference, first, replay) = await ReplayWithOtherNamesAsync();

        Assert.Equal(HttpStatusCode.OK, replay.Status);
        Assert.Equal((reference, Company, "declared"), replay.Organization);
        Assert.Equal(first.Organization, replay.Organization);
    }

    [Fact]
    public async Task BP001_AC_22_AReplayWithOtherNamesLeavesTheOrganizationUnchanged()
    {
        var (founder, reference, _, replay) = await ReplayWithOtherNamesAsync();

        Assert.Equal(HttpStatusCode.OK, replay.Status);
        Assert.Equal(1L, await CountAsync(
            "select count(*) from access.organizations where id = @id and name = 'Kouassi BTP' and updated_at is null and aggregate_version = 1",
            reference));
        Assert.Equal(1L, await CountAsync(
            "select count(*) from access.workspaces where organization_id = @id and name = 'Siège' and updated_at is null and aggregate_version = 1",
            reference));
        await AssertOneFoundingAsync(founder, reference);
    }

    [Fact]
    public async Task BP001_AC_21_AnotherPersonsRequestWithTheReferenceIsAnswered409()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var (owner, reference) = await FoundAsync(api);
        var before = await OrganizationRowAsync(reference);
        var other = Guid.NewGuid();

        var refused = await api.FoundAsync(other, reference, "Autre Société", "Autre Site");

        AssertInUse(refused);
        Assert.Equal(before, await OrganizationRowAsync(reference));
        Assert.Equal(0L, await CountAsync("select count(*) from access.profiles where user_id = @id", other));
        await AssertOneFoundingAsync(owner, reference);
        await AssertFoundsTheirOwnAsync(api, other);
    }

    /// <summary>
    /// No process of BP-001 lets a person belong to an organization without owning it, so the
    /// membership is inserted as the container's superuser, outside <c>saasbtp_api</c>; the
    /// submission itself runs as <c>saasbtp_api</c>.
    /// </summary>
    [Fact]
    public async Task BP001_AC_21_AMemberWhoDoesNotOwnTheOrganizationIsAnswered409()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var (owner, reference) = await FoundAsync(api);
        var member = Guid.NewGuid();
        var profile = Guid.CreateVersion7();
        await using (var setup = await Session.BeginAsync(database.Superuser, subject: null))
        {
            await setup.InsertProfileAsync(profile, member, returning: false);
            await setup.InsertMembershipAsync(Guid.CreateVersion7(), profile, reference, returning: false);
            await setup.CommitAsync();
        }

        var before = await OrganizationRowAsync(reference);

        var refused = await api.FoundAsync(member, reference, Company, Site);

        AssertInUse(refused);
        Assert.Equal(before, await OrganizationRowAsync(reference));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.memberships where profile_id = @profile and organization_id = @organization",
            ("profile", profile), ("organization", reference)));
        Assert.Equal(0L, await database.CountAsSuperuserAsync(
            "select count(*) from access.ownerships where profile_id = @profile", ("profile", profile)));
        await AssertOneFoundingAsync(owner, reference, memberships: 2);
        await AssertFoundsTheirOwnAsync(api, member);
    }

    /// <summary>
    /// An ownership row naming the caller that the organization does not reference, a former one or
    /// an orphan, cannot be created through the API: it is inserted as the container's superuser,
    /// outside <c>saasbtp_api</c>; the submission itself runs as <c>saasbtp_api</c>.
    /// </summary>
    [Fact]
    public async Task BP001_AC_21_ACallerHoldingAnOwnershipTheOrganizationDoesNotReferenceIsAnswered409()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var (owner, reference) = await FoundAsync(api);
        var holder = Guid.NewGuid();
        var profile = Guid.CreateVersion7();
        var orphan = Guid.CreateVersion7();
        await using (var setup = await Session.BeginAsync(database.Superuser, subject: null))
        {
            await setup.InsertProfileAsync(profile, holder, returning: false);
            await setup.InsertOwnershipAsync(orphan, profile, reference, returning: false);
            await setup.CommitAsync();
        }

        var before = await OrganizationRowAsync(reference);

        var refused = await api.FoundAsync(holder, reference, Company, Site);

        AssertInUse(refused);
        Assert.Equal(before, await OrganizationRowAsync(reference));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.ownerships where profile_id = @profile", ("profile", profile)));
        await AssertOneFoundingAsync(owner, reference, ownerships: 2);
        await AssertFoundsTheirOwnAsync(api, holder);
    }

    [Fact]
    public async Task INV_ACCESS_09_The409CarriesOnlyThatTheReferenceIsInUse()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var (_, reference) = await FoundAsync(api);

        var refused = await api.FoundAsync(Guid.NewGuid(), reference, Company, Site);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal(ProblemContentType, refused.ContentType);
        Assert.Equal(InUse, refused.Body);
        Assert.DoesNotContain(reference.ToString(), refused.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Company, refused.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Another transaction commits the founder's profile between the unit of work's profile read and
    /// its profile insert (contract.md §4): the unit of work is started again from its beginning, in
    /// a new transaction, reads that profile, and founds with it.
    /// </summary>
    [Fact]
    public async Task TheProfileConflictIsRetriedOnceFromTheStartInANewTransaction()
    {
        var founder = Guid.NewGuid();
        var conflict = new ProfileConflict(database, founder, persistent: false);
        await using var api = await FoundingApi.StartAsync(database.Api, Intercepted(conflict));
        var reference = Guid.CreateVersion7();

        var answer = await api.FoundAsync(founder, reference, Company, Site);

        Assert.Equal(HttpStatusCode.Created, answer.Status);
        Assert.Equal((reference, Company, "declared"), answer.Organization);

        // Two transactions, each from the profile read; only the first attempt inserted a profile.
        Assert.Equal(2, conflict.Begun);
        Assert.Equal(2, conflict.Reads);
        Assert.Equal(1, conflict.Inserts);

        // One profile, the other transaction's, which owns the company.
        var profile = Assert.Single(conflict.Committed);
        Assert.Equal(profile, await ProfileOfAsync(founder));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            """
            select count(*) from access.organizations o
            join access.ownerships w on w.id = o.current_ownership_id and w.organization_id = o.id
            where w.profile_id = @profile and o.id = @organization
            """,
            ("profile", profile), ("organization", reference)));
        await AssertOneFoundingAsync(founder, reference);
    }

    [Fact]
    public async Task AProfileConflictThatPersistsAfterTheRetryIsAnswered500WithNoDetail()
    {
        var founder = Guid.NewGuid();
        var conflict = new ProfileConflict(database, founder, persistent: true);
        await using var api = await FoundingApi.StartAsync(database.Api, Intercepted(conflict));
        var reference = Guid.CreateVersion7();

        var answer = await api.FoundAsync(founder, reference, Company, Site);

        Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);
        Assert.Empty(answer.Body);
        Assert.Equal(2, conflict.Begun);
        Assert.Equal(2, conflict.Inserts);
        Assert.Equal(0L, await CountAsync("select count(*) from access.organizations where id = @id", reference));
        Assert.Equal(0L, await CountAsync("select count(*) from access.ownerships where organization_id = @id", reference));

        // Logged with its class, never naming the constraint (contract.md §2, ADR-ARCH-032 §5).
        var logged = Assert.Single(api.Logs.Entries, entry => entry.Contains("answered 500:", StringComparison.Ordinal));
        Assert.Contains("PersistenceException", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("ux_profiles__user_id", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReplayAndTheReferenceInUseAreLoggedWithTheirClass()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var (owner, reference) = await FoundAsync(api);
        var profile = await ProfileOfAsync(owner);

        var replay = await api.FoundAsync(owner, reference, Company, Site);
        var refused = await api.FoundAsync(Guid.NewGuid(), reference, Company, Site);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], [replay.Status, refused.Status]);
        Assert.Single(api.Logs.Entries, entry => entry.Contains("answered 200: replay", StringComparison.Ordinal));
        Assert.Single(api.Logs.Entries, entry => entry.Contains("answered 409: identifier in use", StringComparison.Ordinal));

        // Nothing of the owner's data: neither the organization, its name, nor its owner.
        string[] others = [reference.ToString(), Company, owner.ToString(), profile.ToString()];
        Assert.All(api.Logs.Entries, entry =>
            Assert.DoesNotContain(others, value => entry.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    private static Action<IServiceCollection> Intercepted(Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor interceptor) =>
        services => services.ConfigureDbContext<AccessDbContext>(options => options.AddInterceptors(interceptor));

    private static void AssertInUse(Answer refused)
    {
        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal(ProblemContentType, refused.ContentType);
        Assert.Equal(InUse, refused.Body);
    }

    /// <summary>A new person founds a company through the API; its founder and reference.</summary>
    private static async Task<(Guid Founder, Guid Reference)> FoundAsync(FoundingApi api)
    {
        var founder = Guid.NewGuid();
        var reference = Guid.CreateVersion7();
        var founded = await api.FoundAsync(founder, reference, Company, Site);
        Assert.Equal(HttpStatusCode.Created, founded.Status);
        return (founder, reference);
    }

    /// <summary>The refused person's request with a new reference founds their own company.</summary>
    private static async Task AssertFoundsTheirOwnAsync(FoundingApi api, Guid person)
    {
        var reference = Guid.CreateVersion7();
        var own = await api.FoundAsync(person, reference, "Leur Société", "Leur Site");
        Assert.Equal(HttpStatusCode.Created, own.Status);
        Assert.Equal((reference, "Leur Société", "declared"), own.Organization);
    }

    /// <summary>A company founded, then its request submitted again with other names.</summary>
    private async Task<(Guid Founder, Guid Reference, Answer First, Answer Replay)> ReplayWithOtherNamesAsync()
    {
        await using var api = await FoundingApi.StartAsync(database.Api);
        var founder = Guid.NewGuid();
        var reference = Guid.CreateVersion7();
        var first = await api.FoundAsync(founder, reference, Company, Site);
        Assert.Equal(HttpStatusCode.Created, first.Status);

        var replay = await api.FoundAsync(founder, reference, "Kouassi Bâtiment", "Dépôt");
        return (founder, reference, first, replay);
    }

    /// <summary>
    /// The reference names one organization and the rows of its one founding, read past row level
    /// security; the founder has one profile.
    /// </summary>
    private async Task AssertOneFoundingAsync(Guid founder, Guid reference, long ownerships = 1, long memberships = 1)
    {
        Assert.Equal(1L, await CountAsync("select count(*) from access.organizations where id = @id", reference));
        Assert.Equal(ownerships, await CountAsync("select count(*) from access.ownerships where organization_id = @id", reference));
        Assert.Equal(memberships, await CountAsync("select count(*) from access.memberships where organization_id = @id", reference));
        Assert.Equal(1L, await CountAsync("select count(*) from access.workspace_accesses where organization_id = @id", reference));
        Assert.Equal(1L, await CountAsync("select count(*) from access.workspaces where organization_id = @id", reference));
        Assert.Equal(1L, await CountAsync("select count(*) from access.profiles where user_id = @id", founder));
    }

    private Task<long> CountAsync(string sql, Guid id) => database.CountAsSuperuserAsync(sql, ("id", id));

    /// <summary>The founder's profile, read past row level security.</summary>
    private async Task<Guid> ProfileOfAsync(Guid user)
    {
        await using var connection = new NpgsqlConnection(database.Superuser);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("select id from access.profiles where user_id = @user", connection);
        command.Parameters.AddWithValue("user", user);
        return (Guid)(await command.ExecuteScalarAsync(Token))!;
    }

    /// <summary>Every column of the organization's row, read past row level security, to compare before and after.</summary>
    private async Task<string> OrganizationRowAsync(Guid reference)
    {
        await using var connection = new NpgsqlConnection(database.Superuser);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "select row(o.*)::text from access.organizations o where o.id = @id", connection);
        command.Parameters.AddWithValue("id", reference);
        return (string)(await command.ExecuteScalarAsync(Token))!;
    }
}

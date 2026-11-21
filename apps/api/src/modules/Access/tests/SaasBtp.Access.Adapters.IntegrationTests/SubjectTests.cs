using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The tests of ADR-ARCH-032 §6 on the Access tables: the subject of a transaction, and what the
/// policies admit without one, with another one, or with a forged one. Each test first commits the
/// rows it reads past and confirms that they exist, so that no assertion holds on an empty table.
/// </summary>
public sealed class SubjectTests(AccessDatabase database)
{
    [Fact]
    public async Task Adr032_NoRowWithoutASubjectOnAFreshConnection()
    {
        var founder = await Founding.CommitAsync(database);
        await Rows.AssertExistAsync(database, founder);

        // Outside the pool: a server session that never defined the setting.
        var unpooled = new NpgsqlConnectionStringBuilder(database.Api) { Pooling = false }.ConnectionString;
        await using var session = await Session.BeginAsync(unpooled, subject: null);

        Assert.Null(await session.ScalarAsync<string?>("select current_setting('app.user_id', true)"));
        Assert.Empty(await Rows.SeenAsync(session, founder));
        Assert.False(await session.MayActInAsync(founder.WorkspaceId));
        Assert.Null(await session.ScalarAsync<Guid?>("select access.current_profile_id()"));
    }

    [Fact]
    public async Task Adr032_NoRowWithoutASubjectOnAReusedConnectionAfterACommittedTransaction()
    {
        var founder = await Founding.CommitAsync(database);
        await using var connection = await OpenApiAsync();

        int firstSession;
        await using (var session = await Session.OnAsync(connection, founder.UserId))
        {
            firstSession = await session.ScalarAsync<int>("select pg_backend_pid()");
            Assert.Equal(Rows.Of(founder).Select(row => row.Table), await Rows.SeenAsync(session, founder));
            await session.CommitAsync();
        }

        await using (var session = await Session.OnAsync(connection, subject: null))
        {
            Assert.Equal(firstSession, await session.ScalarAsync<int>("select pg_backend_pid()"));
            Assert.Equal(string.Empty, await session.ScalarAsync<string>("select current_setting('app.user_id', true)"));
            Assert.Empty(await Rows.SeenAsync(session, founder));
            Assert.False(await session.MayActInAsync(founder.WorkspaceId));
        }
    }

    [Fact]
    public async Task Adr032_OnOneConnectionTheNextSubjectSeesNothingOfThePrevious()
    {
        var first = await Founding.CommitAsync(database);
        var next = await Founding.CommitAsync(database);
        await using var connection = await OpenApiAsync();

        await using (var session = await Session.OnAsync(connection, first.UserId))
        {
            Assert.Equal(Rows.Of(first).Select(row => row.Table), await Rows.SeenAsync(session, first));
            await session.CommitAsync();
        }

        await using (var session = await Session.OnAsync(connection, next.UserId))
        {
            Assert.Empty(await Rows.SeenAsync(session, first));
            Assert.False(await session.MayActInAsync(first.WorkspaceId));
            Assert.Equal(Rows.Of(next).Select(row => row.Table), await Rows.SeenAsync(session, next));
        }
    }

    [Fact]
    public async Task Adr032_ARolledBackTransactionLeavesNoSubject()
    {
        var founder = await Founding.CommitAsync(database);
        await using var connection = await OpenApiAsync();

        await using (var session = await Session.OnAsync(connection, founder.UserId))
        {
            Assert.Equal(Rows.Of(founder).Select(row => row.Table), await Rows.SeenAsync(session, founder));
            await session.RollbackAsync();
        }

        await using (var session = await Session.OnAsync(connection, subject: null))
        {
            Assert.Null(await session.ScalarAsync<string?>("select nullif(current_setting('app.user_id', true), '')"));
            Assert.Empty(await Rows.SeenAsync(session, founder));
        }
    }

    [Fact]
    public async Task Adr032_ASubjectThatIsNotAnIdentifierMakesTheStatementFail()
    {
        var founder = await Founding.CommitAsync(database);
        await Rows.AssertExistAsync(database, founder);

        // Each table read by the identifier of a row it holds, so that its select policy is evaluated.
        (string Sql, Guid Id)[] reads =
        [
            ("select access.current_profile_id()::text where @id is not null", founder.ProfileId),
            ("select access.may_act_in(@id)::text", founder.WorkspaceId),
            .. Rows.Of(founder).Select(row => ($"select count(*)::text from access.{row.Table} where id = @id", row.Id)),
        ];

        foreach (var (sql, id) in reads)
        {
            await using var session = await database.AsRawAsync("not-a-uuid");
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => session.ScalarAsync<string>(sql, ("id", id)));
            Assert.True(refusal.SqlState == PostgresErrorCodes.InvalidTextRepresentation, $"{sql}: {refusal.SqlState}");
        }

        // Each table's insert policy, on a row it would otherwise check against the founder's records.
        Func<Session, Task>[] inserts =
        [
            session => session.InsertProfileAsync(Guid.CreateVersion7(), founder.UserId, returning: false),
            session => session.InsertOwnershipAsync(Guid.CreateVersion7(), founder.ProfileId, founder.OrganizationId, returning: false),
            session => session.InsertOrganizationAsync(Guid.CreateVersion7(), "Forged", founder.OwnershipId),
            session => session.InsertMembershipAsync(Guid.CreateVersion7(), founder.ProfileId, founder.OrganizationId, returning: false),
            session => session.InsertAccessAsync(
                Guid.CreateVersion7(), founder.ProfileId, founder.WorkspaceId, founder.OrganizationId, returning: false),
            session => session.InsertWorkspaceAsync(Guid.CreateVersion7(), founder.OrganizationId, $"Forged {Guid.NewGuid():N}", returning: false),
        ];

        foreach (var insert in inserts)
        {
            await using var session = await database.AsRawAsync("not-a-uuid");
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => insert(session));
            Assert.Equal(PostgresErrorCodes.InvalidTextRepresentation, refusal.SqlState);
        }
    }

    [Fact]
    public async Task Adr032_NoWorkspaceRowWithoutACurrentAccessEvenByIdentifier()
    {
        var owner = await Founding.CommitAsync(database);
        var other = await Founding.CommitAsync(database);

        await using var session = await database.AsAsync(other.UserId);

        Assert.Equal(0L, await session.CountAsync("select count(*) from access.workspaces where id = @id", ("id", owner.WorkspaceId)));
        Assert.False(await session.MayActInAsync(owner.WorkspaceId));
    }

    [Fact]
    public async Task Adr032_AnAccessToAWorkspaceTheSubjectMayNotActInIsRefused()
    {
        var owner = await Founding.CommitAsync(database);
        var other = await Founding.CommitAsync(database);

        // Under the workspace's own organization, which the subject does not own: the insert policy.
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, other.UserId, session =>
            session.InsertAccessAsync(Guid.CreateVersion7(), other.ProfileId, owner.WorkspaceId, owner.OrganizationId)));

        // Under the subject's own organization: admitted at insert, refused by the deferred key at commit.
        await using var attempt = await database.AsAsync(other.UserId);
        await attempt.InsertAccessAsync(Guid.CreateVersion7(), other.ProfileId, owner.WorkspaceId, other.OrganizationId);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => attempt.CommitAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refusal.SqlState);
    }

    [Fact]
    public async Task Adr032_AMembershipOrAccessInAnOrganizationTheSubjectDoesNotOwnIsRefused()
    {
        var owner = await Founding.CommitAsync(database);
        var other = await Founding.CommitAsync(database);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, other.UserId, session =>
            session.InsertMembershipAsync(Guid.CreateVersion7(), other.ProfileId, owner.OrganizationId)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, other.UserId, session =>
            session.InsertAccessAsync(Guid.CreateVersion7(), other.ProfileId, owner.WorkspaceId, owner.OrganizationId)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, other.UserId, session =>
            session.InsertWorkspaceAsync(Guid.CreateVersion7(), owner.OrganizationId, $"Added {Guid.NewGuid():N}", returning: false)));
    }

    [Fact]
    public async Task Adr032_AClaimOnAnExistingOrganizationIsRefused()
    {
        var owner = await Founding.CommitAsync(database);
        var other = await Founding.CommitAsync(database);
        var claim = Guid.CreateVersion7();

        var refusal = await Session.RefusalAsync(database.Api, other.UserId, async session =>
        {
            await session.InsertOwnershipAsync(claim, other.ProfileId, owner.OrganizationId);
            await session.InsertOrganizationAsync(owner.OrganizationId, "Claimed", claim, returning: false);
        });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal);
        await using var session = await database.AsAsync(owner.UserId);
        Assert.Equal(1L, await session.CountAsync(
            "select count(*) from access.organizations where id = @id and current_ownership_id = @ownership and name = @name",
            ("id", owner.OrganizationId), ("ownership", owner.OwnershipId), ("name", owner.OrganizationName)));
    }

    [Fact]
    public async Task Adr032_TheMigratorReadsNoRowThroughOwnershipAlone()
    {
        var founder = await Founding.CommitAsync(database);
        await Rows.AssertExistAsync(database, founder);

        await using (var session = await Session.BeginAsync(database.Migrator, subject: null))
        {
            Assert.Empty(await Rows.SeenAsync(session, founder));
        }

        // Not even with the founder as subject: the policies apply to saasbtp_api alone.
        await using (var session = await Session.BeginAsync(database.Migrator, founder.UserId))
        {
            Assert.Empty(await Rows.SeenAsync(session, founder));
        }
    }

    private async Task<NpgsqlConnection> OpenApiAsync()
    {
        var connection = new NpgsqlConnection(database.Api);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}

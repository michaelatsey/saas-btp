using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// INV-ACCESS-01: the unique key on the profile's user identifier, the profile policies, and the
/// non-null profile reference of every relation (contract.md §7).
/// </summary>
public sealed class ProfileTests(AccessDatabase database)
{
    [Fact]
    public async Task INV_ACCESS_01_ASecondProfileForOneUserIsRefused()
    {
        var founder = await Founding.CommitAsync(database);

        var refusal = await Session.RefusalAsync(
            database.Api, founder.UserId, session => session.InsertProfileAsync(Guid.CreateVersion7(), founder.UserId));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal);
    }

    [Fact]
    public async Task INV_ACCESS_01_ARelationWithoutTheSubjectsProfileIsRefused()
    {
        var founder = await Founding.CommitAsync(database);
        var other = await Founding.CommitAsync(database);
        var noProfile = Guid.NewGuid();
        Guid?[] profiles = [null, Guid.CreateVersion7(), other.ProfileId];

        foreach (var subject in new[] { founder.UserId, noProfile })
        {
            foreach (var profile in profiles)
            {
                Assert.Equal(
                    PostgresErrorCodes.InsufficientPrivilege,
                    await Session.RefusalAsync(database.Api, subject, session =>
                        session.InsertOwnershipAsync(Guid.CreateVersion7(), profile, founder.OrganizationId)));
                Assert.Equal(
                    PostgresErrorCodes.InsufficientPrivilege,
                    await Session.RefusalAsync(database.Api, subject, session =>
                        session.InsertMembershipAsync(Guid.CreateVersion7(), profile, founder.OrganizationId)));
                Assert.Equal(
                    PostgresErrorCodes.InsufficientPrivilege,
                    await Session.RefusalAsync(database.Api, subject, session =>
                        session.InsertAccessAsync(Guid.CreateVersion7(), profile, founder.WorkspaceId, founder.OrganizationId)));
            }
        }
    }

    [Fact]
    public async Task INV_ACCESS_01_EveryRelationReferencesAProfileThroughANonNullForeignKey()
    {
        const string sql = """
            select count(*)
            from pg_constraint k
            join pg_class c on c.oid = k.conrelid
            join pg_namespace n on n.oid = c.relnamespace
            join pg_attribute a on a.attrelid = c.oid and a.attname = 'profile_id'
            where n.nspname = 'access'
              and c.relname = @table
              and k.contype = 'f'
              and k.confrelid = 'access.profiles'::regclass
              and k.conkey = array[a.attnum]
              and a.attnotnull
            """;

        foreach (var table in new[] { "ownerships", "memberships", "workspace_accesses" })
        {
            Assert.True(
                await database.CountAsSuperuserAsync(sql, ("table", table)) == 1,
                $"access.{table}.profile_id is not a non-null foreign key to access.profiles");
        }
    }

    [Fact]
    public async Task INV_ACCESS_01_TheProfileKeyRefusesARelationWithoutAProfilePastThePolicies()
    {
        // As the container's superuser, which bypasses row level security: the refusal is the
        // constraint's, not the insert policy's. Every other key of the row is satisfied.
        var founder = await Founding.CommitAsync(database);
        (string Table, string Sql, (string, object?)[] Others)[] relations =
        [
            ("ownerships", "insert into access.ownerships (id, profile_id, organization_id, created_at, created_by, aggregate_version) "
                + "values (@id, @profile, @organization, now(), 'integration-test', 1)",
                [("organization", founder.OrganizationId)]),
            ("memberships", "insert into access.memberships (id, profile_id, organization_id, created_at, created_by, aggregate_version) "
                + "values (@id, @profile, @organization, now(), 'integration-test', 1)",
                [("organization", founder.OrganizationId)]),
            ("workspace_accesses", "insert into access.workspace_accesses "
                + "(id, profile_id, workspace_id, organization_id, created_at, created_by, aggregate_version) "
                + "values (@id, @profile, @workspace, @organization, now(), 'integration-test', 1)",
                [("workspace", founder.WorkspaceId), ("organization", founder.OrganizationId)]),
        ];

        foreach (var (table, sql, others) in relations)
        {
            var withoutProfile = await Assert.ThrowsAsync<PostgresException>(() =>
                database.AsSuperuserAsync(sql, [("id", Guid.CreateVersion7()), ("profile", null), .. others]));
            Assert.True(
                withoutProfile.SqlState == PostgresErrorCodes.NotNullViolation && withoutProfile.ColumnName == "profile_id",
                $"{table}: {withoutProfile.SqlState} {withoutProfile.ColumnName}");

            var missingProfile = await Assert.ThrowsAsync<PostgresException>(() =>
                database.AsSuperuserAsync(sql, [("id", Guid.CreateVersion7()), ("profile", Guid.CreateVersion7()), .. others]));
            Assert.True(
                missingProfile.SqlState == PostgresErrorCodes.ForeignKeyViolation && missingProfile.ConstraintName == $"fk_{table}__profiles",
                $"{table}: {missingProfile.SqlState} {missingProfile.ConstraintName}");
        }
    }

    [Fact]
    public async Task INV_ACCESS_01_ASubjectInsertsAndReadsOnlyTheirOwnProfile()
    {
        var founder = await Founding.CommitAsync(database);
        var other = await Founding.CommitAsync(database);

        Assert.Equal(
            PostgresErrorCodes.InsufficientPrivilege,
            await Session.RefusalAsync(database.Api, founder.UserId, session =>
                session.InsertProfileAsync(Guid.CreateVersion7(), Guid.NewGuid())));

        await using var session = await database.AsAsync(founder.UserId);
        Assert.Equal(0L, await session.CountAsync("select count(*) from access.profiles where id = @id", ("id", other.ProfileId)));
        Assert.Equal(1L, await session.CountAsync("select count(*) from access.profiles where id = @id", ("id", founder.ProfileId)));
        Assert.Equal(1L, await session.CountAsync(
            "select count(*) from access.profiles where id = any(@ids)", ("ids", new[] { founder.ProfileId, other.ProfileId })));
        Assert.Equal(founder.ProfileId, await session.ScalarAsync<Guid>("select access.current_profile_id()"));
    }
}

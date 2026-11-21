using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// INV-ACCESS-02: exactly one current owner, by the non-null <c>current_ownership_id</c> and its
/// deferred composite key (ADR-ARCH-033 §6), and the tests of ADR-ARCH-032 §6 for Access.
/// </summary>
public sealed class OwnershipTests(AccessDatabase database)
{
    [Fact]
    public async Task INV_ACCESS_02_AnOrganizationWithoutItsCurrentOwnershipFailsAtCommit()
    {
        var userId = Guid.NewGuid();
        await using var session = await database.AsAsync(userId);
        await session.InsertProfileAsync(Guid.CreateVersion7(), userId);

        // Without RETURNING, so that the refusal is the deferred key's at commit, not the select policy's.
        await session.InsertOrganizationAsync(Guid.CreateVersion7(), "Without owner", Guid.CreateVersion7(), returning: false);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => session.CommitAsync());

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refusal.SqlState);
    }

    [Fact]
    public async Task INV_ACCESS_02_AnOrganizationWhoseCurrentOwnershipBelongsToAnotherFailsAtCommit()
    {
        // The ownership exists, but names another organization: the composite key refuses it.
        var other = await Founding.CommitAsync(database);
        var userId = Guid.NewGuid();
        var profileId = Guid.CreateVersion7();
        var ownershipId = Guid.CreateVersion7();
        await using var session = await database.AsAsync(userId);
        await session.InsertProfileAsync(profileId, userId);
        await session.InsertOwnershipAsync(ownershipId, profileId, other.OrganizationId);
        await session.InsertOrganizationAsync(Guid.CreateVersion7(), "Borrowed owner", ownershipId, returning: false);

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => session.CommitAsync());

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refusal.SqlState);
    }

    [Fact]
    public async Task INV_ACCESS_02_AnOwnershipForSomeoneElsesOrganizationMakesNoOneItsOwner()
    {
        var owner = await Founding.CommitAsync(database);
        var intruder = await Founding.CommitAsync(database);
        var orphan = Guid.CreateVersion7();

        await using (var session = await database.AsAsync(intruder.UserId))
        {
            await session.InsertOwnershipAsync(orphan, intruder.ProfileId, owner.OrganizationId);
            await session.CommitAsync();
        }

        await using (var session = await database.AsAsync(intruder.UserId))
        {
            Assert.Equal(1L, await session.CountAsync("select count(*) from access.ownerships where id = @id", ("id", orphan)));
            Assert.Empty(await Rows.SeenAsync(session, owner));
        }

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, intruder.UserId, session =>
            session.InsertMembershipAsync(Guid.CreateVersion7(), intruder.ProfileId, owner.OrganizationId)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, intruder.UserId, session =>
            session.InsertAccessAsync(Guid.CreateVersion7(), intruder.ProfileId, owner.WorkspaceId, owner.OrganizationId)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Session.RefusalAsync(database.Api, intruder.UserId, session =>
            session.InsertWorkspaceAsync(Guid.CreateVersion7(), owner.OrganizationId, $"Taken {Guid.NewGuid():N}", returning: false)));

        await using (var session = await database.AsAsync(owner.UserId))
        {
            Assert.Equal(1L, await session.CountAsync(
                "select count(*) from access.organizations where id = @id and current_ownership_id = @ownership",
                ("id", owner.OrganizationId), ("ownership", owner.OwnershipId)));
        }
    }
}

using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The tests of ADR-ARCH-033 §5: rows inserted only once the subject may see them, every insert
/// read back with <c>RETURNING</c>, and a workspace visible only through an access of its own
/// organization.
/// </summary>
public sealed class FoundingOrderTests(AccessDatabase database)
{
    [Fact]
    public async Task Adr033_TheFoundingInOrderCommitsAndTheFounderReadsEveryRow()
    {
        var founding = Founding.New();
        await using (var session = await database.AsAsync(founding.UserId))
        {
            await founding.InsertAsync(session);
            await session.CommitAsync();
        }

        await using var reader = await database.AsAsync(founding.UserId);
        Assert.Equal(Rows.Of(founding).Select(row => row.Table), await Rows.SeenAsync(reader, founding));
        Assert.True(await reader.MayActInAsync(founding.WorkspaceId));
        Assert.Equal(founding.ProfileId, await reader.ScalarAsync<Guid>("select access.current_profile_id()"));
        Assert.Equal("declared", await reader.ScalarAsync<string>(
            "select status from access.organizations where id = @id", ("id", founding.OrganizationId)));
    }

    [Fact]
    public async Task Adr033_AnOrganizationBeforeItsOwnershipFails()
    {
        var founding = Founding.New();

        var refusal = await Session.RefusalAsync(database.Api, founding.UserId, async session =>
        {
            await session.InsertProfileAsync(founding.ProfileId, founding.UserId);
            await session.InsertOrganizationAsync(founding.OrganizationId, founding.OrganizationName, founding.OwnershipId);
        });

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refusal);
    }

    [Fact]
    public async Task Adr033_AWorkspaceBeforeItsAccessFails()
    {
        var founding = Founding.New();

        var refusal = await Session.RefusalAsync(database.Api, founding.UserId, async session =>
        {
            await session.InsertProfileAsync(founding.ProfileId, founding.UserId);
            await session.InsertOwnershipAsync(founding.OwnershipId, founding.ProfileId, founding.OrganizationId);
            await session.InsertOrganizationAsync(founding.OrganizationId, founding.OrganizationName, founding.OwnershipId);
            await session.InsertMembershipAsync(founding.MembershipId, founding.ProfileId, founding.OrganizationId);
            await session.InsertWorkspaceAsync(founding.WorkspaceId, founding.OrganizationId, founding.WorkspaceName);
        });

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refusal);
    }

    [Fact]
    public async Task Adr033_AnAccessNamingAnotherOrganizationsWorkspaceGrantsNoSightBeforeRollback()
    {
        var owner = await Founding.CommitAsync(database);
        var other = await Founding.CommitAsync(database);

        await using var session = await database.AsAsync(other.UserId);

        // Admitted at insert: the subject owns the organization the access names.
        await session.InsertAccessAsync(Guid.CreateVersion7(), other.ProfileId, owner.WorkspaceId, other.OrganizationId);

        Assert.Equal(0L, await session.CountAsync("select count(*) from access.workspaces where id = @id", ("id", owner.WorkspaceId)));
        Assert.False(await session.MayActInAsync(owner.WorkspaceId));
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => session.CommitAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refusal.SqlState);
    }
}

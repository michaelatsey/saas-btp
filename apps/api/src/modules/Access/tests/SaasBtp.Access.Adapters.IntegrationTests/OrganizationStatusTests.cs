using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The organization status as a closed set stored as a token under a check (sql.md, Closed sets):
/// the domain is its source of truth (INV-ACCESS-08), the check its defense in depth.
/// </summary>
public sealed class OrganizationStatusTests(AccessDatabase database)
{
    [Theory]
    [InlineData("declared")]
    [InlineData("verified")]
    public async Task ClosedSet_TheStatusCheckAdmitsEachToken(string status)
    {
        var founding = Founding.New();
        await using var session = await database.AsAsync(founding.UserId);
        await session.InsertProfileAsync(founding.ProfileId, founding.UserId);
        await session.InsertOwnershipAsync(founding.OwnershipId, founding.ProfileId, founding.OrganizationId);

        await session.InsertOrganizationAsync(founding.OrganizationId, founding.OrganizationName, founding.OwnershipId, status: status);
        await session.CommitAsync();

        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.organizations where id = @id and status = @status",
            ("id", founding.OrganizationId), ("status", status)));
    }

    [Theory]
    [InlineData("Declared")]
    [InlineData("pending")]
    [InlineData("")]
    public async Task ClosedSet_TheStatusCheckRefusesAnyOtherValue(string status)
    {
        var founding = Founding.New();

        var refusal = await Session.RefusalAsync(database.Api, founding.UserId, async session =>
        {
            await session.InsertProfileAsync(founding.ProfileId, founding.UserId);
            await session.InsertOwnershipAsync(founding.OwnershipId, founding.ProfileId, founding.OrganizationId);
            await session.InsertOrganizationAsync(
                founding.OrganizationId, founding.OrganizationName, founding.OwnershipId, status: status);
        });

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal);
    }
}

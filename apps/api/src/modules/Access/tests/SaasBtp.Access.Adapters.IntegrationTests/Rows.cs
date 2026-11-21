namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>Reads, by identifier, which rows of a founding a session or the superuser sees.</summary>
internal static class Rows
{
    /// <summary>Each Access table with the identifier of the founding's row in it.</summary>
    public static IEnumerable<(string Table, Guid Id)> Of(Founding founding) =>
    [
        ("profiles", founding.ProfileId),
        ("ownerships", founding.OwnershipId),
        ("organizations", founding.OrganizationId),
        ("memberships", founding.MembershipId),
        ("workspace_accesses", founding.AccessId),
        ("workspaces", founding.WorkspaceId),
    ];

    /// <summary>The tables in which <paramref name="session"/> reads the founding's row, by identifier.</summary>
    public static async Task<IReadOnlyList<string>> SeenAsync(Session session, Founding founding)
    {
        var seen = new List<string>();
        foreach (var (table, id) in Of(founding))
        {
            if (await session.CountAsync($"select count(*) from access.{table} where id = @id", ("id", id)) > 0)
            {
                seen.Add(table);
            }
        }

        return seen;
    }

    /// <summary>Fails unless every row of the founding exists, read past row level security.</summary>
    public static async Task AssertExistAsync(AccessDatabase database, Founding founding)
    {
        foreach (var (table, id) in Of(founding))
        {
            Assert.Equal(1L, await database.CountAsSuperuserAsync($"select count(*) from access.{table} where id = @id", ("id", id)));
        }
    }
}

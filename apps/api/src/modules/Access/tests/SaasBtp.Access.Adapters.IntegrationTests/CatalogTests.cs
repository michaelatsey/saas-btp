namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// What the catalog of the migrated database holds for the Access tables and functions:
/// row level security and the policies of contract.md §5 (its test of §7), and the execute
/// privilege on the published functions (ADR-ARCH-028 and ADR-ARCH-032, Consequences).
/// </summary>
public sealed class CatalogTests(AccessDatabase database)
{
    /// <summary>The commands contract.md §5 gives a policy for <c>saasbtp_api</c>, per Access table.</summary>
    private static readonly SortedDictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["memberships"] = "insert,select",
        ["organizations"] = "insert,select",
        ["ownerships"] = "insert,select",
        ["profiles"] = "insert,select",
        ["workspace_accesses"] = "insert,select",
        ["workspaces"] = "insert,select",
    };

    [Fact]
    public async Task PoliciesCoverEveryCommandTheApiUses()
    {
        // A table is listed whatever its policies; a policy applies to saasbtp_api when its roles
        // name it or PUBLIC (role 0), and one for ALL counts for every command. Only a permissive
        // policy admits rows, so the commands covered are its; restrictive ones are listed apart,
        // and none is expected.
        const string sql = """
            with applying as (
                select p.polrelid, p.polpermissive, command
                from pg_policy p
                cross join lateral unnest(case p.polcmd
                    when 'r' then array['select']
                    when 'a' then array['insert']
                    when 'w' then array['update']
                    when 'd' then array['delete']
                    else array['select', 'insert', 'update', 'delete'] end) as command
                where 0 = any(p.polroles) or 'saasbtp_api'::regrole::oid = any(p.polroles)
            )
            select c.relname || ':' || c.relrowsecurity || ':' || c.relforcerowsecurity
                || ':' || coalesce((select string_agg(distinct a.command, ',' order by a.command)
                                    from applying a where a.polrelid = c.oid and a.polpermissive), '')
                || ':' || coalesce((select string_agg(distinct a.command, ',' order by a.command)
                                    from applying a where a.polrelid = c.oid and not a.polpermissive), '')
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'access' and c.relkind in ('r', 'p')
            order by c.relname
            """;

        var actual = await ReadAsync(sql);

        Assert.Equal(Expected.Select(entry => $"{entry.Key}:true:true:{entry.Value}:"), actual);
    }

    [Fact]
    public async Task Adr032_PublicHoldsNoExecuteOnAccessFunctions()
    {
        // The privileges each function holds, its owner's defaults when it has none recorded.
        const string sql = """
            select p.proname || ':' ||
                exists (select 1 from aclexplode(coalesce(p.proacl, acldefault('f', p.proowner))) a
                        where a.grantee = 0 and a.privilege_type = 'EXECUTE') || ':' ||
                has_function_privilege('saasbtp_api', p.oid, 'execute')
            from pg_proc p
            join pg_namespace n on n.oid = p.pronamespace
            where n.nspname = 'access'
            order by p.proname
            """;

        var actual = await ReadAsync(sql);

        Assert.Equal(["current_profile_id:false:true", "may_act_in:false:true"], actual);
    }

    private async Task<List<string>> ReadAsync(string sql)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new Npgsql.NpgsqlConnection(database.Superuser);
        await connection.OpenAsync(cancellationToken);
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}

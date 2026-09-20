using Npgsql;
using Testcontainers.PostgreSql;

namespace SaasBtp.Database.Migrator.Tests;

/// <summary>
/// Anti-drift guard (ADR-ARCH-006) for the PORTABLE BP-001 founding DDL (0001_access_founding_model):
/// <c>access.organizations</c>, <c>access.workspaces</c>, and the three autonomous relation tables
/// <c>access.ownerships</c> / <c>access.organization_memberships</c> / <c>access.workspace_access</c>.
/// Applies the migrator's own runner + embedded scripts to an ephemeral PostgreSQL, then asserts each
/// table's shape (columns/types/nullability, primary key, foreign keys, unique constraints, RLS, and
/// no-default on domain-stamped <c>created_at</c>) directly against <c>information_schema</c> /
/// <c>pg_catalog</c> — there is no EF model to reflect over for the relation tables.
/// <c>access.profiles</c> is asserted separately by <see cref="ProfilesSchemaTests"/>. Requires Docker.
/// </summary>
public sealed class FoundingSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresTestImage.Name)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Single DDL source: the migrator's own runner + embedded scripts. The founding baseline is fully
        // portable (no auth-coupled markers), so IsPortableScript admits every script.
        var result = MigrationRunner.Run(_postgres.GetConnectionString(), MigrationRunner.IsPortableScript);
        result.Successful.ShouldBeTrue(result.Error?.ToString());
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Organizations_HasExactlyTheExpectedColumns()
    {
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["id"] = new("uuid", null, false),
            ["display_name"] = new("character varying", 200, false),
            ["status"] = new("character varying", 32, false),
            ["created_at"] = new("timestamp with time zone", null, false),
        };

        AssertColumns(expected, await ReadColumnsAsync("organizations"));
    }

    [Fact]
    public async Task Workspaces_HasExactlyTheExpectedColumns()
    {
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["id"] = new("uuid", null, false),
            ["organization_id"] = new("uuid", null, false),
            ["display_name"] = new("character varying", 200, false),
            ["created_at"] = new("timestamp with time zone", null, false),
        };

        AssertColumns(expected, await ReadColumnsAsync("workspaces"));
    }

    [Theory]
    [InlineData("ownerships", "organization_id")]
    [InlineData("organization_memberships", "organization_id")]
    [InlineData("workspace_access", "workspace_id")]
    public async Task Relation_HasExactlyTheExpectedColumns(string table, string reference)
    {
        // The three relation tables share one shape: surrogate id, the two references, created_at.
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["id"] = new("uuid", null, false),
            [reference] = new("uuid", null, false),
            ["user_id"] = new("uuid", null, false),
            ["created_at"] = new("timestamp with time zone", null, false),
        };

        AssertColumns(expected, await ReadColumnsAsync(table));
    }

    [Theory]
    [InlineData("organizations", "pk_organizations")]
    [InlineData("workspaces", "pk_workspaces")]
    [InlineData("ownerships", "pk_ownerships")]
    [InlineData("organization_memberships", "pk_organization_memberships")]
    [InlineData("workspace_access", "pk_workspace_access")]
    public async Task Table_PrimaryKey_IsOnId_NamedByConvention(string table, string pkName)
    {
        var pk = await ReadKeyColumnsAsync(table, "PRIMARY KEY");

        pk.ShouldHaveSingleItem();
        pk[0].Constraint.ShouldBe(pkName);
        pk[0].Column.ShouldBe("id");
    }

    [Fact]
    public async Task Ownerships_HasUnique_OnOrganizationId_OneOwnerPerOrganization()
    {
        var unique = await ReadKeyColumnsAsync("ownerships", "UNIQUE");

        // Exactly one owner per organization (INV-2/INV-3); no uniqueness on user_id (a person may own many).
        unique.ShouldHaveSingleItem();
        unique[0].Constraint.ShouldBe("ux_ownerships__organization_id");
        unique[0].Column.ShouldBe("organization_id");
    }

    [Fact]
    public async Task OrganizationMemberships_HasUnique_OnUserThenOrganization()
    {
        var unique = await ReadKeyColumnsAsync("organization_memberships", "UNIQUE");

        // Belonging is binary: one fact per (person, organization). Profile-first (left prefix user_id).
        unique.Select(u => u.Column).ShouldBe(["user_id", "organization_id"]);
        unique.ShouldAllBe(u => u.Constraint == "ux_organization_memberships__user_id_organization_id");
    }

    [Fact]
    public async Task WorkspaceAccess_HasUnique_OnUserThenWorkspace()
    {
        var unique = await ReadKeyColumnsAsync("workspace_access", "UNIQUE");

        // "Can act here" is binary: one fact per (person, workspace). Profile-first (the sync param-query path).
        unique.Select(u => u.Column).ShouldBe(["user_id", "workspace_id"]);
        unique.ShouldAllBe(u => u.Constraint == "ux_workspace_access__user_id_workspace_id");
    }

    [Theory]
    [InlineData("workspaces", "fk_workspaces__organizations")]
    [InlineData("ownerships", "fk_ownerships__organizations")]
    [InlineData("ownerships", "fk_ownerships__profiles")]
    [InlineData("organization_memberships", "fk_organization_memberships__organizations")]
    [InlineData("organization_memberships", "fk_organization_memberships__profiles")]
    [InlineData("workspace_access", "fk_workspace_access__workspaces")]
    [InlineData("workspace_access", "fk_workspace_access__profiles")]
    public async Task Table_HasForeignKey_NamedByConvention(string table, string fkName)
    {
        // The six referential links (Implementation Design §3.3) are enforced as FK constraints.
        (await ForeignKeyExistsAsync(table, fkName)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("organizations")]
    [InlineData("workspaces")]
    [InlineData("ownerships")]
    [InlineData("organization_memberships")]
    [InlineData("workspace_access")]
    public async Task Table_HasRowLevelSecurityEnabled(string table)
    {
        // The load-bearing case is workspace_access (the access edge): RLS-enabled-with-no-policy returns
        // zero rows SILENTLY to a non-privileged consumer. Every founding table enables RLS (sql.md §RLS).
        (await RlsEnabledAsync(table)).ShouldBe(true);
    }

    [Theory]
    [InlineData("organizations")]
    [InlineData("workspaces")]
    [InlineData("ownerships")]
    [InlineData("organization_memberships")]
    [InlineData("workspace_access")]
    public async Task Table_CreatedAt_HasNoDefault(string table)
    {
        // NO DB default: the .NET domain stamps created_at (ADR-ARCH-005), so an omitted stamp fails
        // loudly at INSERT. information_schema returns SQL NULL (-> DBNull) when a column has no default.
        (await ColumnDefaultAsync(table, "created_at")).ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task Organizations_Status_ChecksDeclaredOnly()
    {
        // Defense-in-depth CHECK admits only 'declared'; 'verified' is a future, writer-less status.
        var clause = await CheckClauseAsync("ck_organizations__status");

        clause.ShouldNotBeNull();
        clause!.ShouldContain("declared");
    }

    private static void AssertColumns(
        IReadOnlyDictionary<string, ColumnSpec> expected,
        IReadOnlyDictionary<string, ColumnSpec> actual)
    {
        // Exact column set — catches an added/removed/renamed column, not just a wrong type.
        actual.Keys.ShouldBe(expected.Keys, ignoreOrder: true);

        foreach (var (column, spec) in expected)
        {
            actual[column].DataType.ShouldBe(spec.DataType, $"column '{column}' data_type");
            actual[column].MaxLength.ShouldBe(spec.MaxLength, $"column '{column}' character_maximum_length");
            actual[column].IsNullable.ShouldBe(spec.IsNullable, $"column '{column}' is_nullable");
        }
    }

    private async Task<Dictionary<string, ColumnSpec>> ReadColumnsAsync(string table)
    {
        const string sql =
            """
            SELECT column_name, data_type, character_maximum_length, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'access' AND table_name = @table;
            """;

        var columns = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal);

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var dataType = reader.GetString(1);
            int? maxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            var isNullable = reader.GetString(3) == "YES";
            columns[name] = new ColumnSpec(dataType, maxLength, isNullable);
        }

        return columns;
    }

    private async Task<List<(string Constraint, string Column)>> ReadKeyColumnsAsync(string table, string type)
    {
        const string sql =
            """
            SELECT tc.constraint_name, kcu.column_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.key_column_usage AS kcu
              ON kcu.constraint_schema = tc.constraint_schema
             AND kcu.constraint_name = tc.constraint_name
            WHERE tc.table_schema = 'access'
              AND tc.table_name = @table
              AND tc.constraint_type = @type
            ORDER BY kcu.ordinal_position;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("type", type);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<(string Constraint, string Column)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1)));

        return rows;
    }

    private async Task<bool> ForeignKeyExistsAsync(string table, string constraintName)
    {
        const string sql =
            """
            SELECT count(*)
            FROM information_schema.table_constraints
            WHERE table_schema = 'access'
              AND table_name = @table
              AND constraint_type = 'FOREIGN KEY'
              AND constraint_name = @name;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("name", constraintName);

        // Postgres count(*) is bigint -> Int64; exactly one FK with this name must exist.
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }

    private async Task<bool?> RlsEnabledAsync(string table)
    {
        const string sql =
            """
            SELECT c.relrowsecurity
            FROM pg_catalog.pg_class AS c
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            WHERE n.nspname = 'access' AND c.relname = @table;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);

        return (bool?)await command.ExecuteScalarAsync();
    }

    private async Task<object?> ColumnDefaultAsync(string table, string column)
    {
        const string sql =
            """
            SELECT column_default
            FROM information_schema.columns
            WHERE table_schema = 'access' AND table_name = @table AND column_name = @column;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);

        return await command.ExecuteScalarAsync();
    }

    private async Task<string?> CheckClauseAsync(string constraintName)
    {
        const string sql =
            """
            SELECT check_clause
            FROM information_schema.check_constraints
            WHERE constraint_schema = 'access' AND constraint_name = @name;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", constraintName);

        return (string?)await command.ExecuteScalarAsync();
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private sealed record ColumnSpec(string DataType, int? MaxLength, bool IsNullable);
}

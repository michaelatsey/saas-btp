using Npgsql;
using Testcontainers.PostgreSql;

namespace SaasBtp.Database.Migrator.Tests;

/// <summary>
/// Anti-drift guard (ADR-ARCH-006) for the PORTABLE authorization-bridge DDL declared by 0002:
/// <c>access.tenants</c> and <c>access.memberships</c>. Applies the migrator's own runner + embedded
/// scripts to an ephemeral PostgreSQL, then asserts each table's shape (columns, types, nullability,
/// primary key, CHECK constraints, foreign keys, UNIQUE, indexes, RLS) directly against
/// <c>information_schema</c> / <c>pg_catalog</c>. There is no EF model for these tables — and none may
/// be created — so the schema is introspected with plain Npgsql, exactly like
/// <see cref="ProfilesSchemaTests"/>. Requires Docker.
/// </summary>
/// <remarks>
/// 0002 is PORTABLE by design (it names no GoTrue object), so it runs under the bare-Postgres harness
/// and these assertions execute; the auth-coupled layer (the supabase_auth_admin grant + policy + the
/// JWT hook) is 0003, excluded from this run. One assertion below is a NEGATIVE:
/// <c>ix_memberships__tenant_id</c> must NOT exist — the composite unique index's left prefix already
/// covers tenant-scoped lookups, so a standalone index would be redundant (0002). It is asserted here
/// so a future "helpful" addition fails loudly.
/// </remarks>
public sealed class TenantsAndMembershipsSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresTestImage.Name)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Reuse the migrator's own runner + embedded scripts (single DDL source). Apply only the
        // portable subset (0001, 0002); MigrationRunner.IsPortableScript excludes the auth-coupled
        // script (0003) by the shared marker list — the single source of truth.
        var result = MigrationRunner.Run(
            _postgres.GetConnectionString(),
            MigrationRunner.IsPortableScript);

        result.Successful.ShouldBeTrue(result.Error?.ToString());
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ------------------------------------------------------------------------------------------------
    // access.tenants
    // ------------------------------------------------------------------------------------------------

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Tenants_HasExactlyTheExpectedColumns_WithTypesAndNullability()
    {
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["name"] = new("character varying", MaxLength: 200, IsNullable: false),
            ["status"] = new("character varying", MaxLength: 20, IsNullable: false),
            ["created_at"] = new("timestamp with time zone", MaxLength: null, IsNullable: false),
        };

        await AssertColumnsAsync("tenants", expected);
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Tenants_PrimaryKey_IsPkTenants_OnId() =>
        await AssertSingleColumnPrimaryKeyAsync("tenants", "pk_tenants", "id");

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Tenants_Status_HasCheckConstraint_WithActiveAndSuspended()
    {
        var checks = await ReadCheckConstraintsAsync("tenants");

        checks.ShouldHaveSingleItem();
        checks[0].Name.ShouldBe("ck_tenants__status");
        checks[0].Definition.ShouldContain("active");
        checks[0].Definition.ShouldContain("suspended");
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Tenants_HasRowLevelSecurityEnabled() =>
        (await ReadRowLevelSecurityAsync("tenants")).ShouldBe(true);

    // ------------------------------------------------------------------------------------------------
    // access.memberships
    // ------------------------------------------------------------------------------------------------

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Memberships_HasExactlyTheExpectedColumns_WithTypesAndNullability()
    {
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["tenant_id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["user_id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["role"] = new("character varying", MaxLength: 20, IsNullable: false),
            ["is_active"] = new("boolean", MaxLength: null, IsNullable: false),
            ["created_at"] = new("timestamp with time zone", MaxLength: null, IsNullable: false),
        };

        await AssertColumnsAsync("memberships", expected);
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Memberships_PrimaryKey_IsPkMemberships_OnId() =>
        await AssertSingleColumnPrimaryKeyAsync("memberships", "pk_memberships", "id");

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Memberships_Role_HasCheckConstraint_WithOwnerAdminMember()
    {
        var checks = await ReadCheckConstraintsAsync("memberships");

        checks.ShouldHaveSingleItem();
        checks[0].Name.ShouldBe("ck_memberships__role");
        checks[0].Definition.ShouldContain("owner");
        checks[0].Definition.ShouldContain("admin");
        checks[0].Definition.ShouldContain("member");
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Memberships_HasForeignKeys_ToTenantsAndProfiles()
    {
        const string sql =
            """
            SELECT con.conname,
                   att.attname     AS column_name,
                   ref_cl.relname  AS ref_table,
                   ref_att.attname AS ref_column
            FROM pg_catalog.pg_constraint AS con
            JOIN pg_catalog.pg_class AS cl ON cl.oid = con.conrelid
            JOIN pg_catalog.pg_namespace AS ns ON ns.oid = cl.relnamespace
            JOIN pg_catalog.pg_attribute AS att
              ON att.attrelid = con.conrelid AND att.attnum = con.conkey[1]
            JOIN pg_catalog.pg_class AS ref_cl ON ref_cl.oid = con.confrelid
            JOIN pg_catalog.pg_attribute AS ref_att
              ON ref_att.attrelid = con.confrelid AND ref_att.attnum = con.confkey[1]
            WHERE ns.nspname = 'access' AND cl.relname = 'memberships' AND con.contype = 'f'
            ORDER BY con.conname;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var foreignKeys = new List<(string Name, string Column, string RefTable, string RefColumn)>();
        while (await reader.ReadAsync())
        {
            foreignKeys.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        // Both FKs are intra-context (targets in schema access), single-column, per the naming
        // convention fk_<table>__<ref_table>. Ordered by name: profiles then tenants.
        foreignKeys.Count.ShouldBe(2);
        foreignKeys.ShouldContain(("fk_memberships__profiles", "user_id", "profiles", "user_id"));
        foreignKeys.ShouldContain(("fk_memberships__tenants", "tenant_id", "tenants", "id"));
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Memberships_HasUniqueConstraint_OnTenantIdUserId()
    {
        const string sql =
            """
            SELECT tc.constraint_name, kcu.column_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.key_column_usage AS kcu
              ON kcu.constraint_schema = tc.constraint_schema
             AND kcu.constraint_name = tc.constraint_name
            WHERE tc.table_schema = 'access'
              AND tc.table_name = 'memberships'
              AND tc.constraint_type = 'UNIQUE'
            ORDER BY kcu.ordinal_position;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var uniqueColumns = new List<(string Constraint, string Column)>();
        while (await reader.ReadAsync())
            uniqueColumns.Add((reader.GetString(0), reader.GetString(1)));

        // Composite UNIQUE (tenant_id, user_id) named per convention (ux_<table>__<cols>): one row per
        // user per tenant. Column ORDER matters — tenant_id first is what lets its left prefix serve
        // tenant-scoped lookups (so no standalone tenant_id index; asserted below).
        uniqueColumns.Count.ShouldBe(2);
        uniqueColumns[0].ShouldBe(("ux_memberships__tenant_id_user_id", "tenant_id"));
        uniqueColumns[1].ShouldBe(("ux_memberships__tenant_id_user_id", "user_id"));
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Memberships_HasUserIdIndex_ButNoStandaloneTenantIdIndex()
    {
        const string sql =
            """
            SELECT indexname
            FROM pg_catalog.pg_indexes
            WHERE schemaname = 'access' AND tablename = 'memberships';
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var indexes = new List<string>();
        while (await reader.ReadAsync())
            indexes.Add(reader.GetString(0));

        // The PowerSync user-first lookup index exists...
        indexes.ShouldContain("ix_memberships__user_id");
        // ...and the redundant tenant-first index deliberately does NOT (0002): the composite unique
        // index's left prefix already covers WHERE tenant_id = ?.
        indexes.ShouldNotContain("ix_memberships__tenant_id");
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): legacy access.tenants/memberships DDL (0002) removed on the clean slate; superseded by the BP-001 founding model. Re-enable/replace when that context returns.")]
    public async Task Memberships_HasRowLevelSecurityEnabled() =>
        (await ReadRowLevelSecurityAsync("memberships")).ShouldBe(true);

    // ------------------------------------------------------------------------------------------------
    // Introspection helpers (parameterized by table; same plain-Npgsql approach as ProfilesSchemaTests).
    // ------------------------------------------------------------------------------------------------

    private async Task AssertColumnsAsync(string table, Dictionary<string, ColumnSpec> expected)
    {
        var actual = await ReadColumnsAsync(table);

        // Exact column set — catches an added/removed/renamed column, not just a wrong type.
        actual.Keys.ShouldBe(expected.Keys, ignoreOrder: true);

        foreach (var (column, spec) in expected)
        {
            actual[column].DataType.ShouldBe(spec.DataType, $"{table}.{column} data_type");
            actual[column].MaxLength.ShouldBe(spec.MaxLength, $"{table}.{column} character_maximum_length");
            actual[column].IsNullable.ShouldBe(spec.IsNullable, $"{table}.{column} is_nullable");
        }
    }

    private async Task AssertSingleColumnPrimaryKeyAsync(string table, string expectedName, string expectedColumn)
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
              AND tc.constraint_type = 'PRIMARY KEY'
            ORDER BY kcu.ordinal_position;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync();

        var keyColumns = new List<(string Constraint, string Column)>();
        while (await reader.ReadAsync())
            keyColumns.Add((reader.GetString(0), reader.GetString(1)));

        keyColumns.ShouldHaveSingleItem();
        keyColumns[0].Constraint.ShouldBe(expectedName);
        keyColumns[0].Column.ShouldBe(expectedColumn);
    }

    private async Task<List<CheckConstraint>> ReadCheckConstraintsAsync(string table)
    {
        const string sql =
            """
            SELECT con.conname, pg_catalog.pg_get_constraintdef(con.oid)
            FROM pg_catalog.pg_constraint AS con
            JOIN pg_catalog.pg_class AS cl ON cl.oid = con.conrelid
            JOIN pg_catalog.pg_namespace AS ns ON ns.oid = cl.relnamespace
            WHERE ns.nspname = 'access' AND cl.relname = @table AND con.contype = 'c'
            ORDER BY con.conname;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync();

        var checks = new List<CheckConstraint>();
        while (await reader.ReadAsync())
            checks.Add(new CheckConstraint(reader.GetString(0), reader.GetString(1)));

        return checks;
    }

    private async Task<bool> ReadRowLevelSecurityAsync(string table)
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

        return (bool)(await command.ExecuteScalarAsync())!;
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

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private sealed record ColumnSpec(string DataType, int? MaxLength, bool IsNullable);

    private sealed record CheckConstraint(string Name, string Definition);
}

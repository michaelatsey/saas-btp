using Npgsql;
using Testcontainers.PostgreSql;

namespace SaasBtp.Database.Migrator.Tests;

/// <summary>
/// Anti-drift guard (ADR-ARCH-006) for the PORTABLE site-edge DDL declared by 0004: <c>site.sites</c>
/// and <c>site.site_memberships</c>. Applies the migrator's own runner + embedded scripts to an
/// ephemeral PostgreSQL, then asserts each table's shape (columns, types, nullability, primary key,
/// CHECK constraints, foreign keys, UNIQUE, indexes, RLS, and no-default on domain-stamped columns)
/// directly against <c>information_schema</c> / <c>pg_catalog</c>. There is no EF model for these
/// tables — the site scope provider reads them with plain Npgsql — so the schema is introspected the
/// same way, exactly like <see cref="TenantsAndMembershipsSchemaTests"/>. Requires Docker.
/// </summary>
/// <remarks>
/// 0004 is PORTABLE by design (it names no GoTrue object), so it runs under the bare-Postgres harness
/// and these assertions execute. Two assertions below are NEGATIVES: <c>ix_site_memberships__site_id</c>
/// and <c>ix_site_memberships__tenant_id</c> must NOT exist — the composite unique index's left prefix
/// already covers site-scoped lookups, and the composite FK is validated against the parent, so a
/// standalone child index would be redundant (0004). They are asserted here so a future "helpful"
/// addition fails loudly (the #45 lesson). Conversely <c>ux_sites__id_tenant_id</c> must EXIST: it is
/// the required matching target of the composite FK, not a redundant read-path index.
/// </remarks>
public sealed class SiteSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresTestImage.Name)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Reuse the migrator's own runner + embedded scripts (single DDL source). Apply only the
        // portable subset (0001, 0002, 0004); MigrationRunner.IsPortableScript excludes the auth-coupled
        // script (0003) by the shared marker list — the single source of truth.
        var result = MigrationRunner.Run(
            _postgres.GetConnectionString(),
            MigrationRunner.IsPortableScript);

        result.Successful.ShouldBeTrue(result.Error?.ToString());
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ------------------------------------------------------------------------------------------------
    // site.sites
    // ------------------------------------------------------------------------------------------------

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task Sites_HasExactlyTheExpectedColumns_WithTypesAndNullability()
    {
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["tenant_id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["name"] = new("character varying", MaxLength: 200, IsNullable: false),
            ["status"] = new("character varying", MaxLength: 20, IsNullable: false),
            ["created_at"] = new("timestamp with time zone", MaxLength: null, IsNullable: false),
        };

        await AssertColumnsAsync("sites", expected);
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task Sites_PrimaryKey_IsPkSites_OnId() =>
        await AssertSingleColumnPrimaryKeyAsync("sites", "pk_sites", "id");

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task Sites_Status_HasCheckConstraint_WithActiveAndClosed()
    {
        var checks = await ReadCheckConstraintsAsync("sites");

        checks.ShouldHaveSingleItem();
        checks[0].Name.ShouldBe("ck_sites__status");
        checks[0].Definition.ShouldContain("active");
        checks[0].Definition.ShouldContain("closed");
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task Sites_HasUniqueConstraint_OnIdTenantId_TheCompositeFkTarget()
    {
        var uniqueColumns = await ReadUniqueColumnsAsync("sites");

        // Composite UNIQUE (id, tenant_id) named per convention (ux_<table>__<cols>). It exists SOLELY to
        // be the matching target of site_memberships' composite FK; column ORDER (id first) mirrors the
        // FK's referenced-column order. Asserted PRESENT — it is required, not redundant.
        uniqueColumns.Count.ShouldBe(2);
        uniqueColumns[0].ShouldBe(("ux_sites__id_tenant_id", "id"));
        uniqueColumns[1].ShouldBe(("ux_sites__id_tenant_id", "tenant_id"));
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task Sites_CreatedAt_HasNoDefault() =>
        (await ReadColumnDefaultAsync("sites", "created_at")).ShouldBeNull();

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task Sites_HasRowLevelSecurityEnabled() =>
        (await ReadRowLevelSecurityAsync("sites")).ShouldBe(true);

    // ------------------------------------------------------------------------------------------------
    // site.site_memberships
    // ------------------------------------------------------------------------------------------------

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMemberships_HasExactlyTheExpectedColumns_WithTypesAndNullability()
    {
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["site_id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["tenant_id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["user_id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["origin_tenant_id"] = new("uuid", MaxLength: null, IsNullable: true),
            ["role"] = new("character varying", MaxLength: 20, IsNullable: false),
            ["is_active"] = new("boolean", MaxLength: null, IsNullable: false),
            ["valid_from"] = new("timestamp with time zone", MaxLength: null, IsNullable: false),
            ["valid_until"] = new("timestamp with time zone", MaxLength: null, IsNullable: true),
            ["created_at"] = new("timestamp with time zone", MaxLength: null, IsNullable: false),
        };

        await AssertColumnsAsync("site_memberships", expected);
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMemberships_PrimaryKey_IsPkSiteMemberships_OnId() =>
        await AssertSingleColumnPrimaryKeyAsync("site_memberships", "pk_site_memberships", "id");

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMemberships_HasRoleAndValidWindowCheckConstraints()
    {
        var checks = await ReadCheckConstraintsAsync("site_memberships");

        // Exactly the two expected CHECKs — catches an added/removed/renamed constraint.
        checks.Select(check => check.Name).ShouldBe(
            ["ck_site_memberships__role", "ck_site_memberships__valid_window"], ignoreOrder: true);

        var role = checks.Single(check => check.Name == "ck_site_memberships__role");
        role.Definition.ShouldContain("site_manager");
        role.Definition.ShouldContain("member");

        // valid_until, when set, is strictly after valid_from.
        var window = checks.Single(check => check.Name == "ck_site_memberships__valid_window");
        window.Definition.ShouldContain("valid_until");
        window.Definition.ShouldContain("valid_from");
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMemberships_HasCompositeForeignKey_ToSites()
    {
        const string sql =
            """
            SELECT con.conname,
                   ARRAY(SELECT att.attname::text
                         FROM unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
                         JOIN pg_catalog.pg_attribute AS att
                           ON att.attrelid = con.conrelid AND att.attnum = k.attnum
                         ORDER BY k.ord)   AS columns,
                   ref_cl.relname          AS ref_table,
                   ARRAY(SELECT att.attname::text
                         FROM unnest(con.confkey) WITH ORDINALITY AS k(attnum, ord)
                         JOIN pg_catalog.pg_attribute AS att
                           ON att.attrelid = con.confrelid AND att.attnum = k.attnum
                         ORDER BY k.ord)   AS ref_columns
            FROM pg_catalog.pg_constraint AS con
            JOIN pg_catalog.pg_class AS cl ON cl.oid = con.conrelid
            JOIN pg_catalog.pg_namespace AS ns ON ns.oid = cl.relnamespace
            JOIN pg_catalog.pg_class AS ref_cl ON ref_cl.oid = con.confrelid
            WHERE ns.nspname = 'site' AND cl.relname = 'site_memberships' AND con.contype = 'f'
            ORDER BY con.conname;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var foreignKeys = new List<(string Name, string[] Columns, string RefTable, string[] RefColumns)>();
        while (await reader.ReadAsync())
        {
            foreignKeys.Add((
                reader.GetString(0),
                reader.GetFieldValue<string[]>(1),
                reader.GetString(2),
                reader.GetFieldValue<string[]>(3)));
        }

        // Exactly one FK: the COMPOSITE (site_id, tenant_id) -> sites (id, tenant_id). Composite on
        // purpose — it makes the denormalized tenant_id physically unable to diverge from the site's
        // tenant. Intra-context, so the cross-context FK ban does not apply. Column ORDER is asserted.
        foreignKeys.ShouldHaveSingleItem();
        foreignKeys[0].Name.ShouldBe("fk_site_memberships__sites");
        foreignKeys[0].Columns.ShouldBe(["site_id", "tenant_id"]);
        foreignKeys[0].RefTable.ShouldBe("sites");
        foreignKeys[0].RefColumns.ShouldBe(["id", "tenant_id"]);
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMemberships_HasUniqueConstraint_OnSiteIdUserId()
    {
        var uniqueColumns = await ReadUniqueColumnsAsync("site_memberships");

        // Composite UNIQUE (site_id, user_id) named per convention (ux_<table>__<cols>): one role per
        // user per site. Column ORDER matters — site_id first is what lets its left prefix serve
        // site-scoped lookups (so no standalone site_id index; asserted below).
        uniqueColumns.Count.ShouldBe(2);
        uniqueColumns[0].ShouldBe(("ux_site_memberships__site_id_user_id", "site_id"));
        uniqueColumns[1].ShouldBe(("ux_site_memberships__site_id_user_id", "user_id"));
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMemberships_HasUserIdIndex_ButNoStandaloneSiteIdOrTenantIdIndex()
    {
        const string sql =
            """
            SELECT indexname
            FROM pg_catalog.pg_indexes
            WHERE schemaname = 'site' AND tablename = 'site_memberships';
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var indexes = new List<string>();
        while (await reader.ReadAsync())
            indexes.Add(reader.GetString(0));

        // The PowerSync user-first lookup index exists (no other index's left prefix is user_id)...
        indexes.ShouldContain("ix_site_memberships__user_id");
        // ...and the redundant site-first / tenant-first indexes deliberately do NOT (0004): the
        // composite unique's left prefix (site_id) covers WHERE site_id = ?, and the FK is validated
        // against the parent, so neither child index would earn its write cost.
        indexes.ShouldNotContain("ix_site_memberships__site_id");
        indexes.ShouldNotContain("ix_site_memberships__tenant_id");
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMemberships_CreatedAtAndValidFrom_HaveNoDefault()
    {
        // Both are domain-stamped (ADR-ARCH-005); a DB default would mask a domain that forgot to stamp.
        (await ReadColumnDefaultAsync("site_memberships", "created_at")).ShouldBeNull();
        (await ReadColumnDefaultAsync("site_memberships", "valid_from")).ShouldBeNull();
    }

    [Fact(Skip = "BP-001 perimeter (plan Step 0): Site DDL (0004) removed on the clean slate. Re-enable when Site returns.")]
    public async Task SiteMemberships_HasRowLevelSecurityEnabled() =>
        (await ReadRowLevelSecurityAsync("site_memberships")).ShouldBe(true);

    // ------------------------------------------------------------------------------------------------
    // Introspection helpers (schema 'site'; same plain-Npgsql approach as TenantsAndMembershipsSchemaTests).
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
            WHERE tc.table_schema = 'site'
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

    private async Task<List<(string Constraint, string Column)>> ReadUniqueColumnsAsync(string table)
    {
        const string sql =
            """
            SELECT tc.constraint_name, kcu.column_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.key_column_usage AS kcu
              ON kcu.constraint_schema = tc.constraint_schema
             AND kcu.constraint_name = tc.constraint_name
            WHERE tc.table_schema = 'site'
              AND tc.table_name = @table
              AND tc.constraint_type = 'UNIQUE'
            ORDER BY kcu.ordinal_position;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync();

        var uniqueColumns = new List<(string Constraint, string Column)>();
        while (await reader.ReadAsync())
            uniqueColumns.Add((reader.GetString(0), reader.GetString(1)));

        return uniqueColumns;
    }

    private async Task<List<CheckConstraint>> ReadCheckConstraintsAsync(string table)
    {
        const string sql =
            """
            SELECT con.conname, pg_catalog.pg_get_constraintdef(con.oid)
            FROM pg_catalog.pg_constraint AS con
            JOIN pg_catalog.pg_class AS cl ON cl.oid = con.conrelid
            JOIN pg_catalog.pg_namespace AS ns ON ns.oid = cl.relnamespace
            WHERE ns.nspname = 'site' AND cl.relname = @table AND con.contype = 'c'
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
            WHERE n.nspname = 'site' AND c.relname = @table;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string?> ReadColumnDefaultAsync(string table, string column)
    {
        const string sql =
            """
            SELECT column_default
            FROM information_schema.columns
            WHERE table_schema = 'site' AND table_name = @table AND column_name = @column;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    private async Task<Dictionary<string, ColumnSpec>> ReadColumnsAsync(string table)
    {
        const string sql =
            """
            SELECT column_name, data_type, character_maximum_length, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'site' AND table_name = @table;
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

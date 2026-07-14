using Npgsql;
using Testcontainers.PostgreSql;

namespace SaasBtp.Database.Migrator.Tests;

/// <summary>
/// Anti-drift guard (ADR-ARCH-006) for the PORTABLE onboarding-by-invitation DDL declared by 0005:
/// <c>access.invitations</c> and <c>access.invitation_sites</c> (ADR-ARCH-011). Applies the migrator's
/// own runner + embedded scripts to an ephemeral PostgreSQL, then asserts each table's shape (columns,
/// types, nullability, primary key, CHECK constraints, foreign keys, UNIQUE, indexes, RLS, and
/// no-default on domain-stamped columns) directly against <c>information_schema</c> / <c>pg_catalog</c>.
/// There is no EF model for these tables, so the schema is introspected with plain Npgsql, exactly like
/// <see cref="TenantsAndMembershipsSchemaTests"/>. Requires Docker.
/// </summary>
/// <remarks>
/// 0005 is PORTABLE by design (it names no GoTrue object), so it runs under the bare-Postgres harness
/// and these assertions execute. Several assertions are NEGATIVES, so a well-meaning "fix" that ADDS
/// something forbidden fails loudly (the #45 lesson): <c>access.invitations</c> has ZERO foreign keys
/// (no cross-context / auth FK, no FK to <c>access.tenants</c>); <c>access.invitation_sites</c> has
/// EXACTLY ONE FK — to <c>access.invitations</c>, <c>ON DELETE RESTRICT</c>, never CASCADE (asserted via
/// <c>pg_constraint.confdeltype</c>, the first place the repo pins a delete action); neither table
/// carries a speculative read-path index. Conversely the partial unique
/// <c>ux_invitations__tenant_id_email__pending</c> must EXIST (with its <c>WHERE</c> predicate): it is
/// the structural guard against two live invitations for the same (tenant, e-mail).
/// </remarks>
public sealed class InvitationsSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresTestImage.Name)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Reuse the migrator's own runner + embedded scripts (single DDL source). Apply only the
        // portable subset (0001, 0002, 0004, 0005); MigrationRunner.IsPortableScript excludes the
        // auth-coupled script (0003) by the shared marker list — the single source of truth.
        var result = MigrationRunner.Run(
            _postgres.GetConnectionString(),
            MigrationRunner.IsPortableScript);

        result.Successful.ShouldBeTrue(result.Error?.ToString());
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ------------------------------------------------------------------------------------------------
    // access.invitations
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Invitations_HasExactlyTheExpectedColumns_WithTypesAndNullability()
    {
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["id"] = new("uuid", MaxLength: null, IsNullable: false),
            // email is a COPY CONSTRAINT to access.profiles.email (varchar(255), 0002), NOT RFC 5321's
            // theoretical 320 — a wider invitation e-mail would fail the profile insert at acceptance.
            ["email"] = new("character varying", MaxLength: 255, IsNullable: false),
            // token_hash is char(64): SHA-256 lowercase hex, fixed width.
            ["token_hash"] = new("character", MaxLength: 64, IsNullable: false),
            ["status"] = new("character varying", MaxLength: 20, IsNullable: false),
            ["expires_at"] = new("timestamp with time zone", MaxLength: null, IsNullable: false),
            ["tenant_id"] = new("uuid", MaxLength: null, IsNullable: false),
            // tenant_role NULL => external invitee (no organization edge). The only nullable role.
            ["tenant_role"] = new("character varying", MaxLength: 20, IsNullable: true),
            ["invited_by"] = new("uuid", MaxLength: null, IsNullable: false),
            ["created_at"] = new("timestamp with time zone", MaxLength: null, IsNullable: false),
            ["accepted_at"] = new("timestamp with time zone", MaxLength: null, IsNullable: true),
        };

        await AssertColumnsAsync("invitations", expected);
    }

    [Fact]
    public async Task Invitations_PrimaryKey_IsPkInvitations_OnId() =>
        await AssertPrimaryKeyAsync("invitations", "pk_invitations", "id");

    [Fact]
    public async Task Invitations_HasUniqueConstraint_OnTokenHash()
    {
        var uniqueColumns = await ReadUniqueColumnsAsync("invitations");

        // Exactly ONE unique CONSTRAINT: ux_invitations__token_hash on token_hash — the acceptance
        // lookup path (WHERE token_hash = ?). The partial unique on (tenant_id, email) is a unique
        // INDEX, not a constraint, so it deliberately does NOT appear here (asserted separately below).
        uniqueColumns.ShouldHaveSingleItem();
        uniqueColumns[0].ShouldBe(("ux_invitations__token_hash", "token_hash"));
    }

    [Fact]
    public async Task Invitations_HasExactlyTheThreeExpectedCheckConstraints()
    {
        var checks = await ReadCheckConstraintsAsync("invitations");

        // Exactly the three expected CHECKs — catches an added/removed/renamed constraint.
        checks.Select(check => check.Name).ShouldBe(
            ["ck_invitations__status", "ck_invitations__tenant_role", "ck_invitations__token_hash_hex"],
            ignoreOrder: true);

        // status is the closed set pending/accepted/revoked — 'expired' is DERIVED, never stored.
        var status = checks.Single(check => check.Name == "ck_invitations__status");
        status.Definition.ShouldContain("pending");
        status.Definition.ShouldContain("accepted");
        status.Definition.ShouldContain("revoked");

        // tenant_role mirrors access.memberships.role; NULL is allowed (external invitee).
        var tenantRole = checks.Single(check => check.Name == "ck_invitations__tenant_role");
        tenantRole.Definition.ShouldContain("owner");
        tenantRole.Definition.ShouldContain("admin");
        tenantRole.Definition.ShouldContain("member");

        // token_hash must be exactly 64 lowercase hex chars — makes '', 'abc', an uppercased hash, or a
        // leaked clear token structurally impossible.
        var tokenHash = checks.Single(check => check.Name == "ck_invitations__token_hash_hex");
        tokenHash.Definition.ShouldContain("[0-9a-f]");
        tokenHash.Definition.ShouldContain("64");
    }

    [Fact]
    public async Task Invitations_HasPartialUniqueIndex_OnTenantIdEmail_WherePending()
    {
        var indexes = await ReadIndexesAsync("invitations");

        // The structural guard "at most one LIVE invitation per (tenant, e-mail)" — blocks an
        // invitee-driven role escalation (#45 family). It EXISTS, is UNIQUE, on (tenant_id, email), and
        // is PARTIAL on status = 'pending' (so re-inviting after revoke/accept stays allowed). The exact
        // parenthesization/casts of the predicate are left to Postgres's pg_get_indexdef rendering (a
        // varchar column is cast to text), so the predicate is asserted by robust substrings, not a
        // hand-guessed literal.
        var definition = indexes
            .Where(index => index.Name == "ux_invitations__tenant_id_email__pending")
            .Select(index => index.Definition)
            .ShouldHaveSingleItem();

        definition.ShouldContain("UNIQUE");
        definition.ShouldContain("tenant_id");
        definition.ShouldContain("email");
        definition.ShouldContain("WHERE");
        definition.ShouldContain("status");
        definition.ShouldContain("'pending'");
    }

    [Fact]
    public async Task Invitations_HasNoForeignKeys()
    {
        var foreignKeys = await ReadForeignKeysAsync("invitations");

        // ZERO FKs: tenant_id and invited_by are cross-bounded-context / auth-boundary references
        // (precedent: safety.constats.tenant_id, 0001). This is the guard that catches a well-meaning
        // FK to access.tenants, access.profiles, or any auth.* object added later.
        foreignKeys.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invitations_HasNoStandaloneEmailOrTenantIdIndex()
    {
        var indexes = (await ReadIndexesAsync("invitations")).Select(index => index.Name).ToList();

        // The only non-PK indexes are the token_hash unique and the (tenant_id, email) partial unique.
        // A standalone email or tenant_id read-path index has NO reader yet (the verify endpoint and
        // GET /me/workspaces are later steps of #48); asserted absent so a speculative addition fails
        // loudly (the #45 lesson).
        indexes.ShouldNotContain("ix_invitations__email");
        indexes.ShouldNotContain("ix_invitations__tenant_id");
    }

    [Fact]
    public async Task Invitations_DomainStampedTimestamps_HaveNoDefault()
    {
        // created_at, expires_at, accepted_at are domain-stamped (ADR-ARCH-005); a DB default would mask
        // a domain that forgot to stamp. expires_at especially: the 7-day validity is CONFIGURATION, not
        // a DDL `DEFAULT now() + interval`.
        (await ReadColumnDefaultAsync("invitations", "created_at")).ShouldBeNull();
        (await ReadColumnDefaultAsync("invitations", "expires_at")).ShouldBeNull();
        (await ReadColumnDefaultAsync("invitations", "accepted_at")).ShouldBeNull();
    }

    [Fact]
    public async Task Invitations_HasRowLevelSecurityEnabled() =>
        (await ReadRowLevelSecurityAsync("invitations")).ShouldBe(true);

    // ------------------------------------------------------------------------------------------------
    // access.invitation_sites
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task InvitationSites_HasExactlyTheExpectedColumns_WithTypesAndNullability()
    {
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["invitation_id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["site_id"] = new("uuid", MaxLength: null, IsNullable: false),
            // site_role mirrors site.site_memberships.role (0004) so acceptance is a COPY.
            ["site_role"] = new("character varying", MaxLength: 20, IsNullable: false),
            ["valid_from"] = new("timestamp with time zone", MaxLength: null, IsNullable: false),
            ["valid_until"] = new("timestamp with time zone", MaxLength: null, IsNullable: true),
        };

        await AssertColumnsAsync("invitation_sites", expected);
    }

    [Fact]
    public async Task InvitationSites_PrimaryKey_IsCompositePk_OnInvitationIdSiteId() =>
        // Natural composite PK: the invariant "one site edge per (invitation, site)" IS the identity —
        // no surrogate id, no separate unique. Column ORDER matters — invitation_id first, so its left
        // prefix serves "all sites for this invitation".
        await AssertPrimaryKeyAsync("invitation_sites", "pk_invitation_sites", "invitation_id", "site_id");

    [Fact]
    public async Task InvitationSites_HasSingleForeignKey_ToInvitations_OnDeleteRestrict()
    {
        var foreignKeys = await ReadForeignKeysAsync("invitation_sites");

        // EXACTLY ONE FK, to access.invitations(id). The exact-single-FK assertion is itself the guard:
        // any FK added to site.sites or any auth.* object makes the count != 1 and fails here.
        var foreignKey = foreignKeys.ShouldHaveSingleItem();
        foreignKey.Name.ShouldBe("fk_invitation_sites__invitations");
        foreignKey.Column.ShouldBe("invitation_id");
        foreignKey.RefTable.ShouldBe("invitations");
        foreignKey.RefColumn.ShouldBe("id");
        // ON DELETE RESTRICT ('r'), NEVER CASCADE ('c'): an invitation is never deleted (it is the audit
        // trail); RESTRICT makes an accidental parent delete FAIL rather than silently cascade.
        foreignKey.DeleteAction.ShouldBe("r");
    }

    [Fact]
    public async Task InvitationSites_HasExactlyTheTwoExpectedCheckConstraints()
    {
        var checks = await ReadCheckConstraintsAsync("invitation_sites");

        checks.Select(check => check.Name).ShouldBe(
            ["ck_invitation_sites__site_role", "ck_invitation_sites__valid_window"], ignoreOrder: true);

        var siteRole = checks.Single(check => check.Name == "ck_invitation_sites__site_role");
        siteRole.Definition.ShouldContain("site_manager");
        siteRole.Definition.ShouldContain("member");

        // valid_until, when set, is strictly after valid_from (mirrors ck_site_memberships__valid_window).
        var window = checks.Single(check => check.Name == "ck_invitation_sites__valid_window");
        window.Definition.ShouldContain("valid_until");
        window.Definition.ShouldContain("valid_from");
    }

    [Fact]
    public async Task InvitationSites_HasNoStandaloneSiteIdIndex()
    {
        var indexes = (await ReadIndexesAsync("invitation_sites")).Select(index => index.Name).ToList();

        // The sole read path ("all sites for this invitation") is served by the PK's left prefix
        // (invitation_id); site_id has no reader of its own (it is copied into site.site_memberships and
        // read there). A standalone site_id index would only slow writes (the #45 lesson).
        indexes.ShouldNotContain("ix_invitation_sites__site_id");
    }

    [Fact]
    public async Task InvitationSites_DomainStampedTimestamps_HaveNoDefault()
    {
        // valid_from (BUSINESS start) and valid_until (planned end) are domain-stamped (ADR-ARCH-005).
        (await ReadColumnDefaultAsync("invitation_sites", "valid_from")).ShouldBeNull();
        (await ReadColumnDefaultAsync("invitation_sites", "valid_until")).ShouldBeNull();
    }

    [Fact]
    public async Task InvitationSites_HasRowLevelSecurityEnabled() =>
        (await ReadRowLevelSecurityAsync("invitation_sites")).ShouldBe(true);

    // ------------------------------------------------------------------------------------------------
    // Introspection helpers (schema 'access'; same plain-Npgsql approach as TenantsAndMembershipsSchemaTests).
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

    /// <summary>
    /// Asserts the primary key of <paramref name="table"/> is named <paramref name="expectedName"/> and
    /// covers exactly <paramref name="expectedColumns"/> in order. Handles both the single-column PK
    /// (invitations) and the composite PK (invitation_sites).
    /// </summary>
    private async Task AssertPrimaryKeyAsync(string table, string expectedName, params string[] expectedColumns)
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

        keyColumns.ShouldAllBe(key => key.Constraint == expectedName);
        keyColumns.Select(key => key.Column).ShouldBe(expectedColumns);
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
            WHERE tc.table_schema = 'access'
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

    /// <summary>
    /// Foreign keys of <paramref name="table"/> with their single referencing/referenced columns and the
    /// ON DELETE action (<c>confdeltype</c>, cast to text: 'a'=NO ACTION, 'r'=RESTRICT, 'c'=CASCADE,
    /// 'n'=SET NULL, 'd'=SET DEFAULT). Both FKs 0005 concerns are single-column, so conkey[1]/confkey[1].
    /// </summary>
    private async Task<List<ForeignKey>> ReadForeignKeysAsync(string table)
    {
        const string sql =
            """
            SELECT con.conname,
                   att.attname          AS column_name,
                   ref_cl.relname       AS ref_table,
                   ref_att.attname      AS ref_column,
                   con.confdeltype::text AS delete_action
            FROM pg_catalog.pg_constraint AS con
            JOIN pg_catalog.pg_class AS cl ON cl.oid = con.conrelid
            JOIN pg_catalog.pg_namespace AS ns ON ns.oid = cl.relnamespace
            JOIN pg_catalog.pg_attribute AS att
              ON att.attrelid = con.conrelid AND att.attnum = con.conkey[1]
            JOIN pg_catalog.pg_class AS ref_cl ON ref_cl.oid = con.confrelid
            JOIN pg_catalog.pg_attribute AS ref_att
              ON ref_att.attrelid = con.confrelid AND ref_att.attnum = con.confkey[1]
            WHERE ns.nspname = 'access' AND cl.relname = @table AND con.contype = 'f'
            ORDER BY con.conname;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync();

        var foreignKeys = new List<ForeignKey>();
        while (await reader.ReadAsync())
        {
            foreignKeys.Add(new ForeignKey(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4)));
        }

        return foreignKeys;
    }

    private async Task<List<(string Name, string Definition)>> ReadIndexesAsync(string table)
    {
        const string sql =
            """
            SELECT indexname, indexdef
            FROM pg_catalog.pg_indexes
            WHERE schemaname = 'access' AND tablename = @table;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync();

        var indexes = new List<(string Name, string Definition)>();
        while (await reader.ReadAsync())
            indexes.Add((reader.GetString(0), reader.GetString(1)));

        return indexes;
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

    private async Task<string?> ReadColumnDefaultAsync(string table, string column)
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

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
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

    private sealed record ForeignKey(string Name, string Column, string RefTable, string RefColumn, string DeleteAction);
}

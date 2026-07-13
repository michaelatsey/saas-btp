using Npgsql;
using Testcontainers.PostgreSql;

namespace SaasBtp.Database.Migrator.Tests;

/// <summary>
/// Anti-drift guard (ADR-ARCH-006) for the PORTABLE Access DDL: applies the migrator's own runner +
/// embedded scripts to an ephemeral PostgreSQL, then asserts the shape of <c>access.profiles</c>
/// (columns, types, nullability, primary key, RLS) directly against <c>information_schema</c> /
/// <c>pg_catalog</c>. There is no Access DbContext to reflect over — and none may be created — so the
/// schema is introspected with plain Npgsql. A fresh, freshly-migrated container keeps the assertions
/// isolated. Requires Docker.
/// </summary>
/// <remarks>
/// The auth-coupled script (0003 identity auth: the supabase_auth_admin grant, the FOR SELECT policy,
/// and the JWT hook it executes) is DELIBERATELY excluded: it references <c>supabase_auth_admin</c>,
/// which exists only in a real
/// Supabase project. We do NOT fabricate a fake GoTrue role to "test" it — a green run against a
/// fabricated auth surface proves nothing and gives false confidence about the exact thing most likely
/// to break every login. It is validated by the manual checklist instead (see the specifications). The
/// exclusion is an EXPLICIT list, not an include-match: an auth-coupled script added without being
/// listed in <see cref="MigrationRunner.AuthCoupledScriptMarkers"/> will run on bare Postgres and FAIL
/// — the intended loud signal.
/// </remarks>
public sealed class ProfilesSchemaTests : IAsyncLifetime
{
    // Image passed to the builder constructor (Testcontainers 4.13 deprecated the parameterless ctor
    // + WithImage), same shared, env-overridable tag as the Safety harness.
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

    [Fact]
    public async Task Profiles_HasExactlyTheExpectedColumns_WithTypesAndNullability()
    {
        // Identity model (0002): profiles is the human projection — no tenant_id (that lives on
        // memberships), email + given/family name, job_function. full_name is the entered, NOT NULL
        // display name (never a concatenation of given/family).
        var expected = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal)
        {
            ["user_id"] = new("uuid", MaxLength: null, IsNullable: false),
            ["email"] = new("character varying", MaxLength: 255, IsNullable: false),
            ["full_name"] = new("character varying", MaxLength: 200, IsNullable: false),
            ["given_name"] = new("character varying", MaxLength: 200, IsNullable: true),
            ["family_name"] = new("character varying", MaxLength: 200, IsNullable: true),
            ["job_function"] = new("character varying", MaxLength: 150, IsNullable: true),
            ["created_at"] = new("timestamp with time zone", MaxLength: null, IsNullable: false),
        };

        var actual = await ReadColumnsAsync();

        // Exact column set — catches an added/removed/renamed column, not just a wrong type.
        actual.Keys.ShouldBe(expected.Keys, ignoreOrder: true);

        foreach (var (column, spec) in expected)
        {
            actual[column].DataType.ShouldBe(spec.DataType, $"column '{column}' data_type");
            actual[column].MaxLength.ShouldBe(spec.MaxLength, $"column '{column}' character_maximum_length");
            actual[column].IsNullable.ShouldBe(spec.IsNullable, $"column '{column}' is_nullable");
        }
    }

    [Fact]
    public async Task Profiles_PrimaryKey_IsPkProfiles_OnUserId()
    {
        const string sql =
            """
            SELECT tc.constraint_name, kcu.column_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.key_column_usage AS kcu
              ON kcu.constraint_schema = tc.constraint_schema
             AND kcu.constraint_name = tc.constraint_name
            WHERE tc.table_schema = 'access'
              AND tc.table_name = 'profiles'
              AND tc.constraint_type = 'PRIMARY KEY'
            ORDER BY kcu.ordinal_position;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var keyColumns = new List<(string Constraint, string Column)>();
        while (await reader.ReadAsync())
            keyColumns.Add((reader.GetString(0), reader.GetString(1)));

        // Single-column PK named per convention (pk_<table>), keyed on user_id.
        keyColumns.ShouldHaveSingleItem();
        keyColumns[0].Constraint.ShouldBe("pk_profiles");
        keyColumns[0].Column.ShouldBe("user_id");
    }

    [Fact]
    public async Task Profiles_HasRowLevelSecurityEnabled()
    {
        const string sql =
            """
            SELECT c.relrowsecurity
            FROM pg_catalog.pg_class AS c
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            WHERE n.nspname = 'access' AND c.relname = 'profiles';
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        var relrowsecurity = await command.ExecuteScalarAsync();

        // RLS is ENABLED by 0002 even though the SELECT policy that makes it non-empty is auth-coupled
        // (0003, not run here): enabling RLS is portable, the policy is not.
        relrowsecurity.ShouldBe(true);
    }

    [Fact]
    public async Task Profiles_Email_HasUniqueConstraint_UxProfilesEmail()
    {
        const string sql =
            """
            SELECT tc.constraint_name, kcu.column_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.key_column_usage AS kcu
              ON kcu.constraint_schema = tc.constraint_schema
             AND kcu.constraint_name = tc.constraint_name
            WHERE tc.table_schema = 'access'
              AND tc.table_name = 'profiles'
              AND tc.constraint_type = 'UNIQUE'
            ORDER BY kcu.ordinal_position;
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var uniqueColumns = new List<(string Constraint, string Column)>();
        while (await reader.ReadAsync())
            uniqueColumns.Add((reader.GetString(0), reader.GetString(1)));

        // Single UNIQUE constraint named per convention (ux_<table>__<cols>), on email (0002). It is a
        // LOCAL invariant — it stops two profile rows claiming the same e-mail identity. It does NOT
        // prevent drift from auth.users (nothing resyncs this snapshot column).
        uniqueColumns.ShouldHaveSingleItem();
        uniqueColumns[0].Constraint.ShouldBe("ux_profiles__email");
        uniqueColumns[0].Column.ShouldBe("email");
    }

    [Fact]
    public async Task Profiles_CreatedAt_HasNoDefault()
    {
        const string sql =
            """
            SELECT column_default
            FROM information_schema.columns
            WHERE table_schema = 'access' AND table_name = 'profiles' AND column_name = 'created_at';
            """;

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        var columnDefault = await command.ExecuteScalarAsync();

        // NO DB default: the .NET domain stamps created_at (ADR-ARCH-005), same as safety.constats, so
        // an omitted stamp fails loudly at INSERT instead of being masked by now() (0002). information_
        // schema returns SQL NULL (-> DBNull) when a column has no default.
        columnDefault.ShouldBe(DBNull.Value);
    }

    private async Task<Dictionary<string, ColumnSpec>> ReadColumnsAsync()
    {
        const string sql =
            """
            SELECT column_name, data_type, character_maximum_length, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'access' AND table_name = 'profiles';
            """;

        var columns = new Dictionary<string, ColumnSpec>(StringComparer.Ordinal);

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
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
}

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>A column: its table, name, store type and nullability, and whether the database generates its value.</summary>
internal sealed record StoreColumn(string Table, string Name, string Type, bool Nullable, bool Generated = false);

/// <summary>A primary or unique key: columns in key order, and the predicate of a partial unique index.</summary>
internal sealed record StoreKey(string Table, string Name, string Columns, string? Filter = null);

/// <summary>A foreign key: dependent columns and the principal table and columns, in key order.</summary>
internal sealed record StoreForeignKey(string Table, string Name, string Columns, string PrincipalTable, string PrincipalColumns);

/// <summary>
/// The tables, columns and keys of the <c>access</c> schema in one shape, read either from the
/// catalog of the migrated database or from the EF Core model, so that the two can be compared
/// (ADR-ARCH-033 §3).
/// </summary>
internal sealed record StoreSchema(
    IReadOnlyList<string> Tables,
    IReadOnlyList<StoreColumn> Columns,
    IReadOnlyList<StoreKey> PrimaryKeys,
    IReadOnlyList<StoreKey> UniqueKeys,
    IReadOnlyList<StoreForeignKey> ForeignKeys)
{
    public const string Schema = "access";

    /// <summary>
    /// The keys the database holds and the model leaves out, each with its reason
    /// (ADR-ARCH-033 §3, §5). No column is admitted.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Admitted = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fk_ownerships__organizations"] =
            "ownerships.organization_id is a plain column in the model, so that EF sees no cycle; the key is deferred to commit (ADR-ARCH-033 §5)",
        ["fk_workspace_accesses__workspaces"] =
            "workspace_accesses (workspace_id, organization_id) are plain columns in the model, so that EF sees no cycle; the key is deferred to commit (ADR-ARCH-033 §5)",
        ["ux_workspaces__id_organization_id"] =
            "the target of the access's composite key, which the model does not declare (ADR-ARCH-033 §5)",
    };

    /// <summary>Reads the <c>access</c> schema from the catalog; system columns (attnum &lt;= 0) are outside it.</summary>
    public static async Task<StoreSchema> ReadAsync(string connectionString)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var tables = await RowsAsync(connection, """
            select c.relname
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'access' and c.relkind in ('r', 'p')
            order by c.relname
            """, reader => reader.GetString(0));

        var columns = await RowsAsync(connection, """
            select c.relname, a.attname, format_type(a.atttypid, a.atttypmod), not a.attnotnull,
                   a.atthasdef or a.attidentity <> '' or a.attgenerated <> ''
            from pg_attribute a
            join pg_class c on c.oid = a.attrelid
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'access' and c.relkind in ('r', 'p') and a.attnum > 0 and not a.attisdropped
            order by c.relname, a.attnum
            """, reader => new StoreColumn(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4)));

        var constraints = await RowsAsync(connection, """
            select c.relname, k.conname, k.contype::text,
                   (select string_agg(a.attname, ',' order by u.ord)
                    from unnest(k.conkey) with ordinality u(attnum, ord)
                    join pg_attribute a on a.attrelid = k.conrelid and a.attnum = u.attnum),
                   r.relname,
                   (select string_agg(a.attname, ',' order by u.ord)
                    from unnest(k.confkey) with ordinality u(attnum, ord)
                    join pg_attribute a on a.attrelid = k.confrelid and a.attnum = u.attnum)
            from pg_constraint k
            join pg_class c on c.oid = k.conrelid
            join pg_namespace n on n.oid = c.relnamespace
            left join pg_class r on r.oid = k.confrelid
            where n.nspname = 'access' and k.contype in ('p', 'u', 'f')
            order by c.relname, k.conname
            """, reader => (
                Table: reader.GetString(0),
                Name: reader.GetString(1),
                Type: reader.GetString(2),
                Columns: reader.GetString(3),
                PrincipalTable: reader.IsDBNull(4) ? null : reader.GetString(4),
                PrincipalColumns: reader.IsDBNull(5) ? null : reader.GetString(5)));

        // Unique indexes that back no constraint: the partial unique indexes of the current
        // memberships and accesses (contract.md §5).
        var uniqueIndexes = await RowsAsync(connection, """
            select t.relname, i.relname,
                   (select string_agg(a.attname, ',' order by u.ord)
                    from unnest(x.indkey::int2[]) with ordinality u(attnum, ord)
                    join pg_attribute a on a.attrelid = x.indrelid and a.attnum = u.attnum),
                   pg_get_expr(x.indpred, x.indrelid)
            from pg_index x
            join pg_class i on i.oid = x.indexrelid
            join pg_class t on t.oid = x.indrelid
            join pg_namespace n on n.oid = t.relnamespace
            where n.nspname = 'access' and x.indisunique and not x.indisprimary
              and not exists (
                select 1 from pg_constraint k
                where k.conindid = x.indexrelid and k.contype in ('p', 'u', 'x'))
            order by t.relname, i.relname
            """, reader => new StoreKey(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));

        return new StoreSchema(
            tables,
            columns,
            [.. constraints.Where(k => k.Type == "p").Select(k => new StoreKey(k.Table, k.Name, k.Columns))],
            [.. constraints.Where(k => k.Type == "u").Select(k => new StoreKey(k.Table, k.Name, k.Columns)), .. uniqueIndexes],
            [.. constraints.Where(k => k.Type == "f").Select(k => new StoreForeignKey(k.Table, k.Name, k.Columns, k.PrincipalTable!, k.PrincipalColumns!))]);
    }

    /// <summary>What the EF Core model maps, in the same shape; also returns each entity's schema.</summary>
    public static (StoreSchema Mapped, IReadOnlyList<(string Entity, string? Schema)> Schemas) FromModel(IModel model)
    {
        var tables = new List<string>();
        var columns = new List<StoreColumn>();
        var primaryKeys = new List<StoreKey>();
        var uniqueKeys = new List<StoreKey>();
        var foreignKeys = new List<StoreForeignKey>();
        var schemas = new List<(string, string?)>();

        foreach (var entity in model.GetEntityTypes())
        {
            schemas.Add((entity.ClrType.Name, entity.GetSchema()));
            var name = entity.GetTableName() ?? throw new InvalidOperationException($"{entity.ClrType.Name} maps no table.");
            var table = StoreObjectIdentifier.Table(name, entity.GetSchema());
            tables.Add(name);

            string ColumnsOf(IEnumerable<IReadOnlyProperty> properties) =>
                string.Join(',', properties.Select(property => property.GetColumnName(table)));

            columns.AddRange(entity.GetProperties().Select(property => new StoreColumn(
                name,
                property.GetColumnName(table) ?? property.Name,
                property.GetColumnType(table),
                property.IsColumnNullable(table))));

            if (entity.FindPrimaryKey() is { } primaryKey)
            {
                primaryKeys.Add(new StoreKey(name, primaryKey.GetName(table) ?? string.Empty, ColumnsOf(primaryKey.Properties)));
            }

            uniqueKeys.AddRange(entity.GetKeys().Where(key => !key.IsPrimaryKey())
                .Select(key => new StoreKey(name, key.GetName(table) ?? string.Empty, ColumnsOf(key.Properties))));
            uniqueKeys.AddRange(entity.GetIndexes().Where(index => index.IsUnique)
                .Select(index => new StoreKey(name, index.GetDatabaseName(table) ?? string.Empty, ColumnsOf(index.Properties), index.GetFilter(table))));

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var principal = foreignKey.PrincipalEntityType;
                var principalTable = StoreObjectIdentifier.Table(principal.GetTableName()!, principal.GetSchema());
                foreignKeys.Add(new StoreForeignKey(
                    name,
                    foreignKey.GetConstraintName(table, principalTable) ?? string.Empty,
                    ColumnsOf(foreignKey.Properties),
                    principalTable.Name,
                    string.Join(',', foreignKey.PrincipalKey.Properties.Select(property => property.GetColumnName(principalTable)))));
            }
        }

        return (new StoreSchema(tables, columns, primaryKeys, uniqueKeys, foreignKeys), schemas);
    }

    /// <summary>
    /// Every difference between what <paramref name="mapped"/> maps and what <paramref name="database"/>
    /// holds, as ADR-ARCH-033 §3 compares them: tables, columns, store types, nullability and primary
    /// keys of every mapped table; unique and foreign keys in one direction, a key the database holds and
    /// the model leaves out being admitted only when <see cref="Admitted"/> names it.
    /// </summary>
    public static IReadOnlyList<string> Differences(StoreSchema mapped, StoreSchema database)
    {
        var differences = new List<string>();

        differences.AddRange(database.Tables.Except(mapped.Tables, StringComparer.Ordinal)
            .Select(table => $"table {table} is in the database and mapped by no entity"));

        foreach (var table in mapped.Tables)
        {
            if (!database.Tables.Contains(table, StringComparer.Ordinal))
            {
                differences.Add($"table {table} is mapped and absent from the database");
                continue;
            }

            foreach (var column in mapped.Columns.Where(column => column.Table == table))
            {
                var stored = database.Columns.SingleOrDefault(other => other.Table == table && other.Name == column.Name);
                if (stored is null)
                {
                    differences.Add($"column {table}.{column.Name} is mapped and absent from the database");
                }
                else if (stored.Type != column.Type || stored.Nullable != column.Nullable)
                {
                    differences.Add($"column {table}.{column.Name} is mapped as {Describe(column)} and stored as {Describe(stored)}");
                }
            }

            differences.AddRange(database.Columns
                .Where(column => column.Table == table && !mapped.Columns.Any(other => other.Table == table && other.Name == column.Name))
                .Select(column => $"column {table}.{column.Name} ({Describe(column)}) is in the database and not mapped"));

            var primaryKey = mapped.PrimaryKeys.SingleOrDefault(key => key.Table == table);
            var storedPrimaryKey = database.PrimaryKeys.SingleOrDefault(key => key.Table == table);
            if (primaryKey != storedPrimaryKey)
            {
                differences.Add($"the primary key of {table} is mapped as {primaryKey} and stored as {storedPrimaryKey}");
            }

            Compare(table, mapped.UniqueKeys, database.UniqueKeys, Same, "unique key", differences);
            Compare(table, mapped.ForeignKeys, database.ForeignKeys, (left, right) => left == right, "foreign key", differences);
        }

        differences.AddRange(Admitted.Keys
            .Where(name => !database.UniqueKeys.Any(key => key.Name == name) && !database.ForeignKeys.Any(key => key.Name == name))
            .Select(name => $"{name} is admitted and absent from the database: remove it from the list"));

        // An admitted key is one the model leaves out: mapping it again, such as the relation from the
        // ownership to its organization, brings back the cycle ADR-ARCH-033 §5 removes.
        differences.AddRange(Admitted.Keys
            .Where(name => mapped.UniqueKeys.Any(key => key.Name == name) || mapped.ForeignKeys.Any(key => key.Name == name))
            .Select(name => $"{name} is admitted as left out of the model, and the model maps it"));

        return differences;
    }

    private static void Compare<T>(
        string table, IReadOnlyList<T> mapped, IReadOnlyList<T> database, Func<T, T, bool> same, string kind, List<string> differences)
        where T : class
    {
        static (string Table, string Name) Of(T key) => key switch
        {
            StoreKey k => (k.Table, k.Name),
            StoreForeignKey f => (f.Table, f.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };

        foreach (var key in mapped.Where(key => Of(key).Table == table))
        {
            var stored = database.SingleOrDefault(other => Of(other) == Of(key));
            if (stored is null)
            {
                differences.Add($"{kind} {Of(key).Name} is mapped as {key} and absent from the database");
            }
            else if (!same(key, stored))
            {
                differences.Add($"{kind} {Of(key).Name} is mapped as {key} and stored as {stored}");
            }
        }

        differences.AddRange(database
            .Where(key => Of(key).Table == table
                && !mapped.Any(other => Of(other) == Of(key))
                && !Admitted.ContainsKey(Of(key).Name))
            .Select(key => $"{kind} {Of(key).Name} ({key}) is in the database, not mapped, and not admitted"));
    }

    private static bool Same(StoreKey mapped, StoreKey stored) =>
        mapped.Table == stored.Table
        && mapped.Name == stored.Name
        && mapped.Columns == stored.Columns
        && Normalize(mapped.Filter) == Normalize(stored.Filter);

    // pg_get_expr returns "(ended_at IS NULL)" for the model's "ended_at is null".
    private static string? Normalize(string? filter)
    {
        if (filter is null)
        {
            return null;
        }

        var normalized = Regex.Replace(filter, @"\s+", string.Empty).ToLowerInvariant();
        while (normalized.Length >= 2 && normalized[0] == '(' && normalized[^1] == ')')
        {
            normalized = normalized[1..^1];
        }

        return normalized;
    }

    private static string Describe(StoreColumn column) => $"{column.Type} {(column.Nullable ? "null" : "not null")}";

    private static async Task<List<T>> RowsAsync<T>(NpgsqlConnection connection, string sql, Func<NpgsqlDataReader, T> read)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }
}

using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SaasBtp.Access.Adapters.Persistence;

/// <summary>
/// The mapping every Access table shares, which each aggregate's configuration applies: the
/// schema, the lengths, the filter of current relations, and the key and audit columns of an
/// aggregate root. Names are those of <c>0001__access__create_tables.sql</c>.
/// </summary>
internal static class AccessTable
{
    /// <summary>The Access module's one schema (ADR-ARCH-033 §3, ADR-ARCH-034 §3).</summary>
    public const string Schema = "access";

    /// <summary>The most Unicode scalar values a name holds (BP001-R7), and so a <c>varchar</c> counts.</summary>
    public const int NameLength = 200;

    /// <summary>The longest status token, <c>declared</c> or <c>verified</c>.</summary>
    public const int StatusLength = 8;

    /// <summary>The filter of the unique keys of current relations (contract.md §5).</summary>
    public const string Current = "ended_at is null";

    /// <summary>
    /// What every Access table maps for its aggregate root: the table, the key, set by the API and
    /// never generated, the four audit columns and the aggregate version (<c>sql.md</c>).
    /// </summary>
    public static void Root<TRoot>(EntityTypeBuilder<TRoot> root, string table, string primaryKey)
        where TRoot : class
    {
        root.ToTable(table, Schema);
        root.HasKey("Id").HasName(primaryKey);
        root.Property("Id").HasColumnName("id").ValueGeneratedNever();
        root.Property("CreatedAt").HasColumnName("created_at");
        root.Property("CreatedBy").HasColumnName("created_by");
        root.Property("UpdatedAt").HasColumnName("updated_at");
        root.Property("UpdatedBy").HasColumnName("updated_by");
        root.Property<long>(AggregateVersion.PropertyName).HasColumnName("aggregate_version");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SaasBtp.Safety.Domain.Constats;
using SaasBtp.Safety.Infrastructure.Persistence;

namespace SaasBtp.Safety.IntegrationTests;

/// <summary>
/// Offline EF model checks (no Docker): accessing <see cref="DbContext.Model"/> under the Npgsql
/// provider forces relational model validation, catching mapping misconfiguration (missing/duplicate
/// columns, bad owned-type flattening, missing converters) without a database. The behavioural
/// round-trip against the DbUp schema lives in <see cref="ConstatMappingTests"/> (Docker).
/// </summary>
public sealed class SafetyModelTests
{
    private static readonly string[] ExpectedColumns =
    [
        "id", "tenant_id", "site_id", "type", "severity",
        "observer_user_id", "observer_full_name", "observer_function",
        "occurred_at", "location_description",
        "short_description", "detailed_description", "observations",
        "created_at",
    ];

    private static SafetyDbContext BuildContext() =>
        // A dummy connection string is fine: model building never opens a connection.
        new(new DbContextOptionsBuilder<SafetyDbContext>()
            .UseNpgsql("Host=localhost;Database=none;Username=none;Password=none")
            .Options);

    [Fact]
    public void Model_MapsConstat_ToSafetyConstats_WithExpectedColumns()
    {
        using var context = BuildContext();

        var entity = context.Model.FindEntityType(typeof(Constat));
        entity.ShouldNotBeNull();
        entity!.GetSchema().ShouldBe("safety");
        entity.GetTableName().ShouldBe("constats");

        var table = StoreObjectIdentifier.Table("constats", "safety");

        var columns = entity.GetProperties()
            .Select(property => property.GetColumnName(table))
            .Concat(entity.GetNavigations()
                .Where(navigation => navigation.TargetEntityType.IsOwned())
                .SelectMany(navigation => navigation.TargetEntityType.GetProperties()
                    .Select(property => property.GetColumnName(table))))
            .Where(column => column is not null)
            .ToHashSet(StringComparer.Ordinal);

        columns.ShouldBe(ExpectedColumns, ignoreOrder: true);
    }

    [Fact]
    public void Model_UsesExplicitValueConverters_ForClosedSetsAndIds()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(Constat))!;

        // Ids and closed sets are persisted through explicit converters, never implicit ToString.
        entity.FindProperty(nameof(Constat.Id))!.GetValueConverter().ShouldNotBeNull();
        entity.FindProperty(nameof(Constat.TenantId))!.GetValueConverter().ShouldNotBeNull();
        entity.FindProperty(nameof(Constat.SiteId))!.GetValueConverter().ShouldNotBeNull();
        entity.FindProperty(nameof(Constat.Type))!.GetValueConverter().ShouldNotBeNull();
        entity.FindProperty(nameof(Constat.Severity))!.GetValueConverter().ShouldNotBeNull();
    }

    [Fact]
    public void Model_TreatsOwnedValueObjects_AsRequiredAndTableShared()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(Constat))!;

        foreach (var name in new[] { nameof(Constat.Observer), nameof(Constat.Location), nameof(Constat.Observation) })
        {
            var navigation = entity.FindNavigation(name);
            navigation.ShouldNotBeNull();
            navigation!.TargetEntityType.IsOwned().ShouldBeTrue();
            navigation.ForeignKey.IsRequired.ShouldBeTrue();
            // Owned into the same table (flattened, no child table).
            navigation.TargetEntityType.GetTableName().ShouldBe("constats");
        }
    }
}

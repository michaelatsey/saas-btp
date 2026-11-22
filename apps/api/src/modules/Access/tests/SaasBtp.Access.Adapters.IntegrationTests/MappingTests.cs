using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Access.Adapters.Persistence;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The Access context's mapping against the schema the runner migrated (ADR-ARCH-033 §3), and the
/// module's own schema (ADR-ARCH-034 §7).
/// </summary>
public sealed class MappingTests(AccessDatabase database)
{
    [Fact]
    public async Task MappingMatchesMigratedSchema()
    {
        await using var module = ModuleServices.Start(database.Api);
        await using var scope = module.Scope();
        var (mapped, schemas) = StoreSchema.FromModel(scope.ServiceProvider.GetRequiredService<AccessDbContext>().Model);
        var stored = await StoreSchema.ReadAsync(database.Superuser);

        // ADR-ARCH-034 §7: a module's context maps only its own schema.
        Assert.All(schemas, entity => Assert.Equal(StoreSchema.Schema, entity.Schema));
        Assert.Equal(stored.Tables.Order(StringComparer.Ordinal), mapped.Tables.Order(StringComparer.Ordinal));
        var differences = StoreSchema.Differences(mapped, stored);
        Assert.True(differences.Count == 0, "The mapping differs from the migrated schema:" + Environment.NewLine + string.Join(Environment.NewLine, differences));
    }

    /// <summary>
    /// The comparison itself reports a mapped column that differs from the database: the catalog it
    /// reads is given one column with another store type.
    /// </summary>
    [Fact]
    public async Task MappingMatchesMigratedSchema_ReportsAMappedColumnThatDiffers()
    {
        await using var module = ModuleServices.Start(database.Api);
        await using var scope = module.Scope();
        var (mapped, _) = StoreSchema.FromModel(scope.ServiceProvider.GetRequiredService<AccessDbContext>().Model);
        var stored = await StoreSchema.ReadAsync(database.Superuser);
        var name = stored.Columns.Single(column => column.Table == "organizations" && column.Name == "name");
        var altered = stored with
        {
            Columns = [.. stored.Columns.Select(column => column == name ? column with { Type = "character varying(100)" } : column)],
        };

        var difference = Assert.Single(StoreSchema.Differences(mapped, altered));
        Assert.Equal(
            "column organizations.name is mapped as character varying(200) not null and stored as character varying(100) not null",
            difference);
    }

    /// <summary>
    /// The comparison reports an admitted key that the model maps again: here the relation from the
    /// ownership to its organization, which ADR-ARCH-033 §5 keeps out of the model so that EF sees no
    /// cycle.
    /// </summary>
    [Fact]
    public async Task MappingMatchesMigratedSchema_ReportsAnAdmittedKeyTheModelMaps()
    {
        await using var module = ModuleServices.Start(database.Api);
        await using var scope = module.Scope();
        var (mapped, _) = StoreSchema.FromModel(scope.ServiceProvider.GetRequiredService<AccessDbContext>().Model);
        var stored = await StoreSchema.ReadAsync(database.Superuser);
        var cycle = stored.ForeignKeys.Single(key => key.Name == "fk_ownerships__organizations");
        var remapped = mapped with { ForeignKeys = [.. mapped.ForeignKeys, cycle] };

        var difference = Assert.Single(StoreSchema.Differences(remapped, stored));
        Assert.Equal("fk_ownerships__organizations is admitted as left out of the model, and the model maps it", difference);
    }
}

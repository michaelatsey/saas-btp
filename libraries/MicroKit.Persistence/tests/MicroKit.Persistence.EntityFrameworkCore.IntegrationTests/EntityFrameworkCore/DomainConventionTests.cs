using System.Reflection;
using System.Reflection.Emit;
using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.PostgreSql;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #196 D13: <see cref="DomainConventionExtensions"/> maps MicroKit.Domain's identifiers and
/// <see cref="Actor"/> without hand-written converters. Run on SQLite and PostgreSQL.
/// </summary>
internal static class DomainConventionScenarios
{
    public static async Task EntityIds_GuidIds_AndActors_RoundTrip_ThroughTheConventions(Func<Task<ShipmentStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        var shipment = Shipment.Create(new Actor("courier-7"));
        shipment.Parcels.Add(new Parcel(new ParcelId(Guid.CreateVersion7()), Actor.System));

        await using (var context = store.CreateContext())
        {
            context.Add(shipment);
            await context.SaveChangesAsync();
        }

        await using var read = store.CreateContext();
        var loaded = await read.Shipments.Include(s => s.Parcels).SingleAsync(s => s.Id == shipment.Id);

        Assert.Equal(shipment.Id, loaded.Id);
        Assert.Equal(shipment.Reference, loaded.Reference);
        Assert.Equal(new Actor("courier-7"), loaded.Courier);
        Assert.Same(Actor.System, Assert.Single(loaded.Parcels).HandledBy);
    }

    public static async Task ActorColumns_AreLimitedTo_ActorMaxLength(Func<Task<ShipmentStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        await using var context = store.CreateContext();

        Assert.Equal(Actor.MaxLength, context.Model.FindEntityType(typeof(Shipment))!.FindProperty(nameof(Shipment.Courier))!
            .GetMaxLength());
    }

    public static async Task ConvertedIdentifierKeys_AreNeverStoreGenerated(Func<Task<ShipmentStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        await using var context = store.CreateContext();

        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, context.Model.FindEntityType(typeof(Shipment))!.FindProperty(nameof(Shipment.Id))!
            .ValueGenerated);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, context.Model.FindEntityType(typeof(Parcel))!.FindProperty(nameof(Parcel.Id))!
            .ValueGenerated);
    }

    public static async Task ANewChildWithAnAssignedId_AddedToATrackedAggregate_IsInserted(Func<Task<ShipmentStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        var shipment = Shipment.Create(new Actor("courier-7"));
        await using (var seed = store.CreateContext())
        {
            seed.Add(shipment);
            await seed.SaveChangesAsync();
        }

        await using (var context = store.CreateContext())
        {
            var tracked = await new EfRepository<Shipment, ShipmentId, ShipmentDbContext>(context).FindAsync(shipment.Id);
            tracked!.Parcels.Add(new Parcel(new ParcelId(Guid.CreateVersion7()), new Actor("loader-1")));

            // A domain-assigned key must be read as "new", not as an existing row to update.
            Assert.Null(await Record.ExceptionAsync(() => context.SaveChangesAsync()));
        }

        await using var verify = store.CreateContext();
        Assert.Equal(1, (await verify.Set<Parcel>().CountAsync()));
    }

    public static void AnEntityIdWithoutAGuidConstructor_FailsModelBuilding_NamingTheType()
    {
        var assembly = CompileInvalidIdentifierAssembly();

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            ModelConfigurationBuilderProbe.Configure(b => b.AddEntityIdConventions(assembly)));

        Assert.Contains("Generated.TextOnlyId", thrown.Message);
        Assert.Contains("Guid", thrown.Message);
    }

    // An EntityId whose only constructor takes a string, the type MicroKit compiled with Roslyn:
    //   public sealed record TextOnlyId : EntityId<TextOnlyId> { public TextOnlyId(string value) : base(Guid.Parse(value)) { } }
    // Emitted apart, in a collectible dynamic assembly, so that no scan of this test assembly ever
    // meets it. Rewritten here with System.Reflection.Emit (ADR-ARCH-035 §5: Roslyn stays private
    // to the analyzer).
    private static AssemblyBuilder CompileInvalidIdentifierAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Generated.InvalidIdentifiers"), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Generated.InvalidIdentifiers");
        var type = module.DefineType("Generated.TextOnlyId", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        var baseType = typeof(EntityId<>).MakeGenericType(type);
        type.SetParent(baseType);

        var baseConstructor = TypeBuilder.GetConstructor(
            baseType,
            typeof(EntityId<>).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, [typeof(Guid)])!);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Guid).GetMethod(nameof(Guid.Parse), [typeof(string)])!);
        il.Emit(OpCodes.Call, baseConstructor);
        il.Emit(OpCodes.Ret);

        // EntityId<T> is an abstract record: its abstract members, the compiler's <Clone>$ among
        // them, are overridden so that the type loads. None is ever called.
        foreach (var member in typeof(EntityId<>).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.IsAbstract))
        {
            var returnType = member.ReturnType.ContainsGenericParameters ? baseType : member.ReturnType;
            var parameterTypes = member.GetParameters().Select(p => p.ParameterType).ToArray();
            if (parameterTypes.Any(p => p.ContainsGenericParameters))
                throw new InvalidOperationException($"EntityId<T>.{member.Name} takes a generic parameter: extend this emitter.");
            var implementation = type.DefineMethod(member.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, returnType, parameterTypes);
            var body = implementation.GetILGenerator();
            body.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
            body.Emit(OpCodes.Throw);
            type.DefineMethodOverride(implementation, TypeBuilder.GetMethod(baseType, member));
        }

        _ = type.CreateType();
        return assembly;
    }

    // ConfigureConventions runs during model building; this drives the builder directly.
    private static class ModelConfigurationBuilderProbe
    {
        public static void Configure(Action<ModelConfigurationBuilder> configure)
        {
            var options = new DbContextOptionsBuilder<ProbeContext>()
                .UseSqlite("DataSource=:memory:")
                .Options;
            using var context = new ProbeContext(options, configure);
            _ = context.Model; // builds the model, running ConfigureConventions
        }

        private sealed class ProbeContext(DbContextOptions<ProbeContext> options, Action<ModelConfigurationBuilder> configure)
            : DbContext(options)
        {
            protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
                configure(configurationBuilder);
        }
    }
}

public sealed class DomainConventionSqliteTests
{
    [Fact]
    public Task EntityIds_GuidIds_AndActors_RoundTrip_ThroughTheConventions() => DomainConventionScenarios.EntityIds_GuidIds_AndActors_RoundTrip_ThroughTheConventions(CreateStoreAsync);

    [Fact]
    public Task ActorColumns_AreLimitedTo_ActorMaxLength() => DomainConventionScenarios.ActorColumns_AreLimitedTo_ActorMaxLength(CreateStoreAsync);

    [Fact]
    public Task ConvertedIdentifierKeys_AreNeverStoreGenerated() => DomainConventionScenarios.ConvertedIdentifierKeys_AreNeverStoreGenerated(CreateStoreAsync);

    [Fact]
    public Task ANewChildWithAnAssignedId_AddedToATrackedAggregate_IsInserted() => DomainConventionScenarios.ANewChildWithAnAssignedId_AddedToATrackedAggregate_IsInserted(CreateStoreAsync);

    [Fact]
    public void AnEntityIdWithoutAGuidConstructor_FailsModelBuilding_NamingTheType() => DomainConventionScenarios.AnEntityIdWithoutAGuidConstructor_FailsModelBuilding_NamingTheType();

    private static async Task<ShipmentStore> CreateStoreAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        return await ShipmentStore.CreateAsync(o => o.UseSqlite(connection), connection.DisposeAsync);
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class DomainConventionPostgreSqlTests(PostgreSqlSuiteFixture fixture)
{
    [Fact]
    public Task EntityIds_GuidIds_AndActors_RoundTrip_ThroughTheConventions() => DomainConventionScenarios.EntityIds_GuidIds_AndActors_RoundTrip_ThroughTheConventions(CreateStoreAsync);

    [Fact]
    public Task ActorColumns_AreLimitedTo_ActorMaxLength() => DomainConventionScenarios.ActorColumns_AreLimitedTo_ActorMaxLength(CreateStoreAsync);

    [Fact]
    public Task ConvertedIdentifierKeys_AreNeverStoreGenerated() => DomainConventionScenarios.ConvertedIdentifierKeys_AreNeverStoreGenerated(CreateStoreAsync);

    [Fact]
    public Task ANewChildWithAnAssignedId_AddedToATrackedAggregate_IsInserted() => DomainConventionScenarios.ANewChildWithAnAssignedId_AddedToATrackedAggregate_IsInserted(CreateStoreAsync);

    private async Task<ShipmentStore> CreateStoreAsync()
    {
        var connectionString = fixture.GetConnectionString("conventions_" + Guid.NewGuid().ToString("N"));

        ShipmentStore? store = null;
        store = await ShipmentStore.CreateAsync(o => o.UseNpgsql(connectionString), async () =>
        {
            await using var context = store!.CreateContext();
            await context.Database.EnsureDeletedAsync();
        });
        return store;
    }
}

public sealed class ShipmentStore : IAsyncDisposable
{
    private readonly Action<DbContextOptionsBuilder> _configure;
    private readonly Func<ValueTask> _cleanup;

    private ShipmentStore(Action<DbContextOptionsBuilder> configure, Func<ValueTask> cleanup)
    {
        _configure = configure;
        _cleanup = cleanup;
    }

    public static async Task<ShipmentStore> CreateAsync(Action<DbContextOptionsBuilder> configure, Func<ValueTask> cleanup)
    {
        var store = new ShipmentStore(configure, cleanup);
        await using var context = store.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return store;
    }

    public ShipmentDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ShipmentDbContext>();
        _configure(builder);
        builder.UseMicroKitPersistence();
        return new ShipmentDbContext(builder.Options);
    }

    public ValueTask DisposeAsync() => _cleanup();
}

/// <summary>An identifier whose (Guid) constructor is private: the convention must still build it.</summary>
public sealed record ShipmentId : EntityId<ShipmentId>
{
    private ShipmentId(Guid value) : base(value) { }

    public static ShipmentId New() => new(Guid.CreateVersion7());
}

public sealed record ParcelId : EntityId<ParcelId>
{
    public ParcelId(Guid value) : base(value) { }
}

public sealed class Shipment : IAggregateRoot
{
    private Shipment(ShipmentId id, GuidId reference, Actor courier)
    {
        Id = id;
        Reference = reference;
        Courier = courier;
    }

    public ShipmentId Id { get; private init; }

    public GuidId Reference { get; private init; }

    public Actor Courier { get; private set; }

    public List<Parcel> Parcels { get; } = [];

    public static Shipment Create(Actor courier) => new(ShipmentId.New(), GuidId.New(), courier);
}

public sealed class Parcel(ParcelId id, Actor handledBy)
{
    public ParcelId Id { get; private init; } = id;

    public Actor HandledBy { get; private set; } = handledBy;
}

public sealed class ShipmentDbContext(DbContextOptions<ShipmentDbContext> options) : DbContext(options)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddDomainConventions(typeof(ShipmentId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Shipment>().HasMany(s => s.Parcels).WithOne().HasForeignKey("ShipmentId");
}

using Microsoft.EntityFrameworkCore;
using SaasBtp.Database.Migrator;
using SaasBtp.Safety.Domain.Constats;
using SaasBtp.Safety.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SaasBtp.Safety.IntegrationTests;

/// <summary>
/// Anti-drift guard (ADR-ARCH-006): the EF <see cref="SafetyDbContext"/> mapping is exercised
/// against a schema migrated by DbUp on an ephemeral PostgreSQL — NEVER EnsureCreated, never an EF
/// snapshot/migration. A full Constat is round-tripped (every column, both token converters, owned
/// types, timestamptz UTC), and UUIDv7 ordering through the Npgsql Guid-&gt;uuid mapping is verified.
/// A fresh, freshly-migrated container per test keeps the assertions isolated. Requires Docker.
/// </summary>
public sealed class ConstatMappingTests : IAsyncLifetime
{
    // Image is passed to the builder constructor (Testcontainers 4.13 deprecated the parameterless
    // ctor + WithImage). Tag is the shared, env-overridable PostgresTestImage (default Postgres 17,
    // matching the target Supabase project).
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresTestImage.Name)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Reuse the migrator's own runner + embedded scripts: the schema asserted against is
        // byte-for-byte the schema deployment applies (single DDL source). Apply only the PORTABLE
        // subset — the auth-coupled scripts (0003-0005) reference auth.users / supabase_auth_admin,
        // absent from this bare Postgres (conventions/sql.md §Auth-coupled scripts).
        var result = MigrationRunner.Run(_postgres.GetConnectionString(), MigrationRunner.IsPortableScript);
        result.Successful.ShouldBeTrue(result.Error?.ToString());
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private SafetyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SafetyDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    private static Constat SampleConstat(ConstatId id) =>
        Constat.Create(
            id,
            new TenantId(Guid.CreateVersion7()),
            new SiteId(Guid.CreateVersion7()),
            ConstatType.Incident,
            Severity.Major,
            new ObserverSnapshot(Guid.CreateVersion7(), "Jeanne Martin", "Chef de chantier"),
            new DateTime(2026, 7, 9, 8, 30, 0, DateTimeKind.Utc),
            new Location("Niveau 2"),
            new Observation("Garde-corps manquant", "Détail complet", "Notes"),
            new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc)).Value;

    [Fact]
    public async Task Constat_RoundTrips_AllColumns_ThroughDbUpMigratedSchema()
    {
        var id = new ConstatId(Guid.CreateVersion7());
        var tenantId = new TenantId(Guid.CreateVersion7());
        var siteId = new SiteId(Guid.CreateVersion7());
        var observer = new ObserverSnapshot(Guid.CreateVersion7(), "Jeanne Martin", "Chef de chantier");
        var location = new Location("Niveau 2, aile est");
        var observation = new Observation("Garde-corps manquant", "Détail complet", "Notes diverses");
        var occurredAt = new DateTime(2026, 7, 9, 8, 30, 0, DateTimeKind.Utc);
        var createdAt = new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc);

        var constat = Constat.Create(id, tenantId, siteId, ConstatType.DangerousSituation, Severity.Critical,
            observer, occurredAt, location, observation, createdAt).Value;

        await using (var ctx = NewContext())
        {
            ctx.Constats.Add(constat);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var reloaded = await ctx.Constats.SingleAsync(constat => constat.Id == id);

            reloaded.Id.ShouldBe(id);
            reloaded.TenantId.ShouldBe(tenantId);
            reloaded.SiteId.ShouldBe(siteId);
            reloaded.Type.ShouldBe(ConstatType.DangerousSituation);   // token converter round-trip
            reloaded.Severity.ShouldBe(Severity.Critical);            // token converter round-trip
            reloaded.Observer.ShouldBe(observer);                     // owned type flattened + reloaded
            reloaded.Location.ShouldBe(location);
            reloaded.Observation.ShouldBe(observation);
            reloaded.OccurredAt.ShouldBe(occurredAt);
            reloaded.OccurredAt.Kind.ShouldBe(DateTimeKind.Utc);      // timestamptz -> Kind.Utc
            reloaded.CreatedAt.ShouldBe(createdAt);
            reloaded.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        }
    }

    [Fact]
    public async Task Constat_WithOptionalFieldsOmitted_RoundTrips()
    {
        var id = new ConstatId(Guid.CreateVersion7());
        var observer = new ObserverSnapshot(Guid.CreateVersion7(), "Sans fonction", null);
        var location = new Location(string.Empty);                    // empty tolerated
        var observation = new Observation("Constat minimal", null, null);

        var constat = Constat.Create(id, new TenantId(Guid.CreateVersion7()), new SiteId(Guid.CreateVersion7()),
            ConstatType.Incident, Severity.Minor, observer,
            new DateTime(2026, 7, 9, 8, 0, 0, DateTimeKind.Utc), location, observation,
            new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc)).Value;

        await using (var ctx = NewContext())
        {
            ctx.Constats.Add(constat);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var reloaded = await ctx.Constats.SingleAsync(constat => constat.Id == id);

            reloaded.Observer.Function.ShouldBeNull();                // nullable observer_function
            reloaded.Location.Description.ShouldBe(string.Empty);     // NOT NULL, empty tolerated
            reloaded.Observation.DetailedDescription.ShouldBeNull();  // nullable text
            reloaded.Observation.Observations.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Uuidv7Ids_PreserveTimeOrder_ThroughNpgsqlUuidMapping()
    {
        // Strictly increasing timestamps => deterministically time-ordered UUIDv7s, independent of
        // the intra-ms counter .NET v7 lacks (safety-domain-model.md §6).
        var baseTime = new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(0, 25)
            .Select(i => new ConstatId(Guid.CreateVersion7(baseTime.AddMilliseconds(i))))
            .ToList();

        await using (var ctx = NewContext())
        {
            foreach (var id in ids)
                ctx.Constats.Add(SampleConstat(id));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var dbOrder = await ctx.Constats.OrderBy(constat => constat.Id).Select(constat => constat.Id).ToListAsync();

            // Postgres ORDER BY the uuid column matches the UUIDv7 creation/time order: the Npgsql
            // Guid->uuid mapping preserves ordering (the endianness quirk does not scramble it).
            dbOrder.ShouldBe(ids);
        }
    }
}

using MicroKit.Domain.Aggregates;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// <see cref="EfReadRepository{TAggregate,TContext}.FindAsync"/> loads by primary key without
/// tracking. #196 D2's "a tracked instance is returned as is" is reversed by #197 phase 1: the read
/// side never consults the tracker (<c>AggregateBoundaryScenarios.ReadFindAsync_*</c> and
/// <c>ReadListAsync_*</c> pin that, on PostgreSQL and SQL Server).
/// </summary>
public sealed class EfReadRepositoryFindTests
{
    [Fact]
    public async Task FindAsync_WhenAggregateIsNotTracked_LoadsItDetached()
    {
        await using var h = await Harness.CreateAsync();
        var id = await SeedAsync(h, "stored");

        var found = await h.Sut.FindAsync([id], TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal("stored", found.Name);
        Assert.Equal(EntityState.Detached, h.Context.Entry(found).State);
    }

    [Fact]
    public async Task FindAsync_WhenNotTracked_DoesNotWireTheLoadedAggregateIntoTrackedEntities()
    {
        await using var h = await Harness.CreateAsync();
        var id = await SeedAsync(h, "referenced");
        // A tracked entity of the current command references the aggregate by foreign key only.
        var note = new ReadModelNote { AggregateId = id };
        h.Context.Add(note);
        await h.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await h.Sut.FindAsync([id], TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Null(note.Aggregate);
        Assert.Equal(EntityState.Unchanged, h.Context.Entry(note).State);
        // A later commit must not try to insert the aggregate the read returned.
        Assert.Null(await Record.ExceptionAsync(() => h.Context.SaveChangesAsync(TestContext.Current.CancellationToken)));
        Assert.Empty(h.Context.ChangeTracker.Entries<ReadModelAggregate>());
    }

    [Fact]
    public async Task FindAsync_WhenNotTracked_QueriesTheKeyAsAParameter()
    {
        await using var h = await Harness.CreateAsync();
        var id = await SeedAsync(h, "stored");
        var commands = new List<string>();
        await using var logged = new ReadModelDbContext(new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseSqlite(h.Connection)
            .LogTo(commands.Add, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuting])
            .Options);
        var sut = new EfReadRepository<ReadModelAggregate, ReadModelDbContext>(
            logged, new EfSpecificationEvaluator(), PaginationLimits.Default);

        Assert.NotNull((await sut.FindAsync([id], TestContext.Current.CancellationToken)));

        var sql = Assert.Single(commands);
        Assert.DoesNotContain(id.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@", sql);
    }

    [Fact]
    public async Task FindAsync_WithTheWrongNumberOfKeyValues_Throws()
    {
        await using var h = await Harness.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(async () => await h.Sut.FindAsync([Guid.NewGuid(), Guid.NewGuid()], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindAsync_WithAKeyValueOfTheWrongType_ThrowsArgumentException()
    {
        await using var h = await Harness.CreateAsync();
        await SeedAsync(h, "stored");

        await Assert.ThrowsAsync<ArgumentException>(async () => await h.Sut.FindAsync([42], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindAsync_OnAConvertedKey_WithTheProviderValueInsteadOfTheKeyType_ThrowsArgumentException()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<PlainKeyDbContext>().UseSqlite(connection).Options;
        await using var context = new PlainKeyDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var sut = new EfReadRepository<PlainKeyAggregate, PlainKeyDbContext>(
            context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        // The key's CLR type is PlainKey; its column holds a Guid. Passing the Guid is a caller error.
        await Assert.ThrowsAsync<ArgumentException>(async () => await sut.FindAsync([Guid.NewGuid()], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindAsync_WithAStructKeyThatDefinesNoEqualityOperator_FindsTheAggregate()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<PlainKeyDbContext>().UseSqlite(connection).Options;
        var key = new PlainKey(Guid.NewGuid());
        await using (var seed = new PlainKeyDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            seed.Aggregates.Add(new PlainKeyAggregate { Id = key, Name = "stored" });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = new PlainKeyDbContext(options);
        var sut = new EfReadRepository<PlainKeyAggregate, PlainKeyDbContext>(
            context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        var found = await sut.FindAsync([key], TestContext.Current.CancellationToken);

        Assert.Equal("stored", Present.NotNull(found).Name);
    }

    [Fact]
    public async Task FindAsync_WhenMissing_ReturnsNull()
    {
        await using var h = await Harness.CreateAsync();
        Assert.Null((await h.Sut.FindAsync([Guid.NewGuid()], TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task FindAsync_DoesNotRunAFullDetectChanges()
    {
        await using var h = await Harness.CreateAsync();
        var trackedId = await SeedAsync(h, "tracked");
        await h.Context.Aggregates.SingleAsync(a => a.Id == trackedId, cancellationToken: TestContext.Current.CancellationToken);
        var untrackedId = await SeedAsync(h, "untracked");
        h.Context.Entry(await h.Context.Aggregates.SingleAsync(a => a.Id == untrackedId, cancellationToken: TestContext.Current.CancellationToken)).State = EntityState.Detached;

        var fullDetections = 0;
        h.Context.ChangeTracker.DetectingAllChanges += (_, _) => fullDetections++;

        await h.Sut.FindAsync([trackedId], TestContext.Current.CancellationToken);
        await h.Sut.FindAsync([untrackedId], TestContext.Current.CancellationToken);

        Assert.Equal(0, fullDetections);
        Assert.True(h.Context.ChangeTracker.AutoDetectChangesEnabled, "the caller's setting is restored");
    }

    private static async Task<Guid> SeedAsync(Harness h, string name)
    {
        var aggregate = new ReadModelAggregate { Name = name };
        h.Context.Add(aggregate);
        await h.Context.SaveChangesAsync();
        h.Context.Entry(aggregate).State = EntityState.Detached;
        return aggregate.Id;
    }

    private static async Task<string?> ReadNameFromDatabaseAsync(Harness h, Guid id)
    {
        await using var verify = new ReadModelDbContext(
            new DbContextOptionsBuilder<ReadModelDbContext>().UseSqlite(h.Connection).Options);
        return await verify.Aggregates.AsNoTracking().Where(a => a.Id == id).Select(a => a.Name).SingleOrDefaultAsync();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(SqliteConnection connection, ReadModelDbContext context)
        {
            Connection = connection;
            Context = context;
            Sut = new EfReadRepository<ReadModelAggregate, ReadModelDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);
        }

        public SqliteConnection Connection { get; }

        public ReadModelDbContext Context { get; }

        public EfReadRepository<ReadModelAggregate, ReadModelDbContext> Sut { get; }

        // A SQLite in-memory database lives exactly as long as its connection.
        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var context = new ReadModelDbContext(
                new DbContextOptionsBuilder<ReadModelDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Harness(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}

public sealed class ReadModelAggregate : IAggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>A tracked entity that references a <see cref="ReadModelAggregate"/> by foreign key.</summary>
public sealed class ReadModelNote
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid AggregateId { get; init; }

    public ReadModelAggregate? Aggregate { get; set; }
}

public sealed class ReadModelDbContext(DbContextOptions<ReadModelDbContext> options) : DbContext(options)
{
    public DbSet<ReadModelAggregate> Aggregates => Set<ReadModelAggregate>();

    public DbSet<ReadModelNote> Notes => Set<ReadModelNote>();
}

/// <summary>A strongly-typed key declared as a plain struct: no <c>==</c> operator, value equality only.</summary>
public readonly struct PlainKey(Guid value)
{
    public Guid Value { get; } = value;
}

public sealed class PlainKeyAggregate : IAggregateRoot
{
    public PlainKey Id { get; init; }

    public string Name { get; set; } = string.Empty;
}

public sealed class PlainKeyDbContext(DbContextOptions<PlainKeyDbContext> options) : DbContext(options)
{
    public DbSet<PlainKeyAggregate> Aggregates => Set<PlainKeyAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PlainKeyAggregate>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion(k => k.Value, g => new PlainKey(g)).ValueGeneratedNever();
        });
}

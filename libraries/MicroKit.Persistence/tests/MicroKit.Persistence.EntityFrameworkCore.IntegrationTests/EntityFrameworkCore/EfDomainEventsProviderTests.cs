using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MicroKit.Domain.Events;
using MicroKit.Persistence.EntityFrameworkCore;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.EntityFrameworkCore;

public sealed class EfDomainEventsProviderTests
{
    [Fact]
    public void DrainDomainEvents_WithSeveralTrackedAggregates_DrainsAllInOneCall()
    {
        using var context = CreateContext();
        var first = new TestAggregate();
        var second = new TestAggregate();
        first.Raise(new TestEvent("first-a", DateTimeOffset.UtcNow));
        first.Raise(new TestEvent("first-b", DateTimeOffset.UtcNow));
        second.Raise(new TestEvent("second-a", DateTimeOffset.UtcNow));
        context.Add(first);
        context.Add(second);
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        var drained = sut.DrainDomainEvents();

        Assert.Equal(3, drained.Count);
        // The same names, whatever their order.
        string[] expected = ["first-a", "first-b", "second-a"];
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            drained.OfType<TestEvent>().Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DrainDomainEvents_WhenNoAggregateHasPendingEvents_ReturnsEmpty()
    {
        using var context = CreateContext();
        context.Add(new TestAggregate());
        context.Add(new TestAggregate());
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        var drained = sut.DrainDomainEvents();

        Assert.Empty(drained);
    }

    [Fact]
    public void DrainDomainEvents_IgnoresTrackedEntitiesExposingNoEvents()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        aggregate.Raise(new TestEvent("only-one", DateTimeOffset.UtcNow));
        context.Add(aggregate);
        context.Add(new TestEntityWithoutEvents());
        context.Add(new TestAggregate());
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        var drained = sut.DrainDomainEvents();

        Assert.Single(drained);
        Assert.Equal("only-one", Assert.IsType<TestEvent>(drained[0]).Name);
    }

    [Fact]
    public void DrainDomainEvents_SkipsEntityThatExposesEventsButCannotDrain()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        aggregate.Raise(new TestEvent("drainable", DateTimeOffset.UtcNow));
        var exposeOnly = new TestExposeOnly();
        exposeOnly.Raise(new TestEvent("not-drainable", DateTimeOffset.UtcNow));
        context.Add(aggregate);
        context.Add(exposeOnly);
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        // The documented asymmetry: the read member reports everything exposed...
        Assert.Equal(2, sut.DomainEvents.Count);

        // ...the drain takes only what implements IDomainEventsProvider.
        var drained = sut.DrainDomainEvents();

        Assert.Single(drained);
        Assert.Equal("drainable", Assert.IsType<TestEvent>(drained[0]).Name);
        Assert.Single(exposeOnly.DomainEvents);
    }

    [Fact]
    public void DrainDomainEvents_LeavesDrainedAggregateEmpty()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        aggregate.Raise(new TestEvent("once", DateTimeOffset.UtcNow));
        context.Add(aggregate);
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        Assert.Single(sut.DrainDomainEvents());
        var second = sut.DrainDomainEvents();

        Assert.Empty(second);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void DomainEvents_ReadsWithoutClearing()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        aggregate.Raise(new TestEvent("kept", DateTimeOffset.UtcNow));
        context.Add(aggregate);
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        Assert.Single(sut.DomainEvents);
        Assert.Single(sut.DomainEvents);

        Assert.Single(sut.DrainDomainEvents());
    }

    [Fact]
    public void AddUnitOfWork_RegistersDomainEventsProvider()
    {
        var services = new ServiceCollection();
        services.AddMicroKitPersistence(persistence =>
            persistence.AddEntityFrameworkCore(ef => ef
                .AddDbContext<TestDbContext>(o => o.UseSqlite(InMemoryConnectionString))
                .AddUnitOfWork<TestDbContext>()));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IDomainEventsProvider>();

        Assert.IsType<EfDomainEventsProvider<TestDbContext>>(resolved);
    }

    /// <summary>
    /// #196 D7 probe: a drain pays at most one full <c>DetectChanges</c>, whatever the number of
    /// tracked aggregates. Kept as a regression guard.
    /// </summary>
    [Fact]
    public void DrainDomainEvents_RunsAtMostOneFullDetectChanges()
    {
        using var context = CreateContext();
        for (var i = 0; i < 3; i++)
        {
            var aggregate = new TestAggregate();
            aggregate.Raise(new TestEvent($"event-{i}", DateTimeOffset.UtcNow));
            context.Add(aggregate);
            context.Add(new TestEntityWithoutEvents());
        }

        var sut = new EfDomainEventsProvider<TestDbContext>(context);
        var fullDetections = 0;
        context.ChangeTracker.DetectingAllChanges += (_, _) => fullDetections++;

        var drained = sut.DrainDomainEvents();

        Assert.Equal(3, drained.Count);
        Assert.True(fullDetections <= 1);
    }

    // The connection is never opened: the model is built, nothing is executed.
    private const string InMemoryConnectionString = "DataSource=:memory:";

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(InMemoryConnectionString)
            .Options;

        return new TestDbContext(options);
    }
}

public sealed record TestEvent(string Name, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Stands in for an aggregate: exposes events and can drain them.</summary>
public sealed class TestAggregate : IDomainEventsProvider
{
    private readonly List<IDomainEvent> _events = [];

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>A mutable scalar, so write-path tests can reach the <c>Modified</c> state.</summary>
    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<IDomainEvent> DomainEvents =>
        _events.Count == 0 ? Array.Empty<IDomainEvent>() : _events.AsReadOnly();

    public void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> DrainDomainEvents()
    {
        if (_events.Count == 0)
            return Array.Empty<IDomainEvent>();

        var drained = _events.ToArray();
        _events.Clear();
        return drained;
    }
}

/// <summary>Exposes events via the read contract but offers no way to drain them.</summary>
public sealed class TestExposeOnly : IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public Guid Id { get; init; } = Guid.NewGuid();

    public IReadOnlyList<IDomainEvent> DomainEvents => _events.AsReadOnly();

    public void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);
}

/// <summary>A tracked entity with no domain-event contract at all.</summary>
public sealed class TestEntityWithoutEvents
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

    public DbSet<TestExposeOnly> ExposeOnly => Set<TestExposeOnly>();

    public DbSet<TestEntityWithoutEvents> Plain => Set<TestEntityWithoutEvents>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // DomainEvents is a domain concern, never a mapped navigation.
        modelBuilder.Entity<TestAggregate>().Ignore(nameof(TestAggregate.DomainEvents));
        modelBuilder.Entity<TestExposeOnly>().Ignore(nameof(TestExposeOnly.DomainEvents));
        modelBuilder.Entity<TestEntityWithoutEvents>();
    }
}

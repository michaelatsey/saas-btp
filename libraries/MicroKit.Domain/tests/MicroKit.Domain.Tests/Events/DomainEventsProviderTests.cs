using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using Xunit;

namespace MicroKit.Domain.Tests.Events;

public sealed class DomainEventsProviderTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DomainEvents_Initially_ShouldBeEmpty()
    {
        // Arrange & Act
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));

        // Assert
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void DomainEvents_AfterRaisingEvent_ShouldContainEvent()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var domainEvent = new TestDomainEvent("Test Data", OccurredAt);

        // Act
        aggregate.RaiseEvent(domainEvent);

        // Assert
        Assert.Single(aggregate.DomainEvents);
        Assert.Contains(domainEvent, aggregate.DomainEvents);
    }

    [Fact]
    public void DomainEvents_AfterRaisingMultipleEvents_ShouldMaintainOrder()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var event1 = new TestDomainEvent("Event 1", OccurredAt);
        var event2 = new TestDomainEvent("Event 2", OccurredAt);
        var event3 = new TestDomainEvent("Event 3", OccurredAt);

        // Act
        aggregate.RaiseEvent(event1);
        aggregate.RaiseEvent(event2);
        aggregate.RaiseEvent(event3);

        // Assert
        Assert.Equal(3, aggregate.DomainEvents.Count);
        Assert.Equal(event1, aggregate.DomainEvents[0]);
        Assert.Equal(event2, aggregate.DomainEvents[1]);
        Assert.Equal(event3, aggregate.DomainEvents[2]);
    }

    [Fact]
    public void DrainDomainEvents_WhenEmpty_ShouldReturnEmptyCollection()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));

        // Act
        var events = aggregate.DrainDomainEvents();

        // Assert
        Assert.Empty(events);
        Assert.Same(Array.Empty<IDomainEvent>(), events);
    }

    [Fact]
    public void DrainDomainEvents_WithEvents_ShouldReturnAllEventsAndClearCollection()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var event1 = new TestDomainEvent("Event 1", OccurredAt);
        var event2 = new TestDomainEvent("Event 2", OccurredAt);

        aggregate.RaiseEvent(event1);
        aggregate.RaiseEvent(event2);

        // Act
        var events = aggregate.DrainDomainEvents();

        // Assert
        Assert.Equal(2, events.Count);
        Assert.Equal(event1, events[0]);
        Assert.Equal(event2, events[1]);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void DrainDomainEvents_AfterDraining_ShouldReturnEmptyCollectionOnSubsequentCalls()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var domainEvent = new TestDomainEvent("Test Data", OccurredAt);

        aggregate.RaiseEvent(domainEvent);
        aggregate.DrainDomainEvents(); // First drain

        // Act
        var events = aggregate.DrainDomainEvents(); // Second drain

        // Assert
        Assert.Empty(events);
        Assert.Same(Array.Empty<IDomainEvent>(), events);
    }

    [Fact]
    public void DrainDomainEvents_ReturnedCollection_ShouldBeImmutable()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var domainEvent = new TestDomainEvent("Test Data", OccurredAt);

        aggregate.RaiseEvent(domainEvent);

        // Act
        var events = aggregate.DrainDomainEvents();

        // Assert
        Assert.IsAssignableFrom<IReadOnlyCollection<IDomainEvent>>(events);

        // Verify that even if cast to IList, modifications would throw
        if (events is IList<IDomainEvent> list)
        {
            Assert.Throws<NotSupportedException>(() => list.Add(new TestDomainEvent("Should fail", OccurredAt)));
        }
    }
}

// Test implementations
public sealed record TestId(Guid Value) : IEntityId;

public sealed record TestDomainEvent(string Data, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public class TestAggregateRoot : AggregateRoot<TestId>
{
    public TestAggregateRoot(TestId id) : base(id) { }

    public void RaiseEvent(IDomainEvent domainEvent)
    {
        RaiseDomainEvent(domainEvent);
    }
}

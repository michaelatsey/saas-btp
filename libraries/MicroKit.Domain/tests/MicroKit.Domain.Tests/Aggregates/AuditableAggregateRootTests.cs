using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using Xunit;

namespace MicroKit.Domain.Tests.Aggregates;

// What is specific to the aggregate base. The audit rules both bases share run in AuditRulesTests.
public sealed class AuditableAggregateRootTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Actor Creator = new("user-123");

    [Fact]
    public void Constructor_ShouldImplementIAuditableEntity()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);

        Assert.IsAssignableFrom<IAuditableEntity>(aggregate);
        Assert.IsAssignableFrom<AggregateRoot<TestAuditableId>>(aggregate);
    }

    [Fact]
    public void Constructor_ShouldSetId()
    {
        var id = NewId();

        Assert.Equal(id, new TestAuditableAggregateRoot(id, CreatedAt, Creator).Id);
    }

    [Fact]
    public void RaiseDomainEvent_StoresTheEventAsSupplied()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);
        var domainEvent = new TestAuditableEvent("Test", CreatedAt);

        aggregate.TestRaiseEvent(domainEvent);

        Assert.Equal([domainEvent], aggregate.DomainEvents);
        Assert.Equal(CreatedAt, aggregate.DomainEvents[0].OccurredAt);
    }

    private static TestAuditableId NewId() => new(Guid.CreateVersion7());
}

// Test implementations
public sealed record TestAuditableId(Guid Value) : IEntityId;

public sealed record TestAuditableEvent(string Data, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed class TestAuditableAggregateRoot : AuditableAggregateRoot<TestAuditableId>
{
    public TestAuditableAggregateRoot(TestAuditableId id, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy) { }

    public void TestRaiseEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
}

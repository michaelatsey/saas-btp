using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using Xunit;

namespace MicroKit.Domain.Tests.Events;

public sealed class DomainEventTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OccurredAt_IsThePositionalArgument()
    {
        Assert.Equal(OccurredAt, new SampleEvent("a", OccurredAt).OccurredAt);
    }

    [Fact]
    public void EventId_IsAVersion7Guid()
    {
        Assert.Equal(7, new SampleEvent("a", OccurredAt).EventId.Version);
    }

    [Fact]
    public void EventId_IsUniquePerEvent()
    {
        Assert.NotEqual(new SampleEvent("a", OccurredAt).EventId, new SampleEvent("a", OccurredAt).EventId);
    }

    [Fact]
    public void EventId_CanBePreservedOnReplay()
    {
        var original = new SampleEvent("a", OccurredAt);

        var replayed = new SampleEvent("a", original.OccurredAt) { EventId = original.EventId };

        Assert.Equal(original, replayed);
    }

    [Fact]
    public void With_ChangingAMember_GivesANewEventId()
    {
        var original = new SampleEvent("a", OccurredAt);

        var later = original with { OccurredAt = OccurredAt.AddMinutes(1) };

        Assert.NotEqual(original.EventId, later.EventId);
        Assert.Equal(7, later.EventId.Version);
    }

    [Fact]
    public void With_NoChange_CopiesTheFactUnderANewEventId()
    {
        var original = new SampleEvent("a", OccurredAt);

        var copy = original with { };

        Assert.Equal("a", copy.Data);
        Assert.Equal(original.OccurredAt, copy.OccurredAt);
        Assert.NotEqual(original.EventId, copy.EventId);
    }

    [Fact]
    public void With_ExplicitEventId_KeepsIt()
    {
        var original = new SampleEvent("a", OccurredAt);

        var replayed = original with { Data = "b", EventId = original.EventId };

        Assert.Equal(original.EventId, replayed.EventId);
        Assert.Equal("b", replayed.Data);
    }

    [Fact]
    public void Initializer_ExplicitEventId_KeepsIt()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(id, new SampleEvent("a", OccurredAt) { EventId = id }.EventId);
    }

    [Fact]
    public void Initializer_EmptyEventId_ThrowsArgumentException()
    {
        Assert.Equal("EventId", Assert.Throws<ArgumentException>(() => new SampleEvent("a", OccurredAt) { EventId = Guid.Empty })
            .ParamName);
    }

    [Fact]
    public void With_EmptyEventId_ThrowsArgumentException()
    {
        var original = new SampleEvent("a", OccurredAt);

        Assert.Equal("EventId", Assert.Throws<ArgumentException>(() => original with { EventId = Guid.Empty })
            .ParamName);
    }

    [Fact]
    public void OccurredAt_NonZeroOffset_IsStoredAtOffsetZero()
    {
        var plusTwo = OccurredAt.ToOffset(TimeSpan.FromHours(2));

        var evt = new SampleEvent("a", plusTwo);

        Assert.Equal(TimeSpan.Zero, evt.OccurredAt.Offset);
        Assert.Equal(plusTwo.UtcDateTime, evt.OccurredAt.UtcDateTime);
    }

    [Fact]
    public void OccurredAt_NonZeroOffsetThroughWith_IsStoredAtOffsetZero()
    {
        var later = OccurredAt.AddMinutes(1).ToOffset(TimeSpan.FromHours(2));

        var evt = new SampleEvent("a", OccurredAt) with { OccurredAt = later };

        Assert.Equal(TimeSpan.Zero, evt.OccurredAt.Offset);
        Assert.Equal(later.UtcDateTime, evt.OccurredAt.UtcDateTime);
    }

    [Fact]
    public void OccurredAt_Default_ThrowsArgumentOutOfRangeException()
    {
        Assert.Equal("OccurredAt", Assert.Throws<ArgumentOutOfRangeException>(() => new SampleEvent("a", default))
            .ParamName);
    }

    [Fact]
    public void OccurredAt_DefaultThroughWith_ThrowsArgumentOutOfRangeException()
    {
        var evt = new SampleEvent("a", OccurredAt);

        Assert.Equal("OccurredAt", Assert.Throws<ArgumentOutOfRangeException>(() => evt with { OccurredAt = default })
            .ParamName);
    }

    [Fact]
    public void GuidIdNew_IsAVersion7Guid()
    {
        Assert.Equal(7, GuidId.New().Value.Version);
    }

    private sealed record SampleEvent(string Data, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
}

using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MicroKit.Domain.Tests.Aggregates;

// AuditRules (#179), the audit rules both auditable bases share (#112, #113, #116, #165, #171, #176),
// run once against each base.
public sealed class AuditRulesTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PlusTwo = new(2026, 10, 24, 11, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset At = CreatedAt.AddMinutes(5);
    private static readonly Actor Creator = new("user-123");
    private static readonly Actor Updater = new("user-456");

    public static TheoryData<string> Bases => [AuditedSubject.Entity, AuditedSubject.Aggregate];

    // --- creation ---

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_ExplicitInstant_SetsCreationFields(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        Assert.Equal(CreatedAt, subject.CreatedAt);
        Assert.Equal(Creator, subject.CreatedBy);
        Assert.Null(subject.UpdatedAt);
        Assert.Null(subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_FakeTimeProvider_DrivesCreatedAt(string kind)
    {
        var clock = new FakeTimeProvider(CreatedAt);

        var subject = AuditedSubject.Create(kind, clock, Actor.System);

        Assert.Equal(CreatedAt, subject.CreatedAt);
        Assert.Equal(Actor.System, subject.CreatedBy);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(CreatedAt.AddHours(1), AuditedSubject.Create(kind, clock, Actor.System).CreatedAt);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_NullTimeProvider_ThrowsArgumentNullException(string kind)
    {
        Assert.Equal("timeProvider", Assert.Throws<ArgumentNullException>(() => AuditedSubject.Create(kind, (TimeProvider)null!, Creator))
            .ParamName);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_NullCreator_ThrowsArgumentNullException(string kind)
    {
        Assert.Equal("createdBy", Assert.Throws<ArgumentNullException>(() => AuditedSubject.Create(kind, CreatedAt, null!))
            .ParamName);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_NonZeroOffset_IsStoredAtOffsetZero(string kind)
    {
        var subject = AuditedSubject.Create(kind, PlusTwo, Creator);

        Assert.Equal(TimeSpan.Zero, subject.CreatedAt.Offset);
        Assert.Equal(PlusTwo.UtcDateTime, subject.CreatedAt.UtcDateTime);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Constructor_DefaultInstant_ThrowsArgumentOutOfRangeException(string kind)
    {
        Assert.Equal("createdAt", Assert.Throws<ArgumentOutOfRangeException>(() => AuditedSubject.Create(kind, default(DateTimeOffset), Creator))
            .ParamName);
    }

    // --- Mutate: validation before the mutation runs ---

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_InvalidInstant_ThrowsBeforeTheMutationRuns(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var ran = false;

        Assert.Throws<ArgumentOutOfRangeException>(() => subject.Run(CreatedAt.AddTicks(-1), Updater, () => ran = true));
        Assert.Throws<ArgumentOutOfRangeException>(() => subject.Run(default, Updater, () => ran = true));

        Assert.False(ran);
        Assert.Null(subject.UpdatedAt);
        Assert.Null(subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_DefaultInstant_ThrowsArgumentOutOfRangeException(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        Assert.Equal("at", Assert.Throws<ArgumentOutOfRangeException>(() => subject.Run(default, Creator, () => { }))
            .ParamName);
        Assert.Null(subject.UpdatedAt);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_BeforeCreatedAt_ThrowsArgumentOutOfRangeException(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => subject.Run(CreatedAt.AddTicks(-1), Creator, () => { }));

        Assert.Equal("at", ex.ParamName);
        Assert.Contains("cannot precede 2026-10-24T09:00:00.0000000+00:00, the creation instant", ex.Message);
        Assert.Null(subject.UpdatedAt);
        Assert.Null(subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_EarlierThanThePreviousUpdate_ThrowsBeforeTheMutationRuns(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var at1005 = new DateTimeOffset(2026, 10, 24, 10, 5, 0, TimeSpan.Zero);
        subject.Run(at1005, Updater, () => { });
        var ran = false;

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => subject.Run(new DateTimeOffset(2026, 10, 24, 10, 1, 0, TimeSpan.Zero), Creator, () => ran = true));

        Assert.Equal("at", ex.ParamName);
        Assert.Contains("cannot precede 2026-10-24T10:05:00.0000000+00:00, the previous modification", ex.Message);
        Assert.False(ran);
        Assert.Equal(at1005, subject.UpdatedAt);
        Assert.Equal(Updater, subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NullActor_ThrowsBeforeTheMutationRuns(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var ran = false;

        Assert.Equal("by", Assert.Throws<ArgumentNullException>(() => subject.Run(At, null!, () => ran = true))
            .ParamName);

        Assert.False(ran);
        Assert.Null(subject.UpdatedAt);
        Assert.Null(subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NullMutation_ThrowsArgumentNullException(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        Assert.Equal("mutation", Assert.Throws<ArgumentNullException>(() => subject.Run(At, Updater, null!))
            .ParamName);

        Assert.Null(subject.UpdatedAt);
    }

    // --- Mutate: outcome ---

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_SuccessfulMutation_RunsItAndSetsBothFields(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        subject.Rename("final", At, Updater);

        Assert.Equal("final", subject.Name);
        Assert.Equal(At, subject.UpdatedAt);
        Assert.Equal(Updater, subject.UpdatedBy);
        Assert.Equal(CreatedAt, subject.CreatedAt);
        Assert.Equal(Creator, subject.CreatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_ThrowingMutation_LeavesTheAuditFieldsUnchanged(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        subject.Run(At, Updater, () => { });

        Assert.Throws<InvalidOperationException>(() => subject.Run(At.AddMinutes(1), Creator, () => throw new InvalidOperationException("rule broken")));

        Assert.Equal(At, subject.UpdatedAt);
        Assert.Equal(Updater, subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NonZeroOffset_IsStoredAtOffsetZero(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var later = At.ToOffset(TimeSpan.FromHours(2));

        subject.Run(later, Creator, () => { });

        Assert.Equal(TimeSpan.Zero, subject.UpdatedAt!.Value.Offset);
        Assert.Equal(later.UtcDateTime, subject.UpdatedAt.Value.UtcDateTime);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_EqualToCreatedAt_IsAccepted(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        // The same instant in another offset is equal, not earlier.
        subject.Run(CreatedAt.ToOffset(TimeSpan.FromHours(-5)), Creator, () => { });

        Assert.Equal(CreatedAt, subject.UpdatedAt);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_SameInstantTwice_IsAccepted(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        subject.Run(At, Creator, () => { });
        subject.Run(At, Actor.System, () => { });

        Assert.Equal(At, subject.UpdatedAt);
        Assert.Equal(Actor.System, subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_EqualToThePreviousUpdateInAnotherOffset_IsAccepted(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        subject.Run(At, Creator, () => { });

        subject.Run(At.ToOffset(TimeSpan.FromHours(2)), Actor.System, () => { });

        Assert.Equal(At, subject.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, subject.UpdatedAt!.Value.Offset);
        Assert.Equal(Actor.System, subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NestedLaterModification_IsNotOverwrittenByTheOuterOne(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var at1005 = At.AddMinutes(5);

        subject.Run(At, Creator, () => subject.Run(at1005, Updater, () => { }));

        Assert.Equal(at1005, subject.UpdatedAt);
        Assert.Equal(Updater, subject.UpdatedBy);
        Assert.Throws<ArgumentOutOfRangeException>(() => subject.Run(at1005.AddTicks(-1), Creator, () => { }));
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_ThrowingMutation_UndoesTheStampOfANestedSuccessfulMutate(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        subject.Run(At, Updater, () => { });

        Assert.Throws<InvalidOperationException>(() => subject.Run(At.AddMinutes(1), Creator, () =>
        {
            subject.Run(At.AddMinutes(2), Actor.System, () => { });
            throw new InvalidOperationException("rule broken");
        }));

        Assert.Equal(At, subject.UpdatedAt);
        Assert.Equal(Updater, subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NestedModificationAtTheSameInstant_KeepsTheNestedActor(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);

        subject.Run(At, Creator, () => subject.Run(At, Actor.System, () => { }));

        Assert.Equal(At, subject.UpdatedAt);
        Assert.Equal(Actor.System, subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NestedModificationRepeatingTheCurrentStamp_KeepsTheNestedActor(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        subject.Run(At, Actor.System, () => { });

        // The nested call writes the stamp already there: same instant, same singleton actor.
        subject.Run(At, Creator, () => subject.Run(At, Actor.System, () => { }));

        Assert.Equal(At, subject.UpdatedAt);
        Assert.Equal(Actor.System, subject.UpdatedBy);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Mutate_NestedEarlierModification_IsSupersededByTheOuterOne(string kind)
    {
        var subject = AuditedSubject.Create(kind, CreatedAt, Creator);
        var at1005 = At.AddMinutes(5);

        subject.Run(at1005, Creator, () => subject.Run(At, Updater, () => { }));

        Assert.Equal(at1005, subject.UpdatedAt);
        Assert.Equal(Creator, subject.UpdatedBy);
    }

    [Fact]
    public void UpdateFields_AreSetOnlyThroughMutate()
    {
        System.Reflection.PropertyInfo[] properties =
        [
            typeof(AuditableAggregateRoot<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedAt))!,
            typeof(AuditableAggregateRoot<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedBy))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedAt))!,
            typeof(AuditableEntity<GuidId>).GetProperty(nameof(IAuditableEntity.UpdatedBy))!,
        ];

        // A private setter leaves Mutate as the only way a derived type can change the field.
        Assert.All(properties, p => Assert.True(p.SetMethod!.IsPrivate));
    }

    // --- aggregate only: domain events ---

    [Fact]
    public void Mutate_ThrowingMutation_RemovesTheEventsItRaised()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("draft", At);
        var kept = Assert.Single(aggregate.DomainEvents);

        Assert.Throws<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            aggregate.Raise(new Renamed("lost", At.AddMinutes(1)));
            aggregate.Raise(new Renamed("lost too", At.AddMinutes(1)));
            throw new InvalidOperationException("rule broken");
        }));

        Assert.Equal([kept], aggregate.DomainEvents);
        Assert.Equal(At, aggregate.UpdatedAt);
    }

    [Fact]
    public void Mutate_ThrowingMutationThatDrainedEvents_KeepsItsExceptionAndRemovesWhatItRaised()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("one", At);
        aggregate.RenameAndRaise("two", At);
        IReadOnlyList<IDomainEvent> drained = [];

        var ex = Assert.Throws<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            drained = aggregate.DrainDomainEvents();
            aggregate.Raise(new Renamed("lost", At.AddMinutes(1)));
            throw new InvalidOperationException("rule broken");
        }));

        Assert.Equal("rule broken", ex.Message);
        Assert.Equal(2, drained.Count);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void Mutate_ThrowingMutation_KeepsEventsRaisedAfterAnEarlierDrain()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("one", At);
        aggregate.DrainDomainEvents();
        aggregate.RenameAndRaise("two", At);
        var kept = Assert.Single(aggregate.DomainEvents);

        Assert.Throws<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            aggregate.Raise(new Renamed("lost", At.AddMinutes(1)));
            throw new InvalidOperationException("rule broken");
        }));

        Assert.Equal([kept], aggregate.DomainEvents);
    }

    [Fact]
    public void Mutate_NestedThrowingMutation_RemovesOnlyTheEventsEachLevelRaised()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("pending", At);
        var pending = Assert.Single(aggregate.DomainEvents);

        Assert.Throws<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            aggregate.Raise(new Renamed("outer", At.AddMinutes(1)));
            aggregate.Run(At.AddMinutes(2), Updater, () =>
            {
                aggregate.Raise(new Renamed("inner", At.AddMinutes(2)));
                throw new InvalidOperationException("inner rule broken");
            });
        }));

        Assert.Equal([pending], aggregate.DomainEvents);
    }

    [Fact]
    public void Mutate_InnerRollbackCaughtByTheOuterMutation_KeepsTheOuterEvents()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);
        aggregate.RenameAndRaise("pending", At);

        aggregate.Run(At.AddMinutes(1), Updater, () =>
        {
            aggregate.Raise(new Renamed("outer", At.AddMinutes(1)));
            Assert.Throws<InvalidOperationException>(() => aggregate.Run(At.AddMinutes(2), Updater, () =>
            {
                aggregate.Raise(new Renamed("inner", At.AddMinutes(2)));
                throw new InvalidOperationException("inner rule broken");
            }));
        });

        Assert.Equal(["pending", "outer"], aggregate.DomainEvents.Select(e => ((Renamed)e).Name));
    }

    [Fact]
    public void Mutate_SuccessfulMutation_KeepsTheEventsItRaised()
    {
        var aggregate = new AuditedAggregate(new GuidId(Guid.CreateVersion7()), CreatedAt, Creator);

        aggregate.RenameAndRaise("final", At);

        Assert.Equal(new Renamed("final", At) { EventId = aggregate.DomainEvents[0].EventId }, Assert.Single(aggregate.DomainEvents));
        Assert.Equal(At, aggregate.UpdatedAt);
    }
}

internal sealed record Renamed(string Name, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>A uniform view over both auditable bases, so one theory exercises each.</summary>
internal interface IAuditedSubject
{
    string Name { get; }
    DateTimeOffset CreatedAt { get; }
    Actor CreatedBy { get; }
    DateTimeOffset? UpdatedAt { get; }
    Actor? UpdatedBy { get; }

    void Run(DateTimeOffset at, Actor by, Action mutation);

    void Rename(string name, DateTimeOffset at, Actor by);
}

internal static class AuditedSubject
{
    public const string Entity = "AuditableEntity";
    public const string Aggregate = "AuditableAggregateRoot";

    public static IAuditedSubject Create(string kind, DateTimeOffset createdAt, Actor createdBy) => kind switch
    {
        Entity => new AuditedEntity(new GuidId(Guid.CreateVersion7()), createdAt, createdBy),
        Aggregate => new AuditedAggregate(new GuidId(Guid.CreateVersion7()), createdAt, createdBy),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static IAuditedSubject Create(string kind, TimeProvider clock, Actor createdBy) => kind switch
    {
        Entity => new AuditedEntity(new GuidId(Guid.CreateVersion7()), clock, createdBy),
        Aggregate => new AuditedAggregate(new GuidId(Guid.CreateVersion7()), clock, createdBy),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

internal sealed class AuditedEntity : AuditableEntity<GuidId>, IAuditedSubject
{
    public AuditedEntity(GuidId id, DateTimeOffset createdAt, Actor createdBy) : base(id, createdAt, createdBy) { }

    public AuditedEntity(GuidId id, TimeProvider clock, Actor createdBy) : base(id, clock, createdBy) { }

    public string Name { get; private set; } = "draft";

    public void Run(DateTimeOffset at, Actor by, Action mutation) => Mutate(at, by, mutation);

    public void Rename(string name, DateTimeOffset at, Actor by) => Mutate(at, by, () => Name = name);
}

internal sealed class AuditedAggregate : AuditableAggregateRoot<GuidId>, IAuditedSubject
{
    public AuditedAggregate(GuidId id, DateTimeOffset createdAt, Actor createdBy) : base(id, createdAt, createdBy) { }

    public AuditedAggregate(GuidId id, TimeProvider clock, Actor createdBy) : base(id, clock, createdBy) { }

    public string Name { get; private set; } = "draft";

    public void Run(DateTimeOffset at, Actor by, Action mutation) => Mutate(at, by, mutation);

    public void Rename(string name, DateTimeOffset at, Actor by) => Mutate(at, by, () => Name = name);

    public void RenameAndRaise(string name, DateTimeOffset at) => Mutate(at, Actor.System, () =>
    {
        Name = name;
        RaiseDomainEvent(new Renamed(name, at));
    });

    public void Raise(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
}

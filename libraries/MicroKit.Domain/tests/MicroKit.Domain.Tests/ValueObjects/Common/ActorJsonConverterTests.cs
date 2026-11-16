using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Domain.Events;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Domain.ValueObjects.Common.Json;
using Xunit;

namespace MicroKit.Domain.Tests.ValueObjects.Common;

// These tests exercise reflection-based System.Text.Json with default and Web options, which is
// what an application without a source-generated context gets. The test assembly is never trimmed.
#pragma warning disable IL2026
public sealed class ActorJsonConverterTests
{
    public static TheoryData<string> Options => ["default", "web"];

    private static JsonSerializerOptions For(string options) => options switch
    {
        "default" => JsonSerializerOptions.Default,
        "web" => JsonSerializerOptions.Web,
        _ => throw new ArgumentOutOfRangeException(nameof(options)),
    };

    private static T RoundTrip<T>(T value, string options) => RoundTrip(value, For(options));

    private static T RoundTrip<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!;

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_ReservedValue_ThroughTheDefaultConverter_ThrowsJsonExceptionWithAPath(string options)
    {
        var json = options == "web" ? """{"by":"$system"}""" : """{"By":"$system"}""";

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ActorHolder>(json, For(options)));

        Assert.Equal(options == "web" ? "$.by" : "$.By", ex.Path);
        Assert.IsType<DomainException>(ex.InnerException);
    }

    // #181, MicroKit ADR-004 D1 (michaelatsey/microkit@a12097b): writing a built-in actor carries no risk, so the default converter writes it.
    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltIn_WithTheDefaultConverter_WritesItsValue(string options)
    {
        Assert.Equal("\"$system\"", JsonSerializer.Serialize(Actor.System, For(options)));
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltInAsDictionaryKey_WithTheDefaultConverter_WritesItsValue(string options)
    {
        var byActor = new Dictionary<Actor, int> { [Actor.System] = 1 };

        Assert.Equal("""{"$system":1}""", JsonSerializer.Serialize(byActor, For(options)));
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Record_CarryingABuiltInActor_WithTheDefaultConverter_WritesItsValue(string options)
    {
        var json = JsonSerializer.Serialize(new AuditSnapshot(Actor.System), For(options));

        Assert.Equal(options == "web" ? """{"updatedBy":"$system"}""" : """{"UpdatedBy":"$system"}""", json);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Record_CarryingABuiltInActor_WrittenWithTheDefaultConverter_CannotBeReadBack(string options)
    {
        var json = JsonSerializer.Serialize(new AuditSnapshot(Actor.System), For(options));

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AuditSnapshot>(json, For(options)));

        Assert.Equal(options == "web" ? "$.updatedBy" : "$.UpdatedBy", ex.Path);
        Assert.IsType<DomainException>(ex.InnerException);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_BuiltInAsDictionaryKey_WrittenWithTheDefaultConverter_CannotBeReadBack(string options)
    {
        var json = JsonSerializer.Serialize(new Dictionary<Actor, int> { [Actor.System] = 1 }, For(options));

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<Actor, int>>(json, For(options)));

        Assert.Equal("$.$system", ex.Path);
        Assert.IsType<DomainException>(ex.InnerException);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Actor_User_RoundTrips(string options)
    {
        var actor = new Actor("user-123");

        Assert.Equal("\"user-123\"", JsonSerializer.Serialize(actor, For(options)));
        Assert.Equal(actor, RoundTrip(actor, options));
    }

    [Fact]
    public void Actor_UnknownReservedValue_FailsOnDeserialization()
    {
        Assert.IsType<DomainException>(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Actor>("\"$root\""))
            .InnerException);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void DomainEvent_CarryingMoneyAUserActorAndABuiltInActorValue_RoundTrips(string options)
    {
        // A built-in actor travels as its value until the outbox can register the trusted converter (#174).
        var placed = new OrderPlaced(
            new Money(99.99m, Currency.FromCode("EUR")),
            Actor.System.Value,
            new Actor("user-123"),
            new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));

        var read = RoundTrip(placed, options);

        Assert.Equal(placed.Total, read.Total);
        Assert.Same(Actor.System, Actor.Restore(read.PlacedBy));
        Assert.Equal(placed.Customer, read.Customer);
        Assert.Equal(placed.OccurredAt, read.OccurredAt);
        Assert.Equal(placed.EventId, read.EventId);
        Assert.Equal(placed, read);
    }
}
#pragma warning restore IL2026

public sealed record ActorHolder(Actor By);

public sealed record AuditSnapshot(Actor UpdatedBy);

public sealed record OrderPlaced(Money Total, string PlacedBy, Actor Customer, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

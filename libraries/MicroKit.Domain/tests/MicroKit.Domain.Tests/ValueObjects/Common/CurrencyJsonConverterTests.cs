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
public sealed class CurrencyJsonConverterTests
{
    public static TheoryData<string> Options => ["default", "web"];

    private static JsonSerializerOptions For(string options) => options switch
    {
        "default" => JsonSerializerOptions.Default,
        "web" => JsonSerializerOptions.Web,
        _ => throw new ArgumentOutOfRangeException(nameof(options)),
    };

    private static JsonSerializerOptions Trusted(string options) =>
        new(For(options)) { Converters = { new TrustedActorJsonConverter() } };

    private static T RoundTrip<T>(T value, string options) => RoundTrip(value, For(options));

    private static T RoundTrip<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!;

    [Theory]
    [MemberData(nameof(Options))]
    public void Currency_RoundTrips(string options)
    {
        var currency = Currency.FromCode("KWD");

        Assert.Equal("\"KWD\"", JsonSerializer.Serialize(currency, For(options)));
        Assert.Equal(currency, RoundTrip(currency, options));
    }

    [Fact]
    public void Currency_UnknownCode_FailsOnDeserialization()
    {
        Assert.IsType<DomainException>(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Currency>("\"ABC\""))
            .InnerException);
    }

    [Fact]
    public void Currency_Default_FailsOnSerialization()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(Currency)));
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void CurrencyAndActor_RoundTripAsDictionaryKeys(string options)
    {
        var totals = new Dictionary<Currency, Money>
        {
            [Currency.FromCode("EUR")] = new(10.50m, Currency.FromCode("EUR")),
            [Currency.FromCode("XOF")] = new(1500m, Currency.FromCode("XOF")),
        };
        var byActor = new Dictionary<Actor, int> { [new Actor("user-1")] = 1, [new Actor("user-2")] = 2 };
        var byTrustedActor = new Dictionary<Actor, int> { [Actor.System] = 1, [new Actor("user-1")] = 2 };

        // xUnit compares dictionaries by key and value, whatever their order.
        Assert.Equal(totals, RoundTrip(totals, options));
        Assert.Equal(byActor, RoundTrip(byActor, options));
        // Written with default options, read back only with the trusted converter (MicroKit ADR-004 D1 (michaelatsey/microkit@a12097b)).
        var written = JsonSerializer.Serialize(byTrustedActor, For(options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<Actor, int>>(written, For(options)));

        var trusted = RoundTrip(byTrustedActor, Trusted(options));
        Assert.Equal(1, trusted[Actor.System]);
        Assert.Equal(2, trusted[new Actor("user-1")]);
    }
}
#pragma warning restore IL2026

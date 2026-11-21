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
public sealed class MoneyJsonConverterTests
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
    [InlineData("default", 10.50, "EUR")]
    [InlineData("default", 1500, "XOF")]
    [InlineData("default", 1.234, "TND")]
    [InlineData("web", 10.50, "EUR")]
    [InlineData("web", 1500, "XOF")]
    [InlineData("web", 1.234, "TND")]
    public void Money_RoundTrips(string options, decimal amount, string code)
    {
        var money = new Money(amount, Currency.FromCode(code));

        Assert.Equal(money, RoundTrip(money, options));
    }

    [Fact]
    public void Money_DefaultOptions_WritesAmountAndCurrencyCodeOnly()
    {
        Assert.Equal("""{"Amount":10.50,"Currency":"EUR"}""", JsonSerializer.Serialize(new Money(10.50m, Currency.FromCode("EUR"))));
    }

    [Fact]
    public void Money_WebOptions_FollowsTheNamingPolicy()
    {
        Assert.Equal("""{"amount":10.50,"currency":"EUR"}""", JsonSerializer.Serialize(new Money(10.50m, Currency.FromCode("EUR")), JsonSerializerOptions.Web));
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Money_UnknownCurrencyCode_FailsOnDeserialization(string options)
    {
        var json = options == "web"
            ? """{"amount":10,"currency":"ABC"}"""
            : """{"Amount":10,"Currency":"ABC"}""";

        Assert.IsType<DomainException>(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>(json, For(options)))
            .InnerException);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Money_ExcessScale_FailsOnDeserialization(string options)
    {
        var json = options == "web"
            ? """{"amount":1.5,"currency":"JPY"}"""
            : """{"Amount":1.5,"Currency":"JPY"}""";

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>(json, For(options)));
        Assert.IsType<DomainException>(ex.InnerException);
    }

    [Fact]
    public void Money_MissingCurrency_FailsOnDeserialization()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("""{"Amount":10}"""));
    }

    [Fact]
    public void Money_InvalidContent_ReportsTheJsonPath()
    {
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>("""{"Total":{"Amount":1.5,"Currency":"JPY"}}"""));

        Assert.Equal("$.Total", ex.Path);
    }

    [Fact]
    public void Money_WriteAsString_WritesTheAmountAsAString()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString };
        var money = new Money(10.50m, Currency.FromCode("EUR"));

        var json = JsonSerializer.Serialize(money, options);

        Assert.Equal("""{"Amount":"10.50","Currency":"EUR"}""", json);
        Assert.Equal(money, JsonSerializer.Deserialize<Money>(json, options));
    }

    [Fact]
    public void Money_QuotedAmountWithExponent_IsRead()
    {
        Assert.Equal(100m, JsonSerializer.Deserialize<Money>("""{"Amount":"1E2","Currency":"EUR"}""", JsonSerializerOptions.Web)!
            .Amount);
    }
}
#pragma warning restore IL2026

public sealed record Holder(Money Total);

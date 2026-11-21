using System.Text.Json;
using MicroKit.Result.Serialization;

namespace MicroKit.Result.Tests.Serialization;

// MicroKit ADR-RESULT-001 D6 (michaelatsey/microkit@a12097b): "value": null on a successful payload is invalid; the converter fails with JsonException,
// never with the ArgumentNullException of Success(null).
public sealed class ResultJsonConverterNullValueTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new ResultJsonConverterFactory() } };

    [Fact]
    public void Read_WhenSuccessWithNullReferenceValue_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":null}""";

        // Act + Assert
        var thrown = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<string>>(json, Options));
        Assert.IsType<JsonException>(thrown);
        Assert.IsType<ArgumentNullException>(thrown.InnerException);
    }

    [Fact]
    public void Read_WhenValueNullPrecedesIsSuccess_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"value":null,"isSuccess":true}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<string>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessWithNullNullableValueType_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":null}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int?>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessWithNullObjectValue_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":null}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<Payload>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessWithNonNullValue_ReturnsSuccess()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":"ada"}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<string>>(json, Options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ada", result.Value);
    }

    private sealed record Payload(string Name);
}

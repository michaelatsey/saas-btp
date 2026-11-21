using System.Text.Json;
using MicroKit.Result.Serialization;

namespace MicroKit.Result.Tests.Serialization;

// #184 Phase 2: a payload that does not describe a valid result fails deserialization.
public sealed class ResultJsonConverterValidationTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new ResultJsonConverterFactory() } };

    [Theory]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m"},"isSuccess":true,"value":1}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"","message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"   ","message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"value":1,"error":{"code":"A.B","message":"m"}}""")]
    [InlineData("""{"isSuccess":true,"value":1,"value":2}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m"},"error":{"code":"C.D","message":"n"}}""")]
    public void Read_WhenPayloadIsNotAValidResult_ThrowsJsonException(string json)
    {
        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessHasExplicitValue_ReturnsSuccess()
    {
        // Act
        Result<int> result = JsonSerializer.Deserialize<Result<int>>("""{"isSuccess":true,"value":0}""", Options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }
}

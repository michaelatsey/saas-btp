using System.Text.Json;
using MicroKit.Result.Serialization;
using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Serialization;

public sealed class ResultJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ResultJsonConverterFactory() },
    };

    // ── Write ─────────────────────────────────────────────────────────────

    [Fact]
    public void Write_WhenSuccess_WritesIsSuccessAndValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert
        Assert.Equal("""{"isSuccess":true,"value":42}""", json);
    }

    [Fact]
    public void Write_WhenSuccessWithRecordValue_WritesValueObject()
    {
        // Arrange
        var result = Result.Success(new Person("Ada", 36));

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert
        Assert.Equal("""{"isSuccess":true,"value":{"Name":"Ada","Age":36}}""", json);
    }

    [Fact]
    public void Write_WhenFailure_WritesCodeMessageCategoryAndSeverity()
    {
        // Arrange
        var result = Result.Failure<int>(new NotFoundTestError(7));

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound","severity":"Error"}}""", json);
    }

    [Fact]
    public void Write_WhenFailureHasSeverityAndMetadata_WritesSeverityWithoutMetadata()
    {
        // Arrange
        var result = Result.Failure<int>(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(["code", "message", "category", "severity"], error.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Unavailable", error.GetProperty("category").GetString());
        Assert.Equal("Critical", error.GetProperty("severity").GetString());
    }

    [Fact]
    public void Write_WhenFailureIsErrorCollection_WritesTheErrorsOfItsCategory()
    {
        // Arrange
        var result = Result.Failure<int>(ErrorCollection.From(new NotFoundTestError(1), new OtherError()));

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert: #198 review, a default converter names what ToHttpResult names: the NotFound errors only
        Assert.Equal("""{"isSuccess":false,"error":{"code":"MULTI.ERROR","message":"Item 1 not found","category":"NotFound","severity":"Error","errors":[{"code":"TEST.NOT_FOUND","message":"Item 1 not found","category":"NotFound","severity":"Error"}]}}""", json);
    }

    [Fact]
    public void Write_WhenDefault_ThrowsJsonException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        var thrown = Assert.Throws<JsonException>(() => JsonSerializer.Serialize(result, Options));
        Assert.Contains("uninitialized (default) Result<T>", thrown.Message);
    }

    [Fact]
    public void Write_WhenNestedInCollection_WritesEachResult()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), Result.Failure<int>(new OtherError())];

        // Act
        var json = JsonSerializer.Serialize(results, Options);

        // Assert
        Assert.Equal("""[{"isSuccess":true,"value":1},{"isSuccess":false,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict","severity":"Error"}}]""", json);
    }

    // ── Round trip ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WhenSuccessWithRecordValue_KeepsValue()
    {
        // Arrange
        var original = Result.Success(new Person("Ada", 36));
        var json = JsonSerializer.Serialize(original, Options);

        // Act
        var read = JsonSerializer.Deserialize<Result<Person>>(json, Options);

        // Assert
        Assert.True(read.IsSuccess);
        Assert.Equal(new Person("Ada", 36), read.Value);
    }

    [Fact]
    public void RoundTrip_WhenFailure_KeepsCodeMessageAndCategory()
    {
        // Arrange
        var original = Result.Failure<int>(new NotFoundTestError(7));
        var json = JsonSerializer.Serialize(original, Options);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        Assert.True(read.IsFailure);
        Assert.Equal("TEST.NOT_FOUND", read.Error.Code.Value);
        Assert.Equal("Item 7 not found", read.Error.Message);
        Assert.Equal(ErrorCategory.NotFound, read.Error.Category);
    }

    [Fact]
    public void RoundTrip_WhenFailureHasSeverityAndMetadata_KeepsSeverityAndDropsMetadata()
    {
        // Arrange
        var original = Result.Failure<int>(new CriticalTestError());
        var json = JsonSerializer.Serialize(original, Options);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Unavailable, read.Error.Category);
        Assert.Equal(ErrorSeverity.Critical, read.Error.Severity);
        Assert.Empty(read.Error.Metadata);
    }

    // ── Read ──────────────────────────────────────────────────────────────

    [Fact]
    public void Read_WhenSuccess_ReturnsSuccessWithValue()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":42}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Read_WhenValueBeforeIsSuccess_ReturnsSuccessWithValue()
    {
        // Arrange
        const string json = """{"value":42,"isSuccess":true}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Read_WhenFailure_ReturnsDeserializedErrorWithCodeMessageAndCategory()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<DeserializedError>(result.Error);
        Assert.Equal("TEST.NOT_FOUND", result.Error.Code.Value);
        Assert.Equal("Item 7 not found", result.Error.Message);
        Assert.Equal(ErrorCategory.NotFound, result.Error.Category);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
        Assert.Empty(result.Error.Metadata);
    }

    [Fact]
    public void Read_WhenUnknownPropertiesPresent_SkipsThem()
    {
        // Arrange
        const string json = """
            {
              "extra": {"nested": [1, 2, {"deep": true}]},
              "isSuccess": false,
              "error": {
                "severity": "Critical",
                "code": "TEST.OTHER",
                "metadata": {"retryAfter": 30},
                "unknown": {"nested": [true, null]},
                "message": "Other error",
                "category": "Conflict"
              },
              "trailing": "ignored"
            }
            """;

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<DeserializedError>(result.Error);
        Assert.Equal("TEST.OTHER", result.Error.Code.Value);
        Assert.Equal("Other error", result.Error.Message);
        Assert.Equal(ErrorCategory.Conflict, result.Error.Category);
        Assert.Equal(ErrorSeverity.Critical, result.Error.Severity);
        Assert.Equal(new Dictionary<string, object?> { ["retryAfter"] = 30L }, result.Error.Metadata);
    }

    [Fact]
    public void Read_WhenErrorHasErrorsArray_RebuildsCollectionAndIgnoresOuterFields()
    {
        // Arrange
        const string json = """
            {
              "isSuccess": false,
              "error": {
                "severity": "Critical",
                "code": "TEST.OTHER",
                "metadata": {"retryAfter": 30},
                "message": "Other error",
                "category": "Conflict",
                "errors": [{"code": "X"}]
              }
            }
            """;

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        Assert.True(result.IsFailure);
        var collection = Assert.IsType<ErrorCollection>(result.Error);
        Assert.Single(collection);
        Assert.Equal("MULTI.ERROR", collection.Code.Value);
        Assert.Equal("Unknown error", collection.Message);
        Assert.Equal(ErrorCategory.Technical, collection.Category);
        Assert.Equal(ErrorSeverity.Error, collection.Severity);
        Assert.Empty(collection.Metadata);
        var inner = Assert.IsType<DeserializedError>(collection[0]);
        Assert.Equal("X", inner.Code.Value);
    }

    [Theory]
    [InlineData("notfound", ErrorCategory.NotFound)]
    [InlineData("NOTFOUND", ErrorCategory.NotFound)]
    [InlineData("businessRule", ErrorCategory.BusinessRule)]
    public void Read_WhenCategoryCaseDiffers_ParsesCaseInsensitively(string category, ErrorCategory expected)
    {
        // Arrange
        var json = $$$"""{"isSuccess":false,"error":{"code":"X.Y","message":"m","category":"{{{category}}}"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(expected, result.Error.Category);
    }

    [Fact]
    public void Read_WhenCategoryUnknown_UsesTechnical()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"X.Y","message":"m","category":"NoSuchCategory"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Fact]
    public void Read_WhenIsSuccessMissing_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"value":42}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("true")]
    public void Read_WhenTokenIsNotObject_ThrowsJsonException(string json)
    {
        // Arrange + Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Theory]
    [InlineData("""{"isSuccess":[true]}""")]
    [InlineData("""{"isSuccess":{"a":1}}""")]
    [InlineData("""{"isSuccess":"true"}""")]
    public void Read_WhenIsSuccessIsNotBoolean_ThrowsJsonException(string json)
    {
        // Arrange + Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenErrorIsNotObject_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":"boom"}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenValueHasWrongType_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":"not a number"}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessHasNoValue_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessAlsoHasError_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":1,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict"}}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenFailureHasNoError_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    // ── Factory ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(Result<int>), true)]
    [InlineData(typeof(Result<string>), true)]
    [InlineData(typeof(Result), true)]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(List<int>), false)]
    public void CanConvert_WhenTypeGiven_AcceptsResultAndResultOfT(Type type, bool expected)
    {
        // Arrange
        var factory = new ResultJsonConverterFactory();

        // Act
        var canConvert = factory.CanConvert(type);

        // Assert
        Assert.Equal(expected, canConvert);
    }

    [Fact]
    public void CreateConverter_WhenResultOfT_ReturnsTypedConverter()
    {
        // Arrange
        var factory = new ResultJsonConverterFactory();

        // Act
        var converter = factory.CreateConverter(typeof(Result<int>), Options);

        // Assert
        Assert.IsType<ResultJsonConverter<int>>(converter);
    }

    private sealed record Person(string Name, int Age);
}

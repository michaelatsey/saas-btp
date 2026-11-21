using System.Text.Json;
using MicroKit.Result.Serialization;
using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Serialization;

// #184 Phase 5: the non-generic Result has its own reflection-free converter, carried by its [JsonConverter]
// attribute, so it serializes with or without a registered factory, never with metadata (MicroKit ADR-RESULT-001 D12 (michaelatsey/microkit@a12097b)).
// #195: without a registered converter, a fault of the system is written with the generic message.
public sealed class NonGenericResultJsonConverterTests
{
    private static readonly JsonSerializerOptions FactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory() },
    };

    private static readonly JsonSerializerOptions DefaultOptions = new();

    private static readonly JsonSerializerOptions MetadataFactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory(ResultJsonView.Full) },
    };

    private static readonly JsonSerializerOptions MetadataConverterOptions = new()
    {
        Converters = { new ResultJsonConverter(ResultJsonView.Full) },
    };

    // ── Write ─────────────────────────────────────────────────────────────

    [Fact]
    public void Write_WhenSuccess_WritesIsSuccessOnly()
    {
        // Arrange
        var result = Result.Success();

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        Assert.Equal("""{"isSuccess":true}""", json);
    }

    [Fact]
    public void Write_WhenFailure_WritesCodeMessageCategoryAndSeverity()
    {
        // Arrange
        var result = Result.Failure(new NotFoundTestError(7));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound","severity":"Error"}}""", json);
    }

    [Fact]
    public void Write_WhenFailureIsValidationError_WritesPropertyName()
    {
        // Arrange
        var result = Result.Failure(ValidationError.Custom("Email", "Email is invalid"));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"VALIDATION.EMAIL","message":"Email is invalid","category":"Validation","severity":"Warning","propertyName":"Email"}}""", json);
    }

    [Fact]
    public void Write_WhenFailureIsErrorCollection_WritesTheErrorsOfItsCategory()
    {
        // Arrange
        var result = Result.Failure(ErrorCollection.From(new NotFoundTestError(1), new OtherError()));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert: #198 review, a default converter names what ToHttpResult names: the NotFound errors only
        Assert.Equal("""{"isSuccess":false,"error":{"code":"MULTI.ERROR","message":"Item 1 not found","category":"NotFound","severity":"Error","errors":[{"code":"TEST.NOT_FOUND","message":"Item 1 not found","category":"NotFound","severity":"Error"}]}}""", json);
    }

    [Fact]
    public void Write_WhenFailureHasMetadata_DoesNotWriteMetadata()
    {
        // Arrange
        var result = Result.Failure(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert: #195, a default converter writes a fault of the system with the generic message
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"The server could not complete the request.","category":"Unavailable","severity":"Critical"}}""", json);
    }

    [Fact]
    public void Write_WhenDefault_ThrowsJsonException()
    {
        // Arrange
        Result result = default;

        // Act + Assert
        var thrown = Assert.Throws<JsonException>(() => JsonSerializer.Serialize(result, FactoryOptions));
        Assert.Contains("uninitialized (default) Result", thrown.Message);
    }

    [Fact]
    public void Write_WhenWriterIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var converter = new ResultJsonConverter();

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => converter.Write(null!, Result.Success(), DefaultOptions));
    }

    // ── Read ──────────────────────────────────────────────────────────────

    [Fact]
    public void Read_WhenSuccess_ReturnsSuccess()
    {
        // Arrange
        const string json = """{"isSuccess":true}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Read_WhenFailure_ReturnsDeserializedError()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict","severity":"Critical"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        Assert.True(result.IsFailure);
        var error = Assert.IsType<DeserializedError>(result.Error);
        Assert.Equal("TEST.OTHER", error.Code.Value);
        Assert.Equal("Other error", error.Message);
        Assert.Equal(ErrorCategory.Conflict, error.Category);
        Assert.Equal(ErrorSeverity.Critical, error.Severity);
        Assert.Empty(error.Metadata);
    }

    [Fact]
    public void Read_WhenErrorBeforeIsSuccess_ReturnsFailure()
    {
        // Arrange
        const string json = """{"error":{"code":"TEST.OTHER","message":"Other error"},"isSuccess":false}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("TEST.OTHER", result.Error.Code.Value);
    }

    [Fact]
    public void Read_WhenUnknownPropertiesPresent_SkipsThem()
    {
        // Arrange
        const string json = """{"extra":{"nested":[1,{"deep":true}]},"isSuccess":true,"trailing":"ignored"}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("""{"isSuccess":true,"value":1}""")]
    [InlineData("""{"isSuccess":true,"value":null}""")]
    [InlineData("""{"value":{},"isSuccess":false,"error":{"code":"A.B","message":"m"}}""")]
    [InlineData("""{"isSuccess":true,"isSuccess":true}""")]
    [InlineData("""{"isSuccess":false,"isSuccess":true}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m"},"error":{"code":"C.D","message":"n"}}""")]
    [InlineData("""{"isSuccess":true,"error":{"code":"A.B","message":"m"}}""")]
    [InlineData("""{"isSuccess":false}""")]
    [InlineData("""{"error":{"code":"A.B","message":"m"}}""")]
    [InlineData("""{}""")]
    [InlineData("""{"isSuccess":"true"}""")]
    [InlineData("""{"isSuccess":false,"error":"boom"}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"","message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","errors":[]}}""")]
    public void Read_WhenPayloadIsNotAValidResult_ThrowsJsonException(string json)
    {
        // Arrange + Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result>(json, FactoryOptions));
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
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result>(json, FactoryOptions));
    }

    [Fact]
    public void Read_WhenValuePresent_ThrowsJsonExceptionNamingValue()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":1}""";

        // Act + Assert
        var thrown = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result>(json, FactoryOptions));
        Assert.Contains("'value'", thrown.Message);
    }

    // ── Round trip ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WhenSuccess_ReturnsEqualSuccess()
    {
        // Arrange
        var original = Result.Success();
        var json = JsonSerializer.Serialize(original, FactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        Assert.True(read.IsSuccess);
        Assert.Equal(original, read);
    }

    [Fact]
    public void RoundTrip_WhenFailure_KeepsCodeCategoryAndSeverity()
    {
        // Arrange
        var original = Result.Failure(new CriticalTestError());
        var json = JsonSerializer.Serialize(original, FactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert: #195, a default converter writes a fault of the system (Unavailable) with the generic message
        Assert.True(read.IsFailure);
        Assert.Equal("TEST.CRITICAL", read.Error.Code.Value);
        Assert.Equal("The server could not complete the request.", read.Error.Message);
        Assert.Equal(ErrorCategory.Unavailable, read.Error.Category);
        Assert.Equal(ErrorSeverity.Critical, read.Error.Severity);
        Assert.Empty(read.Error.Metadata);
    }

    [Fact]
    public void RoundTrip_WhenFailureIsValidationError_ReturnsDeserializedValidationError()
    {
        // Arrange
        var original = Result.Failure(ValidationError.Required("Name"));
        var json = JsonSerializer.Serialize(original, FactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        Assert.True(read.IsFailure);
        var error = Assert.IsType<DeserializedValidationError>(read.Error);
        Assert.Equal("Name", error.PropertyName);
        Assert.Equal("VALIDATION.NAME", error.Code.Value);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Equal(ErrorSeverity.Warning, error.Severity);
    }

    // ── [JsonConverter] attribute, no converter registered ────────────────

    [Fact]
    public void Serialize_WhenNoConverterRegisteredAndSuccess_WritesIsSuccessWithoutThrowing()
    {
        // Arrange
        var result = Result.Success();

        // Act
        var json = JsonSerializer.Serialize(result, DefaultOptions);

        // Assert
        Assert.Equal("""{"isSuccess":true}""", json);
    }

    [Fact]
    public void Serialize_WhenNoConverterRegisteredAndFailureHasMetadata_WritesErrorWithoutMetadata()
    {
        // Arrange
        var result = Result.Failure(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, DefaultOptions);

        // Assert
        // #195: with no converter registered, a fault of the system (Unavailable) is written with the generic message
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"The server could not complete the request.","category":"Unavailable","severity":"Critical"}}""", json);
    }

    [Fact]
    public void Serialize_WhenNoConverterRegisteredAndDefault_ThrowsJsonException()
    {
        // Arrange
        Result result = default;

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(result, DefaultOptions));
    }

    [Fact]
    public void Deserialize_WhenNoConverterRegistered_ReadsFailure()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, DefaultOptions);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<DeserializedError>(result.Error);
        Assert.Equal(ErrorCategory.Conflict, result.Error.Category);
    }

    [Fact]
    public void Deserialize_WhenNoConverterRegisteredAndPayloadInvalid_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"error":{"code":"A.B","message":"m"}}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result>(json, DefaultOptions));
    }

    [Fact]
    public void Serialize_WhenResultIsPropertyOfAType_UsesAttributeConverterWithoutMetadata()
    {
        // Arrange
        var envelope = new Envelope("save", Result.Failure(new CriticalTestError()));

        // Act
        var json = JsonSerializer.Serialize(envelope, DefaultOptions);

        // Assert
        // #195: with no converter registered, a fault of the system (Unavailable) is written with the generic message
        Assert.Equal("""{"Name":"save","Outcome":{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"The server could not complete the request.","category":"Unavailable","severity":"Critical"}}}""", json);
    }

    [Fact]
    public void Serialize_WhenSuccessIsPropertyOfAType_WritesIsSuccessOnly()
    {
        // Arrange
        var envelope = new Envelope("save", Result.Success());

        // Act
        var json = JsonSerializer.Serialize(envelope, DefaultOptions);

        // Assert
        Assert.Equal("""{"Name":"save","Outcome":{"isSuccess":true}}""", json);
    }

    [Fact]
    public void RoundTrip_WhenResultIsPropertyOfAType_KeepsResult()
    {
        // Arrange
        var original = new Envelope("save", Result.Failure(new OtherError()));
        var json = JsonSerializer.Serialize(original, DefaultOptions);

        // Act
        var read = JsonSerializer.Deserialize<Envelope>(json, DefaultOptions);

        // Assert
        Assert.NotNull(read);
        Assert.Equal("save", read.Name);
        Assert.True(read.Outcome.IsFailure);
        Assert.Equal("TEST.OTHER", read.Outcome.Error.Code.Value);
        Assert.Equal(ErrorCategory.Conflict, read.Outcome.Error.Category);
    }

    // ── Options converters take precedence over the type attribute ────────

    [Fact]
    public void Serialize_WhenFactoryWithMetadataRegistered_TakesPrecedenceAndWritesMetadata()
    {
        // Arrange
        var result = Result.Failure(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, MetadataFactoryOptions);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"Critical error","category":"Unavailable","severity":"Critical","metadata":{"retryAfter":30}}}""", json);
    }

    [Fact]
    public void Serialize_WhenConverterWithMetadataRegistered_TakesPrecedenceAndWritesMetadata()
    {
        // Arrange
        var result = Result.Failure(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, MetadataConverterOptions);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"Critical error","category":"Unavailable","severity":"Critical","metadata":{"retryAfter":30}}}""", json);
    }

    [Fact]
    public void Serialize_WhenFactoryWithMetadataRegisteredAndResultIsProperty_WritesMetadata()
    {
        // Arrange
        var envelope = new Envelope("save", Result.Failure(new CriticalTestError()));

        // Act
        var json = JsonSerializer.Serialize(envelope, MetadataFactoryOptions);

        // Assert
        Assert.Equal("""{"Name":"save","Outcome":{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"Critical error","category":"Unavailable","severity":"Critical","metadata":{"retryAfter":30}}}}""", json);
    }

    [Fact]
    public void RoundTrip_WhenFactoryWithMetadataRegistered_KeepsMetadata()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Failure(new CriticalTestError()), MetadataFactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, MetadataFactoryOptions);

        // Assert
        Assert.True(read.IsFailure);
        Assert.Equal(new Dictionary<string, object?> { ["retryAfter"] = 30L }, read.Error.Metadata);
    }

    // ── Factory ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateConverter_WhenResult_ReturnsNonGenericConverter()
    {
        // Arrange
        var factory = new ResultJsonConverterFactory();

        // Act
        var converter = factory.CreateConverter(typeof(Result), FactoryOptions);

        // Assert
        Assert.IsType<ResultJsonConverter>(converter);
    }

    // ── #195: the converter Result carries reaches clients by default ─────

    [Theory]
    [InlineData(ErrorCategory.Technical)]
    [InlineData(ErrorCategory.External)]
    [InlineData(ErrorCategory.Unavailable)]
    [InlineData(ErrorCategory.Timeout)]
    [InlineData(ErrorCategory.NotSupported)]
    [InlineData((ErrorCategory)999)]
    public void Write_WithoutConverterRegistered_WhenSystemFault_WritesAGenericMessage(ErrorCategory category)
    {
        // Arrange
        var result = Result.Failure(new FaultError(category, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, DefaultOptions);

        // Assert
        Assert.DoesNotContain("db-7", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("The server could not complete the request.", error.GetProperty("message").GetString());
        Assert.Equal("TEST.FAULT", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Write_WithoutConverterRegistered_WhenNotFound_KeepsItsMessage()
    {
        // Arrange
        var result = Result.Failure(new NotFoundTestError(7));

        // Act
        var json = JsonSerializer.Serialize(result, DefaultOptions);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound","severity":"Error"}}""", json);
    }

    [Fact]
    public void Write_WithDefaultFactoryRegistered_WhenSystemFault_WritesAGenericMessage()
    {
        // Arrange: the factory a host registers for its endpoints writes what a client may read
        var result = Result.Failure(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        Assert.DoesNotContain("db-7", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(MetadataOptions))]
    public void Write_WithMetadataConverterRegistered_WhenSystemFault_KeepsItsMessage(JsonSerializerOptions options)
    {
        // Arrange: ResultJsonView.Full is for in-process storage, never a response: it writes everything
        var result = Result.Failure(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, options);

        // Assert
        Assert.Contains("Host db-7 refused the login of sa", json);
    }

    public static TheoryData<JsonSerializerOptions> MetadataOptions() => new() { MetadataFactoryOptions, MetadataConverterOptions };

    [Fact]
    public void RoundTrip_WithoutConverterRegistered_WhenSystemFault_ReadsTheGenericMessage()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Failure(new FaultError(ErrorCategory.Technical, "secret")), DefaultOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, DefaultOptions);

        // Assert
        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCode.From("TEST.FAULT"), read.Error.Code);
        Assert.Equal(ErrorCategory.Technical, read.Error.Category);
        Assert.Equal("The server could not complete the request.", read.Error.Message);
    }

    private sealed record FaultError(ErrorCategory Kind, string Text) : Error(ErrorCode.From("TEST.FAULT"), Text)
    {
        public override ErrorCategory Category => Kind;
    }

    private sealed record Envelope(string Name, Result Outcome);
}

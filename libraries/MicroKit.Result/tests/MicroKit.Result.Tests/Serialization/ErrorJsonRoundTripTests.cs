using System.Globalization;
using System.Text.Json;
using MicroKit.Result.Serialization;
using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Serialization;

// #184 Phase 5: the error object keeps Severity, the inner errors of an ErrorCollection, PropertyName and, on
// opt-in (ResultJsonView.Full), Metadata across a round trip (MicroKit ADR-RESULT-001 D1, D12 (michaelatsey/microkit@a12097b)).
public sealed class ErrorJsonRoundTripTests
{
    // The factory, as a consumer registers it.
    private static readonly JsonSerializerOptions FactoryMetadataOptions = new()
    {
        Converters = { new ResultJsonConverterFactory(ResultJsonView.Full) },
    };

    // The converter created directly: the encoding tests below do not depend on how the factory creates it.
    private static readonly JsonSerializerOptions MetadataOptions = new()
    {
        Converters = { new ResultJsonConverter<int>(ResultJsonView.Full) },
    };

    private static readonly JsonSerializerOptions DefaultFactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory() },
    };

    private static readonly JsonSerializerOptions DefaultConverterOfTOptions = new()
    {
        Converters = { new ResultJsonConverter<int>() },
    };

    private static readonly Guid SampleId = new("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    // ── Severity and metadata ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WhenFactoryIncludesMetadata_KeepsSeverityCategoryAndMetadata()
    {
        // Arrange
        var original = Result.Failure<int>(new CriticalTestError());
        var json = JsonSerializer.Serialize(original, FactoryMetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, FactoryMetadataOptions);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"Critical error","category":"Unavailable","severity":"Critical","metadata":{"retryAfter":30}}}""", json);
        Assert.True(read.IsFailure);
        var error = Assert.IsType<DeserializedError>(read.Error);
        Assert.Equal("TEST.CRITICAL", error.Code.Value);
        Assert.Equal("Critical error", error.Message);
        Assert.Equal(ErrorCategory.Unavailable, error.Category);
        Assert.Equal(ErrorSeverity.Critical, error.Severity);
        Assert.Equal(new Dictionary<string, object?> { ["retryAfter"] = 30L }, error.Metadata);
    }

    [Fact]
    public void Write_WithTheFullViewAndMetadataEmpty_OmitsMetadata()
    {
        // Arrange
        var result = Result.Failure<int>(new OtherError());

        // Act
        var json = JsonSerializer.Serialize(result, MetadataOptions);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict","severity":"Error"}}""", json);
    }

    [Fact]
    public void Write_WithTheFullView_WritesPrimitivesAsJsonValues()
    {
        // Arrange
        var metadata = ErrorMetadata.Create(
            new("text", "hello"),
            new("flag", true),
            new("off", false),
            new("int", 42),
            new("long", 9_000_000_000L),
            new("short", (short)-7),
            new("byte", (byte)255),
            new("uint", 4_000_000_000u),
            new("ulong", ulong.MaxValue),
            new("decimal", 12.50m),
            new("double", 1.25d),
            new("float", 0.5f),
            new("guid", SampleId),
            new("dateTime", new DateTime(2026, 11, 7, 10, 30, 0, DateTimeKind.Utc)),
            new("dateTimeOffset", new DateTimeOffset(2026, 11, 7, 10, 30, 0, TimeSpan.FromHours(1))),
            new("none", null));
        var result = Result.Failure<int>(new MetadataError(metadata));

        // Act
        var json = JsonSerializer.Serialize(result, MetadataOptions);

        // Assert: each value as its JSON primitive; metadata is an immutable dictionary, whose key order is not defined
        using var document = JsonDocument.Parse(json);
        using var expected = JsonDocument.Parse(
            """{"text":"hello","flag":true,"off":false,"int":42,"long":9000000000,"short":-7,"byte":255,"uint":4000000000,"ulong":18446744073709551615,"decimal":12.50,"double":1.25,"float":0.5,"guid":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","dateTime":"2026-11-07T10:30:00Z","dateTimeOffset":"2026-11-07T10:30:00+01:00","none":null}""");
        var written = document.RootElement.GetProperty("error").GetProperty("metadata");
        Assert.True(JsonElement.DeepEquals(written, expected.RootElement), written.GetRawText());
        Assert.Equal("12.50", written.GetProperty("decimal").GetRawText()); // DeepEquals compares numbers by value
    }

    [Fact]
    public void RoundTrip_WithTheFullView_ReadsPrimitivesAsStringLongDoubleBoolOrNull()
    {
        // Arrange
        var metadata = ErrorMetadata.Create(
            new("text", "hello"),
            new("flag", true),
            new("off", false),
            new("int", 42),
            new("long", 9_000_000_000L),
            new("ulong", ulong.MaxValue),
            new("decimal", 12.50m),
            new("double", 1.25d),
            new("guid", SampleId),
            new("dateTime", new DateTime(2026, 11, 7, 10, 30, 0, DateTimeKind.Utc)),
            new("dateTimeOffset", new DateTimeOffset(2026, 11, 7, 10, 30, 0, TimeSpan.FromHours(1))),
            new("none", null));
        var json = JsonSerializer.Serialize(Result.Failure<int>(new MetadataError(metadata)), MetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);

        // Assert
        Assert.True(read.IsFailure);
        var values = read.Error.Metadata;
        Assert.Equal(12, values.Count);
        Assert.Equal("hello", Assert.IsType<string>(values["text"]));
        Assert.True(Assert.IsType<bool>(values["flag"]));
        Assert.False(Assert.IsType<bool>(values["off"]));
        Assert.Equal(42L, Assert.IsType<long>(values["int"]));
        Assert.Equal(9_000_000_000L, Assert.IsType<long>(values["long"]));
        Assert.Equal(ulong.MaxValue, Assert.IsType<ulong>(values["ulong"]));
        Assert.Equal(12.5d, Assert.IsType<double>(values["decimal"]));
        Assert.Equal(1.25d, Assert.IsType<double>(values["double"]));
        Assert.Equal("3f2504e0-4f89-11d3-9a0c-0305e82c3301", Assert.IsType<string>(values["guid"]));
        Assert.Equal("2026-11-07T10:30:00Z", Assert.IsType<string>(values["dateTime"]));
        Assert.Equal("2026-11-07T10:30:00+01:00", Assert.IsType<string>(values["dateTimeOffset"]));
        Assert.Null(values["none"]);
    }

    [Fact]
    public void RoundTrip_WhenMetadataValueIsNotPrimitive_KeepsItsString()
    {
        // Arrange
        var metadata = ErrorMetadata.Create("money", new Money(10, "EUR"));
        var json = JsonSerializer.Serialize(Result.Failure<int>(new MetadataError(metadata)), MetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);

        // Assert
        Assert.True(read.IsFailure);
        Assert.Equal("10 EUR", read.Error.Metadata["money"]);
    }

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void Write_WhenMetadataDoubleIsNotFinite_WritesItsInvariantString(double value, string expected)
    {
        // Arrange
        var result = Result.Failure<int>(new MetadataError(ErrorMetadata.Create("value", value)));

        // Act
        var json = JsonSerializer.Serialize(result, MetadataOptions);

        // Assert
        using var document = JsonDocument.Parse(json);
        var written = document.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("value");
        Assert.Equal(JsonValueKind.String, written.ValueKind);
        Assert.Equal(expected, written.GetString());
    }

    [Fact]
    public void Write_WhenMetadataFloatIsNotFinite_WritesItsInvariantString()
    {
        // Arrange
        var result = Result.Failure<int>(new MetadataError(ErrorMetadata.Create("value", float.NaN)));

        // Act
        var json = JsonSerializer.Serialize(result, MetadataOptions);

        // Assert
        using var document = JsonDocument.Parse(json);
        Assert.Equal("NaN", document.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("value").GetString());
    }

    [Fact]
    public void Write_WhenMetadataValueIsFormattable_UsesInvariantCulture()
    {
        // Arrange
        var result = Result.Failure<int>(new MetadataError(ErrorMetadata.Create("day", new DateOnly(2026, 11, 7))));
        var previous = CultureInfo.CurrentCulture;
        string json;

        // Act
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            json = JsonSerializer.Serialize(result, MetadataOptions);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Assert
        using var document = JsonDocument.Parse(json);
        Assert.Equal("11/07/2026", document.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("day").GetString());
    }

    [Fact]
    public void RoundTrip_WhenMetadataValueIsJsonElement_KeepsItAsJsonElement()
    {
        // Arrange
        using var source = JsonDocument.Parse("""{"limits":[1,2,{"deep":true}],"name":"x"}""");
        var nested = source.RootElement.Clone();
        var json = JsonSerializer.Serialize(
            Result.Failure<int>(new MetadataError(ErrorMetadata.Create("nested", nested))), MetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);

        // Assert
        Assert.True(read.IsFailure);
        var element = Assert.IsType<JsonElement>(read.Error.Metadata["nested"]);
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal("""{"limits":[1,2,{"deep":true}],"name":"x"}""", element.GetRawText());
    }

    [Fact]
    public void Read_WhenMetadataValueIsArray_ReturnsJsonElement()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":{"ids":[1,2]}}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        Assert.True(result.IsFailure);
        var element = Assert.IsType<JsonElement>(result.Error.Metadata["ids"]);
        Assert.Equal(JsonValueKind.Array, element.ValueKind);
        Assert.Equal("[1,2]", element.GetRawText());
    }

    [Fact]
    public void RoundTrip_WhenConverterCreatedDirectlyWithMetadata_KeepsMetadata()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Failure<int>(new CriticalTestError()), MetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);

        // Assert
        Assert.True(read.IsFailure);
        Assert.Equal(new Dictionary<string, object?> { ["retryAfter"] = 30L }, read.Error.Metadata);
    }

    // ── Default factory never writes metadata ─────────────────────────────

    [Fact]
    public void Write_WhenDefaultFactory_NeverWritesMetadata()
    {
        // Arrange
        var inner = new MetadataError(ErrorMetadata.Create("secret", "internal"));
        var result = Result.Failure<int>(ErrorCollection.From(inner, new CriticalTestError()));

        // Act
        var json = JsonSerializer.Serialize(result, DefaultFactoryOptions);

        // Assert
        Assert.DoesNotContain("\"metadata\"", json);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("retryAfter", json);
    }

    [Fact]
    public void Write_WhenDefaultConverterOfT_NeverWritesMetadata()
    {
        // Arrange
        var result = Result.Failure<int>(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, DefaultConverterOfTOptions);

        // Assert: #195, a default converter writes a fault of the system with the generic message
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"The server could not complete the request.","category":"Unavailable","severity":"Critical"}}""", json);
    }

    [Fact]
    public void Read_WhenDefaultFactoryAndPayloadHasMetadata_StillReadsIt()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":{"retryAfter":30}}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(new Dictionary<string, object?> { ["retryAfter"] = 30L }, result.Error.Metadata);
    }

    // ── ErrorCollection and validation errors ─────────────────────────────

    [Fact]
    public void RoundTrip_WhenNestedCollectionWithValidationError_KeepsStructurePropertyNameAndOrder()
    {
        // Arrange
        var nested = ErrorCollection.From(ValidationError.Required("Email"), ValidationError.MaxLength("Name", 10));
        var original = Result.Failure<int>(ErrorCollection.From(nested, new CriticalTestError(), new NotFoundTestError(3)));
        var json = JsonSerializer.Serialize(original, FactoryMetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, FactoryMetadataOptions);

        // Assert
        Assert.True(read.IsFailure);
        var outer = Assert.IsType<ErrorCollection>(read.Error);
        Assert.Equal(3, outer.Count);
        Assert.Equal(original.Error.Message, outer.Message);
        Assert.Equal(original.Error.Category, outer.Category);
        Assert.Equal(ErrorSeverity.Critical, outer.Severity);

        var readNested = Assert.IsType<ErrorCollection>(outer[0]);
        Assert.Equal(2, readNested.Count);
        var email = Assert.IsType<DeserializedValidationError>(readNested[0]);
        Assert.IsAssignableFrom<IValidationError>(email);
        Assert.Equal("Email", email.PropertyName);
        Assert.Equal("VALIDATION.EMAIL", email.Code.Value);
        Assert.Equal(ErrorCategory.Validation, email.Category);
        Assert.Equal(ErrorSeverity.Warning, email.Severity);
        var name = Assert.IsType<DeserializedValidationError>(readNested[1]);
        Assert.Equal("Name", name.PropertyName);

        var critical = Assert.IsType<DeserializedError>(outer[1]);
        Assert.Equal("TEST.CRITICAL", critical.Code.Value);
        Assert.Equal(ErrorSeverity.Critical, critical.Severity);
        Assert.Equal(new Dictionary<string, object?> { ["retryAfter"] = 30L }, critical.Metadata);

        var notFound = Assert.IsType<DeserializedError>(outer[2]);
        Assert.Equal("TEST.NOT_FOUND", notFound.Code.Value);
        Assert.Equal(ErrorCategory.NotFound, notFound.Category);
    }

    [Fact]
    public void Write_WhenCollectionContainsValidationError_WritesPropertyNameAfterSeverity()
    {
        // Arrange
        var result = Result.Failure<int>(ErrorCollection.From(ValidationError.Custom("Age", "Too young")));

        // Act
        var json = JsonSerializer.Serialize(result, DefaultFactoryOptions);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"MULTI.ERROR","message":"Too young","category":"Validation","severity":"Warning","errors":[{"code":"VALIDATION.AGE","message":"Too young","category":"Validation","severity":"Warning","propertyName":"Age"}]}}""", json);
    }

    [Fact]
    public void Read_WhenErrorsArrayEmpty_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"MULTI.ERROR","message":"m","errors":[]}}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions));
    }

    // ── Read defaults and rejections ──────────────────────────────────────

    [Fact]
    public void Read_WhenMessageCategoryAndSeverityMissing_UsesDefaults()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        Assert.True(result.IsFailure);
        var error = Assert.IsType<DeserializedError>(result.Error);
        Assert.Equal("A.B", error.Code.Value);
        Assert.Equal("Unknown error", error.Message);
        Assert.Equal(ErrorCategory.Technical, error.Category);
        Assert.Equal(ErrorSeverity.Error, error.Severity);
        Assert.Empty(error.Metadata);
    }

    [Theory]
    [InlineData("""{"isSuccess":false,"error":{}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":null,"message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"","message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"message":"m","errors":[{"message":"inner"}]}}""")]
    public void Read_WhenCodeIsMissingNullOrEmpty_ThrowsJsonException(string json)
    {
        // Act + Assert: an error without a code does not describe a valid result
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions));
    }

    [Fact]
    public void Read_WhenMetadataHasADuplicateKey_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":{"tenant":"a","tenant":"b"}}}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions));
    }

    [Theory]
    [InlineData("NoSuchCategory")]
    [InlineData("999")]
    public void Read_WhenCategoryUnknownOrUndefined_UsesTechnical(string category)
    {
        // Arrange
        var json = $$$"""{"isSuccess":false,"error":{"code":"A.B","message":"m","category":"{{{category}}}"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Theory]
    [InlineData("NoSuchSeverity", ErrorSeverity.Error)]
    [InlineData("42", ErrorSeverity.Error)]
    [InlineData("warning", ErrorSeverity.Warning)]
    [InlineData("INFORMATION", ErrorSeverity.Information)]
    public void Read_WhenSeverityGiven_ParsesCaseInsensitivelyOrUsesError(string severity, ErrorSeverity expected)
    {
        // Arrange
        var json = $$$"""{"isSuccess":false,"error":{"code":"A.B","message":"m","severity":"{{{severity}}}"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(expected, result.Error.Severity);
    }

    [Fact]
    public void RoundTrip_WhenCategoryIsUndefinedValue_ReadsTechnical()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Failure<int>(new UndefinedCategoryError()), DefaultFactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        Assert.Contains("\"category\":\"999\"", json);
        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Technical, read.Error.Category);
    }

    [Theory]
    [InlineData("""{"isSuccess":false,"error":{"code":"","message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":5,"message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","errors":{}}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","errors":["x"]}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":[]}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":5}}""")]
    public void Read_WhenErrorObjectIsInvalid_ThrowsJsonException(string json)
    {
        // Arrange + Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions));
    }

    // ── Metadata access on a deserialized error ───────────────────────────

    [Fact]
    public void HasMetadata_WhenDeserializedErrorCarriesKey_ReturnsTrue()
    {
        // Arrange
        var read = ReadMetadataError(ErrorMetadata.Create("correlationId", "abc-123"));

        // Act
        var hasKey = read.Error.HasMetadata("correlationId");
        var hasOther = read.Error.HasMetadata("missing");

        // Assert
        Assert.True(hasKey);
        Assert.False(hasOther);
    }

    [Fact]
    public void TryGetMetadata_WhenDeserializedErrorCarriesString_ReturnsIt()
    {
        // Arrange
        var read = ReadMetadataError(ErrorMetadata.Create(new("correlationId", "abc-123"), new("attempt", 3)));

        // Act
        var correlationId = read.Error.TryGetMetadata<string>("correlationId");
        var attemptAsInt = read.Error.TryGetMetadata<int>("attempt");
        var attemptAsLong = read.Error.TryGetMetadata<long>("attempt");

        // Assert
        Assert.Equal("abc-123", correlationId);
        Assert.Equal(0, attemptAsInt);
        Assert.Equal(3L, attemptAsLong);
    }

    private static Result<int> ReadMetadataError(IReadOnlyDictionary<string, object?> metadata)
    {
        var json = JsonSerializer.Serialize(Result.Failure<int>(new MetadataError(metadata)), MetadataOptions);
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);
        Assert.True(read.IsFailure);
        Assert.IsType<DeserializedError>(read.Error);
        return read;
    }

    private sealed record MetadataError(IReadOnlyDictionary<string, object?> Values)
        : Error(ErrorCode.From("TEST.METADATA"), "Metadata error")
    {
        public override IReadOnlyDictionary<string, object?> Metadata => Values;
    }

    private sealed record UndefinedCategoryError()
        : Error(ErrorCode.From("TEST.UNDEFINED"), "Undefined category")
    {
        public override ErrorCategory Category => (ErrorCategory)999;
    }

    private sealed class Money(int amount, string currency)
    {
        public override string ToString() => $"{amount} {currency}";
    }

    [Fact]
    public void Read_WhenErrorObjectHasDuplicateProperty_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","category":"Validation","category":"Technical"}}""";

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DuplicateOptions));
    }

    [Fact]
    public void Read_WhenMetadataPresent_ReturnsImmutableMetadata()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":{"k":1}}}""";

        // Act
        Result<int> result = JsonSerializer.Deserialize<Result<int>>(json, DuplicateOptions);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsAssignableFrom<System.Collections.Immutable.IImmutableDictionary<string, object?>>(result.Error.Metadata);
    }

    private static readonly JsonSerializerOptions DuplicateOptions = new() { Converters = { new MicroKit.Result.Serialization.ResultJsonConverterFactory() } };
}

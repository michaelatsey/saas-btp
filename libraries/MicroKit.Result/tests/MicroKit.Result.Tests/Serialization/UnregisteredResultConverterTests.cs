using System.Text.Json;
using MicroKit.Result.Serialization;
using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Serialization;

// #195 item 6: with no converter registered, Result<T> still writes the documented envelope, never metadata, and
// reading it names the factory to register. A converter registered in the options takes precedence.
public sealed class UnregisteredResultConverterTests
{
    private static readonly JsonSerializerOptions MetadataFactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory(ResultJsonView.Full) },
    };

    private static readonly JsonSerializerOptions ClosedConverterOptions = new()
    {
        Converters = { new ResultJsonConverter<int>() },
    };

    private static readonly JsonSerializerOptions FactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory() },
    };

    [Fact]
    public void Serialize_WhenSuccessWithoutOptions_WritesTheEnvelope()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        Assert.Equal("""{"isSuccess":true,"value":42}""", json);
    }

    [Fact]
    public void Serialize_WhenFailureWithoutOptions_WritesTheErrorEnvelope()
    {
        // Arrange
        var result = Result.Failure<int>(new NotFoundTestError(7));

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        Assert.Equal("""{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound","severity":"Error"}}""", json);
    }

    [Fact]
    public void Serialize_WhenFailureWithMetadataWithoutOptions_OmitsMetadata()
    {
        // Arrange
        var result = Result.Failure<int>(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        Assert.DoesNotContain("metadata", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("retryAfter", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.Equal("TEST.CRITICAL", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Serialize_WhenResultIsAPropertyWithoutOptions_WritesTheEnvelope()
    {
        // Arrange
        var envelope = new Envelope(Result.Success("Pen"));

        // Act
        var json = JsonSerializer.Serialize(envelope);

        // Assert
        Assert.Equal("""{"Outcome":{"isSuccess":true,"value":"Pen"}}""", json);
    }

    [Fact]
    public void Serialize_WhenDefaultWithoutOptions_ThrowsJsonException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Deserialize_WithoutOptions_ThrowsJsonExceptionNamingTheFactory()
    {
        // Arrange: #198 review, what it wrote must fail on read as an unreadable payload does, a JsonException,
        // which a reader that catches JsonException (a cache, a request binder) handles as an unreadable entry
        const string json = """{"isSuccess":true,"value":42}""";

        // Act
        var exception = Record.Exception(() => JsonSerializer.Deserialize<Result<int>>(json));

        // Assert
        Assert.IsType<JsonException>(exception);
        Assert.Contains(nameof(ResultJsonConverterFactory), exception.Message);
    }

    [Fact]
    public void RoundTrip_WithoutOptions_WhenWrittenThenRead_FailsWithJsonException()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Success(42));

        // Act
        var exception = Record.Exception(() => JsonSerializer.Deserialize<Result<int>>(json));

        // Assert: never a NotSupportedException
        Assert.IsType<JsonException>(exception);
    }

    [Fact]
    public void Serialize_WhenFactoryWithMetadataIsRegistered_TakesPrecedenceAndWritesMetadata()
    {
        // Arrange
        var result = Result.Failure<int>(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, MetadataFactoryOptions);

        // Assert
        using var document = JsonDocument.Parse(json);
        Assert.Equal(30, document.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("retryAfter").GetInt32());
    }

    [Fact]
    public void Deserialize_WhenFactoryIsRegistered_ReadsTheEnvelope()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":42}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, FactoryOptions);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Theory]
    [InlineData(ErrorCategory.Technical)]
    [InlineData(ErrorCategory.External)]
    [InlineData(ErrorCategory.Unavailable)]
    [InlineData(ErrorCategory.Timeout)]
    [InlineData(ErrorCategory.NotSupported)]
    [InlineData((ErrorCategory)999)]
    public void Serialize_WhenSystemFaultWithoutOptions_WritesAGenericMessage(ErrorCategory category)
    {
        // Arrange: a fault of the system may describe it; the envelope reaches clients by default
        var result = Result.Failure<int>(new FaultError(category, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        Assert.DoesNotContain("db-7", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("The server could not complete the request.", error.GetProperty("message").GetString());
        Assert.Equal("TEST.FAULT", error.GetProperty("code").GetString());
    }

    [Fact]
    public void Serialize_WhenCollectionHoldsASystemFaultWithoutOptions_RedactsItAndNamesOnlyTheFaults()
    {
        // Arrange
        var errors = ErrorCollection.From(
            new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"),
            new NotFoundTestError(7));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert: the collection is Technical, so its message is redacted, and it names only its faults
        Assert.DoesNotContain("db-7", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Item 7 not found", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("The server could not complete the request.", error.GetProperty("message").GetString());
        var inner = error.GetProperty("errors");
        Assert.Equal(1, inner.GetArrayLength());
        Assert.Equal("The server could not complete the request.", inner[0].GetProperty("message").GetString());
    }

    [Fact]
    public void Serialize_WhenSystemFaultWithDefaultFactoryRegistered_WritesAGenericMessage()
    {
        // Arrange: the factory a host registers for its endpoints writes what a client may read
        var result = Result.Failure<int>(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        Assert.DoesNotContain("db-7", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The server could not complete the request.", json);
    }

    [Fact]
    public void Serialize_WhenSystemFaultWithMetadataFactoryRegistered_KeepsItsMessage()
    {
        // Arrange: ResultJsonView.Full is for in-process storage, never a response: it writes everything
        var result = Result.Failure<int>(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, MetadataFactoryOptions);

        // Assert
        Assert.Contains("Host db-7 refused the login of sa", json);
    }

    [Fact]
    public void Serialize_WhenSystemFaultWithDefaultClosedConverterRegistered_WritesAGenericMessage()
    {
        // Arrange
        var result = Result.Failure<int>(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, ClosedConverterOptions);

        // Assert
        Assert.DoesNotContain("db-7", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_WhenJsonIsNullWithoutOptions_ThrowsAndNeverReturnsDefault()
    {
        // Arrange
        const string json = "null";

        // Act
        var thrown = Record.Exception(() => JsonSerializer.Deserialize<Result<int>>(json));

        // Assert: never an uninitialized result read as a value
        Assert.IsType<JsonException>(thrown);
    }

    [Fact]
    public void Serialize_WhenUnauthorizedCollectionHoldsAValidationErrorWithoutOptions_WritesNoValidationDetail()
    {
        // Arrange: ToHttpResult answers 401 and names neither the field nor its message
        var errors = ErrorCollection.From(new AccessError(ErrorCategory.Unauthorized), ValidationError.Required("Email"));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        Assert.DoesNotContain("Email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("propertyName", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("Unauthorized", error.GetProperty("category").GetString());
        Assert.Equal("Sign in first.", error.GetProperty("message").GetString());
        var inner = error.GetProperty("errors");
        Assert.Equal(1, inner.GetArrayLength());
        Assert.Equal("TEST.ACCESS", inner[0].GetProperty("code").GetString());
    }

    [Fact]
    public void Serialize_WhenTechnicalCollectionHoldsOtherErrorsWithoutOptions_WritesOnlyItsFaults()
    {
        // Arrange
        var errors = ErrorCollection.From(
            ValidationError.Required("Email"),
            new NotFoundTestError(7),
            new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        Assert.DoesNotContain("Email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Item 7 not found", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db-7", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        var inner = document.RootElement.GetProperty("error").GetProperty("errors");
        Assert.Equal(1, inner.GetArrayLength());
        Assert.Equal("TEST.FAULT", inner[0].GetProperty("code").GetString());
    }

    [Fact]
    public void Serialize_WhenNestedCollectionsWithoutOptions_FlattensTheErrorsOfTheCategory()
    {
        // Arrange
        var errors = ErrorCollection.From(
            ErrorCollection.From(new AccessError(ErrorCategory.Unauthorized), ValidationError.Required("Email")),
            new AccessError(ErrorCategory.Unauthorized));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        using var document = JsonDocument.Parse(json);
        var inner = document.RootElement.GetProperty("error").GetProperty("errors");
        Assert.Equal(["TEST.ACCESS", "TEST.ACCESS"], inner.EnumerateArray().Select(e => e.GetProperty("code").GetString()));
        Assert.DoesNotContain(inner.EnumerateArray(), e => e.TryGetProperty("errors", out _));
    }

    [Fact]
    public void Serialize_WhenValidationCollectionWithoutOptions_KeepsEveryFieldError()
    {
        // Arrange
        var errors = ErrorCollection.From(ValidationError.Required("Email"), ValidationError.Required("Name"));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        Assert.Contains("\"propertyName\":\"Email\"", json);
        Assert.Contains("\"propertyName\":\"Name\"", json);
    }

    [Fact]
    public void Serialize_WhenValidationErrorHasAnotherCategoryWithoutOptions_WritesNoPropertyName()
    {
        // Arrange: a third-party IValidationError whose category is not an input category
        var result = Result.Failure<int>(new ForeignValidationError());

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        Assert.DoesNotContain("propertyName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_WhenUnauthorizedCollectionWithMetadataFactory_KeepsEveryError()
    {
        // Arrange: ResultJsonView.Full is for in-process storage, which keeps the whole collection
        var errors = ErrorCollection.From(new AccessError(ErrorCategory.Unauthorized), ValidationError.Required("Email"));

        // Act
        var json = JsonSerializer.Serialize(Result.Failure<int>(errors), MetadataFactoryOptions);

        // Assert
        Assert.Contains("\"propertyName\":\"Email\"", json);
    }

    private sealed record AccessError(ErrorCategory Kind) : Error(ErrorCode.From("TEST.ACCESS"), "Sign in first.")
    {
        public override ErrorCategory Category => Kind;
    }

    private sealed record ForeignValidationError() : Error(ErrorCode.From("TEST.FOREIGN"), "Not found."), IValidationError
    {
        public override ErrorCategory Category => ErrorCategory.NotFound;

        public string PropertyName => "Secret";
    }

    private sealed record FaultError(ErrorCategory Kind, string Text) : Error(ErrorCode.From("TEST.FAULT"), Text)
    {
        public override ErrorCategory Category => Kind;
    }

    private sealed record Envelope(Result<string> Outcome);
}

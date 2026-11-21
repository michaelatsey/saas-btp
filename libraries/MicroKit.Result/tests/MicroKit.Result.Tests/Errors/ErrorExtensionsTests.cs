using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Errors;

public sealed class ErrorExtensionsTests
{
    [Theory]
    [InlineData(ErrorCategory.NotFound, true)]
    [InlineData(ErrorCategory.Technical, false)]
    public void IsCategory_WhenCalled_ComparesCategory(ErrorCategory category, bool expected)
    {
        // Arrange
        IError error = new NotFoundTestError(1);

        // Act
        var matches = error.IsCategory(category);

        // Assert
        Assert.Equal(expected, matches);
    }

    [Theory]
    [InlineData(ErrorSeverity.Critical, true, false, false)]
    [InlineData(ErrorSeverity.Error, false, false, false)]
    [InlineData(ErrorSeverity.Warning, false, true, false)]
    [InlineData(ErrorSeverity.Information, false, false, true)]
    public void SeverityPredicates_WhenCalled_MatchOnlyTheirSeverity(
        ErrorSeverity severity,
        bool critical,
        bool warning,
        bool information)
    {
        // Arrange
        IError error = new SeverityError(severity);

        // Act
        var isCritical = error.IsCritical();
        var isWarning = error.IsWarning();
        var isInformation = error.IsInformation();

        // Assert
        Assert.Equal(critical, isCritical);
        Assert.Equal(warning, isWarning);
        Assert.Equal(information, isInformation);
    }

    [Fact]
    public void IsWarning_WhenValidationError_ReturnsTrue()
    {
        // Arrange
        IError error = ValidationError.Required("Name");

        // Act
        var isWarning = error.IsWarning();

        // Assert
        Assert.True(isWarning);
    }

    [Theory]
    [InlineData("retryAfter", true)]
    [InlineData("missing", false)]
    [InlineData("RETRYAFTER", false)]
    public void HasMetadata_WhenCalled_ReportsWhetherKeyExists(string key, bool expected)
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var has = error.HasMetadata(key);

        // Assert
        Assert.Equal(expected, has);
    }

    [Fact]
    public void TryGetMetadata_WhenKeyPresentWithMatchingType_ReturnsTypedValue()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var retryAfter = error.TryGetMetadata<int>("retryAfter");

        // Assert
        Assert.Equal(30, retryAfter);
    }

    [Fact]
    public void TryGetMetadata_WhenKeyPresentWithReferenceTypeMismatch_ReturnsNull()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var retryAfter = error.TryGetMetadata<string>("retryAfter");

        // Assert
        Assert.Null(retryAfter);
    }

    [Fact]
    public void TryGetMetadata_WhenKeyPresentWithValueTypeMismatch_ReturnsDefault()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var retryAfter = error.TryGetMetadata<long>("retryAfter");

        // Assert
        Assert.Equal(0L, retryAfter);
    }

    [Fact]
    public void TryGetMetadata_WhenKeyAbsent_ReturnsDefault()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var missingNumber = error.TryGetMetadata<int>("missing");
        var missingText = error.TryGetMetadata<string>("missing");

        // Assert
        Assert.Equal(0, missingNumber);
        Assert.Null(missingText);
    }

    [Fact]
    public void TryGetMetadata_WhenKeyPresentWithNullValue_ReturnsDefault()
    {
        // Arrange
        IError error = new MetadataError(ErrorMetadata.Create("correlationId", null));

        // Act
        var value = error.TryGetMetadata<string>("correlationId");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void ToException_WhenCalled_ReturnsResultExceptionWithCodeAndMessage()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var exception = error.ToException();

        // Assert
        Assert.IsType<ResultException>(exception);
        Assert.Equal("[TEST.CRITICAL] Critical error", exception.Message);
        Assert.Single(exception.Errors);
        Assert.Same(error, exception.Errors[0]);
    }

    [Fact]
    public void IsCategory_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => error!.IsCategory(ErrorCategory.NotFound));

        // Assert
        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void IsCritical_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => error!.IsCritical());

        // Assert
        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void IsWarning_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => error!.IsWarning());

        // Assert
        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void IsInformation_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => error!.IsInformation());

        // Assert
        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void HasMetadata_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => error!.HasMetadata("key"));

        // Assert
        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void HasMetadata_WhenKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => error.HasMetadata(null!));

        // Assert
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void TryGetMetadata_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => error!.TryGetMetadata<int>("key"));

        // Assert
        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void TryGetMetadata_WhenKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => error.TryGetMetadata<int>(null!));

        // Assert
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ToException_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => error!.ToException());

        // Assert
        Assert.Equal("error", exception.ParamName);
    }

    private sealed record SeverityError(ErrorSeverity Level)
        : Error(ErrorCode.From("TEST.SEVERITY"), "Severity error")
    {
        public override ErrorSeverity Severity => Level;
    }

    private sealed record MetadataError(IReadOnlyDictionary<string, object?> Values)
        : Error(ErrorCode.From("TEST.METADATA"), "Metadata error")
    {
        public override IReadOnlyDictionary<string, object?> Metadata => Values;
    }
}

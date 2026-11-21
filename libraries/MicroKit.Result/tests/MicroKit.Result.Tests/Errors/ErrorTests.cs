using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Errors;

public sealed class ErrorTests
{
    [Fact]
    public void Category_WhenNotOverridden_IsTechnical()
    {
        // Arrange
        var error = new TestError();

        // Act
        var category = error.Category;

        // Assert
        Assert.Equal(ErrorCategory.Technical, category);
    }

    [Fact]
    public void Severity_WhenNotOverridden_IsError()
    {
        // Arrange
        var error = new TestError();

        // Act
        var severity = error.Severity;

        // Assert
        Assert.Equal(ErrorSeverity.Error, severity);
    }

    [Fact]
    public void Metadata_WhenNotOverridden_IsEmpty()
    {
        // Arrange
        var error = new TestError();

        // Act
        var metadata = error.Metadata;

        // Assert
        Assert.Empty(metadata);
        Assert.Same(ErrorMetadata.Empty, metadata);
    }

    [Fact]
    public void Constructor_WhenCalled_ExposesCodeAndMessage()
    {
        // Arrange
        var code = ErrorCode.From("ORDER.PAYMENT.DECLINED");

        // Act
        var error = new ConfigurableError(code, "Payment declined");

        // Assert
        Assert.Equal(code, error.Code);
        Assert.Equal("Payment declined", error.Message);
    }

    [Fact]
    public void Category_WhenOverridden_ReturnsOverride()
    {
        // Arrange
        var error = new NotFoundTestError(7);

        // Act
        var category = error.Category;

        // Assert
        Assert.Equal(ErrorCategory.NotFound, category);
    }

    [Fact]
    public void Overrides_WhenCategorySeverityAndMetadataOverridden_ReturnsOverrides()
    {
        // Arrange
        var error = new CriticalTestError();

        // Act
        var category = error.Category;
        var severity = error.Severity;
        var metadata = error.Metadata;

        // Assert
        Assert.Equal(ErrorCategory.Unavailable, category);
        Assert.Equal(ErrorSeverity.Critical, severity);
        Assert.Single(metadata);
        Assert.Equal(30, metadata["retryAfter"]);
    }

    [Fact]
    public void Equals_WhenSameTypeAndValues_ReturnsTrue()
    {
        // Arrange
        var first = new TestError("same");
        var second = new TestError("same");

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.True(equal);
        Assert.True((first == second));
        Assert.Equal(second.GetHashCode(), first.GetHashCode());
    }

    [Fact]
    public void Equals_WhenDifferentCode_ReturnsFalse()
    {
        // Arrange
        var first = new ConfigurableError(ErrorCode.From("A.CODE"), "message");
        var second = new ConfigurableError(ErrorCode.From("B.CODE"), "message");

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.False(equal);
        Assert.True((first != second));
    }

    [Fact]
    public void Equals_WhenDifferentMessage_ReturnsFalse()
    {
        // Arrange
        var first = new ConfigurableError(ErrorCode.From("A.CODE"), "first");
        var second = new ConfigurableError(ErrorCode.From("A.CODE"), "second");

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.False(equal);
    }

    [Fact]
    public void Equals_WhenDifferentTypeWithSameCodeAndMessage_ReturnsFalse()
    {
        // Arrange
        var code = ErrorCode.From("A.CODE");
        Error first = new ConfigurableError(code, "message");
        Error second = new OtherConfigurableError(code, "message");

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.False(equal);
    }

    [Fact]
    public void With_WhenMessageChanged_KeepsCodeAndChangesMessage()
    {
        // Arrange
        var original = new ConfigurableError(ErrorCode.From("A.CODE"), "before");

        // Act
        var changed = original with { Message = "after" };

        // Assert
        Assert.Equal(original.Code, changed.Code);
        Assert.Equal("after", changed.Message);
        Assert.Equal("before", original.Message);
    }

    private sealed record ConfigurableError(ErrorCode Code, string Message) : Error(Code, Message);

    private sealed record OtherConfigurableError(ErrorCode Code, string Message) : Error(Code, Message);
}

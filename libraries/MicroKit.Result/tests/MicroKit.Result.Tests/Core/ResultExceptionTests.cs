using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Core;

public sealed class ResultExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessageAndEmptyErrors()
    {
        // Act
        var exception = new ResultException("message");

        // Assert
        Assert.Equal("message", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.NotNull(exception.Errors);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInnerExceptionAndEmptyErrors()
    {
        // Arrange
        var inner = new FormatException("inner");

        // Act
        var exception = new ResultException("message", inner);

        // Assert
        Assert.Equal("message", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.NotNull(exception.Errors);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void Constructor_WithErrors_SetsErrors()
    {
        // Arrange
        var first = new TestError("a");
        var second = new OtherError();

        // Act
        var exception = new ResultException("message", [first, second]);

        // Assert
        Assert.Equal("message", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(2, exception.Errors.Count);
        Assert.Same(first, exception.Errors[0]);
        Assert.Same(second, exception.Errors[1]);
    }

    [Fact]
    public void Constructor_WithNullErrors_SetsEmptyErrors()
    {
        // Act
        var exception = new ResultException("message", (IReadOnlyList<IError>)null!);

        // Assert
        Assert.NotNull(exception.Errors);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void Constructor_WithInnerExceptionAndErrors_SetsBoth()
    {
        // Arrange
        var inner = new FormatException("inner");
        var error = new TestError();

        // Act
        var exception = new ResultException("message", inner, [error]);

        // Assert
        Assert.Equal("message", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Same(error, Assert.Single(exception.Errors));
    }

    [Fact]
    public void Constructor_WithInnerExceptionAndNullErrors_SetsEmptyErrors()
    {
        // Act
        var exception = new ResultException("message", new FormatException("inner"), null!);

        // Assert
        Assert.NotNull(exception.Errors);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void ResultException_WhenCreated_IsInvalidOperationException()
    {
        // Act
        var exception = new ResultException("message");

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    [Fact]
    public void Value_WhenFailure_ThrowsWithErrorInMessageAndErrors()
    {
        // Arrange
        var error = new NotFoundTestError(7);
        Result<int> result = Result.Failure<int>(error);

        // Act
        var exception = Assert.Throws<ResultException>(() => result.Value);

        // Assert
        Assert.Contains("[TEST.NOT_FOUND]", exception.Message);
        Assert.Contains("Item 7 not found", exception.Message);
        Assert.Same(error, Assert.Single(exception.Errors));
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Value_WhenDefault_ThrowsWithEmptyErrors()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var exception = Assert.Throws<ResultException>(() => result.Value);

        // Assert
        Assert.Equal("Result was not initialized. Use Result.Success() or Result.Failure() factory methods.", exception.Message);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void Error_WhenSuccess_ThrowsWithEmptyErrors()
    {
        // Arrange
        Result result = Result.Success();

        // Act
        var exception = Assert.Throws<ResultException>(() => result.Error);

        // Assert
        Assert.Empty(exception.Errors);
    }
}

using System.Text.Json;
using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Core;

public sealed class ExceptionErrorTests
{
    private const string SecretMessage = "secret connection string";

    [Fact]
    public void Constructor_WhenExceptionIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => new ExceptionError(null!));
    }

    [Fact]
    public void Constructor_WhenCalled_KeepsTheExceptionInProcessBehindAGenericMessage()
    {
        // Arrange
        var exception = new InvalidOperationException(SecretMessage);

        // Act
        var error = new ExceptionError(exception);

        // Assert
        Assert.Same(exception, error.Exception);
        Assert.Equal(ErrorCode.Exception, error.Code);
        Assert.Equal("An unexpected error occurred.", error.Message);
        Assert.Equal(ErrorCategory.Technical, error.Category);
        Assert.Equal(ErrorSeverity.Error, error.Severity);
        Assert.Empty(error.Metadata);
    }

    [Fact]
    public void Try_WhenFuncThrows_ErrorCarriesTheCaughtException()
    {
        // Arrange
        var exception = new InvalidOperationException(SecretMessage);

        // Act
        Result<int> result = Result<int>.Try(() => throw exception);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(exception, Assert.IsType<ExceptionError>(result.Error).Exception);
    }

    [Fact]
    public void Serialize_WhenSerializedAsItsOwnType_DoesNotEmitTheException()
    {
        // Arrange
        var error = new ExceptionError(new InvalidOperationException(SecretMessage));

        // Act
        string json = JsonSerializer.Serialize(error);

        // Assert
        Assert.DoesNotContain(SecretMessage, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Exception\":", json);
    }

    [Fact]
    public void ToString_WhenCalled_DoesNotPrintTheException()
    {
        // Arrange
        var error = new ExceptionError(new InvalidOperationException(SecretMessage));

        // Act
        string text = error.ToString();

        // Assert
        Assert.DoesNotContain(SecretMessage, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(InvalidOperationException), text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXCEPTION", text);
    }

    [Fact]
    public void Try_WhenMapperReturnsNull_KeepsTheCaughtExceptionAsInner()
    {
        // Arrange
        var caught = new InvalidOperationException(SecretMessage);

        // Act
        var thrown = Assert.Throws<ArgumentNullException>(() => Result<int>.Try(() => throw caught, _ => null!));

        // Assert
        Assert.Same(caught, thrown.InnerException);
    }
}

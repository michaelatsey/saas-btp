using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Core;

// MicroKit ADR-RESULT-001 D4 (michaelatsey/microkit@a12097b): an Error or an ErrorCollection converts implicitly to a failed Result<T> or Result.
// Result<Error> r = error; and Result<ErrorCollection> r = errors; do not compile (CS0457, ambiguous between the
// conversion from T and the conversion from the error type): they are deliberately not written here.
public sealed class ImplicitErrorConversionTests
{
    // ── Result<T> ─────────────────────────────────────────────────────────

    [Fact]
    public void ImplicitFromError_ToResultOfT_ReturnsFailureWithSameError()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result<int> result = error;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ImplicitFromCategorizedError_ToResultOfT_ReturnsFailureWithSameError()
    {
        // Arrange
        var error = Error.NotFound(ErrorCode.From("USER.NOT_FOUND"), "User not found.");

        // Act
        Result<User> result = error;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
        Assert.Equal(ErrorCategory.NotFound, result.Error.Category);
    }

    [Fact]
    public void ImplicitFromErrorCollection_ToResultOfT_ReturnsFailureWithSameCollection()
    {
        // Arrange
        var errors = ErrorCollection.From(new TestError("a"), new OtherError());

        // Act
        Result<int> result = errors;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(errors, result.Error);
    }

    [Fact]
    public void ImplicitFromError_InReturnStatementOfResultOfT_ReturnsFailure()
    {
        // Act
        var result = FindUser(7);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(7, Assert.IsType<NotFoundTestError>(result.Error).Id);
    }

    [Fact]
    public void ImplicitFromValue_InReturnStatementOfResultOfT_ReturnsSuccess()
    {
        // Act
        var result = FindUser(1);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Id);
    }

    [Fact]
    public void ImplicitFromErrorCollection_InReturnStatementOfResultOfT_ReturnsFailure()
    {
        // Act
        var result = ValidateUser(string.Empty, -1);

        // Assert
        Assert.True(result.IsFailure);
        var errors = Assert.IsType<ErrorCollection>(result.Error);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void ImplicitFromNullError_ToResultOfT_ThrowsArgumentNullException()
    {
        // Arrange
        Error error = null!;

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() =>
        {
            Result<int> result = error;
            return result;
        });
        Assert.Equal("error", thrown.ParamName);
    }

    [Fact]
    public void ImplicitFromNullErrorCollection_ToResultOfT_ThrowsArgumentNullException()
    {
        // Arrange
        ErrorCollection errors = null!;

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() =>
        {
            Result<int> result = errors;
            return result;
        });
        Assert.Equal("error", thrown.ParamName);
    }

    [Fact]
    public void ImplicitFromError_ToResultOfObject_ReturnsFailure()
    {
        // Arrange
        Error error = new TestError();

        // Act
        Result<object> result = error;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ImplicitFromErrorSubtype_ToResultOfObject_ReturnsFailure()
    {
        // Arrange
        var error = new NotFoundTestError(3);

        // Act
        Result<object> result = error;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ImplicitFromErrorCollection_ToResultOfObject_ReturnsFailure()
    {
        // Arrange
        var errors = ErrorCollection.From(new TestError());

        // Act
        Result<object> result = errors;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(errors, result.Error);
    }

    [Fact]
    public void ImplicitFromError_ToResultOfIError_ReturnsFailure()
    {
        // Arrange
        Error error = new TestError();

        // Act
        Result<IError> result = error;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ImplicitFromErrorCollection_ToResultOfReadOnlyListOfIError_ReturnsFailure()
    {
        // Arrange
        var errors = ErrorCollection.From(new TestError(), new OtherError());

        // Act
        Result<IReadOnlyList<IError>> result = errors;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(errors, result.Error);
    }

    [Fact]
    public void ImplicitFromErrorSubtype_ToResultOfThatSubtype_ReturnsSuccessHoldingTheError()
    {
        // Arrange
        var error = new NotFoundTestError(1);

        // Act — the conversion from T is the exact match and wins (MicroKit ADR-RESULT-001 D4 (michaelatsey/microkit@a12097b), documented subtype case)
        Result<NotFoundTestError> result = error;

        // Assert
        Assert.False(result.IsFailure);
        Assert.True(result.IsSuccess);
        Assert.Same(error, result.Value);
    }

    // ── Result ────────────────────────────────────────────────────────────

    [Fact]
    public void ImplicitFromError_ToResult_ReturnsFailureWithSameError()
    {
        // Arrange
        var error = new OtherError();

        // Act
        Result result = error;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ImplicitFromErrorCollection_ToResult_ReturnsFailureWithSameCollection()
    {
        // Arrange
        var errors = ErrorCollection.From(new TestError(), new OtherError());

        // Act
        Result result = errors;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(errors, result.Error);
    }

    [Fact]
    public void ImplicitFromError_InReturnStatementOfResult_ReturnsFailure()
    {
        // Act
        var result = DeleteUser(7);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Forbidden, result.Error.Category);
    }

    [Fact]
    public void ImplicitFromErrorCollection_InReturnStatementOfResult_ReturnsFailure()
    {
        // Act
        var result = CheckUser(string.Empty, -1);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(2, Assert.IsType<ErrorCollection>(result.Error).Count);
    }

    [Fact]
    public void ImplicitFromError_InReturnStatementOfResult_WhenNoError_ReturnsSuccess()
    {
        // Act
        var result = DeleteUser(1);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ImplicitFromNullError_ToResult_ThrowsArgumentNullException()
    {
        // Arrange
        Error error = null!;

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() =>
        {
            Result result = error;
            return result;
        });
        Assert.Equal("error", thrown.ParamName);
    }

    [Fact]
    public void ImplicitFromNullErrorCollection_ToResult_ThrowsArgumentNullException()
    {
        // Arrange
        ErrorCollection errors = null!;

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() =>
        {
            Result result = errors;
            return result;
        });
        Assert.Equal("error", thrown.ParamName);
    }

    private static Result<User> FindUser(int id)
    {
        if (id != 1)
            return new NotFoundTestError(id);
        return new User(id, "Ada");
    }

    private static Result<User> ValidateUser(string name, int id)
    {
        var validation = new List<IError>();
        if (string.IsNullOrEmpty(name))
            validation.Add(ValidationError.Required("Name"));
        if (id < 0)
            validation.Add(new ValidationError("Id", "Id must be non-negative."));
        if (validation.Count > 0)
            return ErrorCollection.From(validation);
        return new User(id, name);
    }

    private static Result DeleteUser(int id)
    {
        if (id != 1)
            return Error.Forbidden(ErrorCode.From("USER.DELETE.FORBIDDEN"), "The user may not be deleted.");
        return Result.Success();
    }

    private static Result CheckUser(string name, int id)
    {
        var validation = new List<IError>();
        if (string.IsNullOrEmpty(name))
            validation.Add(ValidationError.Required("Name"));
        if (id < 0)
            validation.Add(new ValidationError("Id", "Id must be non-negative."));
        if (validation.Count > 0)
            return ErrorCollection.From(validation);
        return Result.Success();
    }

    private sealed record User(int Id, string Name);
}

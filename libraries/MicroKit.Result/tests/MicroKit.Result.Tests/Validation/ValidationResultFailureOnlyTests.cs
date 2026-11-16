namespace MicroKit.Result.Tests.Validation;

// MicroKit ADR-RESULT-001 D10 (michaelatsey/microkit@a12097b): ToResult<T>() returns a failure-only Result<T>, and throws on a valid ValidationResult.
public sealed class ValidationResultFailureOnlyTests
{
    [Fact]
    public void ToResultOfT_WhenOneError_ReturnsFailureWithCollectionOfThatError()
    {
        // Arrange
        var validation = new ValidationResult().AddError("Name", "Name is required.");

        // Act
        Result<Order> result = validation.ToResult<Order>();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        var errors = Assert.IsType<ErrorCollection>(result.Error);
        Assert.Single(errors);
        Assert.Same(validation.Errors[0], errors[0]);
    }

    [Fact]
    public void ToResultOfT_WhenSeveralErrors_ReturnsFailureWithAllErrorsInOrder()
    {
        // Arrange
        var custom = ValidationError.Required("Email");
        var validation = new ValidationResult()
            .AddError("Name", "Name is required.")
            .AddError(custom)
            .AddErrorIf(true, "Age", "Age must be non-negative.")
            .AddErrorIf(false, "Skipped", "Never added.");

        // Act
        Result<Order> result = validation.ToResult<Order>();

        // Assert
        Assert.True(result.IsFailure);
        var errors = Assert.IsType<ErrorCollection>(result.Error);
        Assert.Equal(3, errors.Count);
        Assert.Equal(validation.Errors.Cast<IError>().ToArray(), errors);
        Assert.Same(custom, errors[1]);
        Assert.Equal(["Name", "Email", "Age"], errors.OfType<IValidationError>().Select(e => e.PropertyName));
        Assert.Equal(ErrorCategory.Validation, errors.Category);
    }

    [Fact]
    public void ToResultOfT_WhenErrors_MatchesFailureOfToResultWithValue()
    {
        // Arrange
        var validation = new ValidationResult().AddError("Name", "Name is required.").AddError("Age", "Too young.");

        // Act
        Result<Order> failureOnly = validation.ToResult<Order>();
        Result<Order> withValue = validation.ToResult(new Order(1));

        // Assert
        Assert.True(failureOnly.IsFailure);
        Assert.True(withValue.IsFailure);
        Assert.Equal(withValue.Error.Code, failureOnly.Error.Code);
        Assert.Equal(withValue.Error.Message, failureOnly.Error.Message);
        var failureOnlyErrors = Assert.IsType<ErrorCollection>(failureOnly.Error);
        var withValueErrors = Assert.IsType<ErrorCollection>(withValue.Error);
        // The same errors in the same order, compared as sequences (ErrorCollection is an IReadOnlyList<IError>).
        Assert.Equal<IError>(withValueErrors.ToList(), failureOnlyErrors.ToList());
    }

    [Fact]
    public void ToResultOfT_WhenUsedForEarlyReturn_PropagatesFailure()
    {
        // Act
        Result<Order> result = CreateOrder(quantity: 0);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Single(Assert.IsType<ErrorCollection>(result.Error));
    }

    [Fact]
    public void ToResultOfT_WhenValid_ThrowsInvalidOperationException()
    {
        // Arrange
        var validation = new ValidationResult();

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => validation.ToResult<Order>());
        Assert.IsType<InvalidOperationException>(thrown);
    }

    [Fact]
    public void ToResultOfT_WhenOnlyFalseConditionsAdded_ThrowsInvalidOperationException()
    {
        // Arrange
        var validation = new ValidationResult().AddErrorIf(false, "Name", "Name is required.");

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => validation.ToResult<int>());
    }

    private static Result<Order> CreateOrder(int quantity)
    {
        var validation = new ValidationResult().AddErrorIf(quantity <= 0, "Quantity", "Quantity must be positive.");
        if (!validation.IsValid)
            return validation.ToResult<Order>();
        return Result.Success(new Order(quantity));
    }

    private sealed record Order(int Quantity);
}

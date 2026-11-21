namespace MicroKit.Result.Tests.Validation;

public sealed class ValidationResultTests
{
    [Fact]
    public void Constructor_WhenNew_IsValidWithNoErrors()
    {
        // Arrange + Act
        var validation = new ValidationResult();

        // Assert
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
    }

    [Fact]
    public void AddError_WhenPropertyAndMessageGiven_AddsValidationError()
    {
        // Arrange
        var validation = new ValidationResult();

        // Act
        var returned = validation.AddError("Name", "Name is required.");

        // Assert
        Assert.Same(validation, returned);
        Assert.False(validation.IsValid);
        Assert.Single(validation.Errors);
        var error = Assert.IsType<ValidationError>(validation.Errors[0]);
        Assert.Equal("Name", error.PropertyName);
        Assert.Equal("Name is required.", error.Message);
    }

    [Fact]
    public void AddError_WhenErrorGiven_AddsSameInstance()
    {
        // Arrange
        var validation = new ValidationResult();
        var error = ValidationError.InvalidEmail();

        // Act
        var returned = validation.AddError(error);

        // Assert
        Assert.Same(validation, returned);
        Assert.False(validation.IsValid);
        Assert.Single(validation.Errors);
        Assert.Same(error, validation.Errors[0]);
    }

    [Fact]
    public void AddError_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var validation = new ValidationResult();
        IValidationError? error = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => validation.AddError(error!));

        // Assert
        Assert.Equal("error", exception.ParamName);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void AddError_WhenPropertyNameIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var validation = new ValidationResult();
        string? propertyName = null;

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => validation.AddError(propertyName!, "message"));
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void AddErrorIf_WhenConditionTrue_AddsError()
    {
        // Arrange
        var validation = new ValidationResult();

        // Act
        var returned = validation.AddErrorIf(true, "Age", "Age must be non-negative.");

        // Assert
        Assert.Same(validation, returned);
        Assert.False(validation.IsValid);
        Assert.Single(validation.Errors);
        Assert.Equal("Age", validation.Errors[0].PropertyName);
        Assert.Equal("Age must be non-negative.", validation.Errors[0].Message);
    }

    [Fact]
    public void AddErrorIf_WhenConditionFalse_AddsNothing()
    {
        // Arrange
        var validation = new ValidationResult();

        // Act
        var returned = validation.AddErrorIf(false, "Age", "Age must be non-negative.");

        // Assert
        Assert.Same(validation, returned);
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
    }

    [Fact]
    public void ToResult_WhenValid_ReturnsSuccess()
    {
        // Arrange
        var validation = new ValidationResult().AddErrorIf(false, "Name", "Name is required.");

        // Act
        var result = validation.ToResult();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void ToResult_WhenInvalid_ReturnsFailureWithAllErrorsInOrder()
    {
        // Arrange
        var email = ValidationError.InvalidEmail();
        var validation = new ValidationResult()
            .AddError("Name", "Name is required.")
            .AddError(email)
            .AddErrorIf(true, "Age", "Age must be non-negative.");

        // Act
        var result = validation.ToResult();

        // Assert
        Assert.True(result.IsFailure);
        var errors = Assert.IsType<ErrorCollection>(result.Error);
        Assert.Equal(3, errors.Count);
        Assert.Same(validation.Errors[0], errors[0]);
        Assert.Same(email, errors[1]);
        Assert.Same(validation.Errors[2], errors[2]);
        Assert.Equal(ErrorCategory.Validation, errors.Category);
    }

    [Fact]
    public void ToResultOfT_WhenValid_ReturnsSuccessWithValue()
    {
        // Arrange
        var validation = new ValidationResult();
        var value = new Registration("ada@example.com");

        // Act
        var result = validation.ToResult(value);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(value, result.Value);
    }

    [Fact]
    public void ToResultOfT_WhenInvalid_ReturnsFailureWithAllErrorsInOrder()
    {
        // Arrange
        var validation = new ValidationResult()
            .AddError("Name", "Name is required.")
            .AddError("Email", "Email is required.");

        // Act
        var result = validation.ToResult(new Registration("ada@example.com"));

        // Assert
        Assert.True(result.IsFailure);
        var errors = Assert.IsType<ErrorCollection>(result.Error);
        Assert.Equal(2, errors.Count);
        Assert.Same(validation.Errors[0], errors[0]);
        Assert.Same(validation.Errors[1], errors[1]);
    }

    [Fact]
    public void ToResult_WhenCalledTwice_ReturnsIndependentCollections()
    {
        // Arrange
        var validation = new ValidationResult().AddError("Name", "Name is required.");
        var first = validation.ToResult();

        // Act
        validation.AddError("Email", "Email is required.");
        var second = validation.ToResult();

        // Assert
        Assert.True(first.IsFailure);
        Assert.True(second.IsFailure);
        Assert.Single(Assert.IsType<ErrorCollection>(first.Error));
        Assert.Equal(2, Assert.IsType<ErrorCollection>(second.Error).Count);
    }

    private sealed record Registration(string Email);
}

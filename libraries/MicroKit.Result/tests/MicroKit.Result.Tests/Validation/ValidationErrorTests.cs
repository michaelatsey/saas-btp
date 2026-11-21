using System.Globalization;

namespace MicroKit.Result.Tests.Validation;

public sealed class ValidationErrorTests
{
    [Theory]
    [InlineData("Email", "VALIDATION.EMAIL")]
    [InlineData("firstName", "VALIDATION.FIRSTNAME")]
    [InlineData("Address.Street", "VALIDATION.ADDRESS.STREET")]
    public void Constructor_WhenPropertyNameGiven_BuildsUppercaseValidationCode(string propertyName, string expectedCode)
    {
        // Arrange
        const string message = "invalid";

        // Act
        var error = new ValidationError(propertyName, message);

        // Assert
        Assert.Equal(expectedCode, error.Code.Value);
        Assert.Equal(propertyName, error.PropertyName);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Category_WhenRead_IsValidation()
    {
        // Arrange
        var error = new ValidationError("Email", "invalid");

        // Act
        var category = error.Category;

        // Assert
        Assert.Equal(ErrorCategory.Validation, category);
    }

    [Fact]
    public void Severity_WhenRead_IsWarning()
    {
        // Arrange
        var error = new ValidationError("Email", "invalid");

        // Act
        var severity = error.Severity;

        // Assert
        Assert.Equal(ErrorSeverity.Warning, severity);
    }

    [Fact]
    public void Metadata_WhenRead_IsEmpty()
    {
        // Arrange
        var error = new ValidationError("Email", "invalid");

        // Act
        var metadata = error.Metadata;

        // Assert
        Assert.Empty(metadata);
    }

    [Fact]
    public void Constructor_WhenPropertyNameIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        string? propertyName = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => new ValidationError(propertyName!, "invalid"));

        // Assert
        Assert.Equal("propertyName", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenPropertyNameIsEmptyOrWhitespace_ThrowsArgumentException(string propertyName)
    {
        // Arrange + Act
        var exception = Assert.Throws<ArgumentException>(() => new ValidationError(propertyName, "invalid"));

        // Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Equal("propertyName", exception.ParamName);
    }

    [Fact]
    public void Required_WhenCalled_BuildsRequiredMessage()
    {
        // Arrange + Act
        var error = ValidationError.Required("Name");

        // Assert
        Assert.Equal("Name", error.PropertyName);
        Assert.Equal("VALIDATION.NAME", error.Code.Value);
        Assert.Equal("Name is required.", error.Message);
    }

    [Fact]
    public void StringLength_WhenCalled_BuildsBetweenMessage()
    {
        // Arrange + Act
        var error = ValidationError.StringLength("Name", 2, 50);

        // Assert
        Assert.Equal("Name", error.PropertyName);
        Assert.Equal("Name must be between 2 and 50 characters.", error.Message);
    }

    [Fact]
    public void MinLength_WhenCalled_BuildsAtLeastMessage()
    {
        // Arrange + Act
        var error = ValidationError.MinLength("Password", 8);

        // Assert
        Assert.Equal("Password", error.PropertyName);
        Assert.Equal("Password must be at least 8 characters.", error.Message);
    }

    [Fact]
    public void MaxLength_WhenCalled_BuildsNotExceedMessage()
    {
        // Arrange + Act
        var error = ValidationError.MaxLength("Bio", 280);

        // Assert
        Assert.Equal("Bio", error.PropertyName);
        Assert.Equal("Bio must not exceed 280 characters.", error.Message);
    }

    [Fact]
    public void OutOfRange_WhenMinAndMax_BuildsBetweenMessage()
    {
        // Arrange + Act
        var error = ValidationError.OutOfRange("Age", 18, 120);

        // Assert
        Assert.Equal("Age", error.PropertyName);
        Assert.Equal("Age must be between 18 and 120.", error.Message);
    }

    [Fact]
    public void OutOfRange_WhenOnlyMin_BuildsGreaterThanOrEqualMessage()
    {
        // Arrange + Act
        var error = ValidationError.OutOfRange("Age", min: 18);

        // Assert
        Assert.Equal("Age must be greater than or equal to 18.", error.Message);
    }

    [Fact]
    public void OutOfRange_WhenOnlyMax_BuildsLessThanOrEqualMessage()
    {
        // Arrange + Act
        var error = ValidationError.OutOfRange("Age", max: 120);

        // Assert
        Assert.Equal("Age must be less than or equal to 120.", error.Message);
    }

    [Fact]
    public void OutOfRange_WhenNoBounds_BuildsOutOfRangeMessage()
    {
        // Arrange + Act
        var error = ValidationError.OutOfRange("Age");

        // Assert
        Assert.Equal("Age is out of range.", error.Message);
    }

    [Fact]
    public void OutOfRange_WhenMinAndMaxUnderFrenchCulture_FormatsBoundsWithInvariantCulture()
    {
        // Arrange
        const decimal min = 1.5m;
        const decimal max = 2.5m;

        // Act
        var error = CreateInCulture("fr-FR", () => ValidationError.OutOfRange("Price", min, max));

        // Assert
        Assert.Equal("Price must be between 1.5 and 2.5.", error.Message);
    }

    [Fact]
    public void OutOfRange_WhenOnlyMinUnderFrenchCulture_FormatsBoundWithInvariantCulture()
    {
        // Arrange
        const double min = 1.5;

        // Act
        var error = CreateInCulture("fr-FR", () => ValidationError.OutOfRange("Price", min: min));

        // Assert
        Assert.Equal("Price must be greater than or equal to 1.5.", error.Message);
    }

    [Fact]
    public void OutOfRange_WhenOnlyMaxUnderFrenchCulture_FormatsBoundWithInvariantCulture()
    {
        // Arrange
        const double max = 2.5;

        // Act
        var error = CreateInCulture("fr-FR", () => ValidationError.OutOfRange("Price", max: max));

        // Assert
        Assert.Equal("Price must be less than or equal to 2.5.", error.Message);
    }

    [Fact]
    public void InvalidFormat_WhenNoHint_BuildsInvalidFormatMessage()
    {
        // Arrange + Act
        var error = ValidationError.InvalidFormat("Phone");

        // Assert
        Assert.Equal("Phone", error.PropertyName);
        Assert.Equal("Phone has an invalid format.", error.Message);
    }

    [Fact]
    public void InvalidFormat_WhenHintGiven_AppendsExpectedHint()
    {
        // Arrange + Act
        var error = ValidationError.InvalidFormat("Phone", "E.164");

        // Assert
        Assert.Equal("Phone has an invalid format. Expected: E.164", error.Message);
    }

    [Fact]
    public void InvalidEmail_WhenNoPropertyName_UsesEmail()
    {
        // Arrange + Act
        var error = ValidationError.InvalidEmail();

        // Assert
        Assert.Equal("Email", error.PropertyName);
        Assert.Equal("VALIDATION.EMAIL", error.Code.Value);
        Assert.Equal("Email is not a valid email address.", error.Message);
    }

    [Fact]
    public void InvalidEmail_WhenPropertyNameGiven_UsesIt()
    {
        // Arrange + Act
        var error = ValidationError.InvalidEmail("ContactEmail");

        // Assert
        Assert.Equal("ContactEmail", error.PropertyName);
        Assert.Equal("ContactEmail is not a valid email address.", error.Message);
    }

    [Fact]
    public void InvalidUrl_WhenNoPropertyName_UsesUrl()
    {
        // Arrange + Act
        var error = ValidationError.InvalidUrl();

        // Assert
        Assert.Equal("Url", error.PropertyName);
        Assert.Equal("VALIDATION.URL", error.Code.Value);
        Assert.Equal("Url is not a valid URL.", error.Message);
    }

    [Fact]
    public void InvalidUrl_WhenPropertyNameGiven_UsesIt()
    {
        // Arrange + Act
        var error = ValidationError.InvalidUrl("Website");

        // Assert
        Assert.Equal("Website", error.PropertyName);
        Assert.Equal("Website is not a valid URL.", error.Message);
    }

    [Fact]
    public void Custom_WhenCalled_UsesMessageAsIs()
    {
        // Arrange + Act
        var error = ValidationError.Custom("Coupon", "Coupon has expired.");

        // Assert
        Assert.Equal("Coupon", error.PropertyName);
        Assert.Equal("VALIDATION.COUPON", error.Code.Value);
        Assert.Equal("Coupon has expired.", error.Message);
    }

    [Fact]
    public void Factory_WhenPropertyNameIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        const string propertyName = " ";

        // Act + Assert
        Assert.Throws<ArgumentException>(() => ValidationError.Required(propertyName));
    }

    [Fact]
    public void ToString_WhenCalled_IncludesPropertyNameAndMessage()
    {
        // Arrange
        var error = ValidationError.Required("Name");

        // Act
        var text = error.ToString();

        // Assert
        Assert.Equal("ValidationError[Name]: Name is required.", text);
    }

    [Fact]
    public void ValidationError_WhenCreated_ImplementsIValidationError()
    {
        // Arrange
        var error = ValidationError.Required("Name");

        // Act
        var asValidation = Assert.IsAssignableFrom<IValidationError>(error);

        // Assert
        Assert.NotNull(asValidation);
        Assert.Equal("Name", asValidation.PropertyName);
        Assert.Equal(ErrorCategory.Validation, asValidation.Category);
    }

    [Fact]
    public void Equals_WhenSamePropertyAndMessage_ReturnsTrue()
    {
        // Arrange
        var first = ValidationError.Required("Name");
        var second = new ValidationError("Name", "Name is required.");

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.True(equal);
    }

    [Fact]
    public void Equals_WhenDifferentProperty_ReturnsFalse()
    {
        // Arrange
        var first = ValidationError.Required("Name");
        var second = ValidationError.Required("Email");

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.False(equal);
    }

    [Fact]
    public void With_WhenPropertyNameChanged_RebuildsCodeFromNewPropertyName()
    {
        // Arrange
        var original = new ValidationError("Email", "m");

        // Act
        var changed = original with { PropertyName = "Phone" };

        // Assert
        Assert.Equal("Phone", changed.PropertyName);
        Assert.Equal(ErrorCode.From("VALIDATION.PHONE"), changed.Code);
    }

    [Fact]
    public void With_WhenPropertyNameChanged_EqualsErrorBuiltWithNewPropertyName()
    {
        // Arrange
        var original = new ValidationError("Email", "m");
        var expected = new ValidationError("Phone", "m");

        // Act
        var changed = original with { PropertyName = "Phone" };

        // Assert
        Assert.Equal(expected, changed);
        Assert.True((changed == expected));
        Assert.Equal(expected.GetHashCode(), changed.GetHashCode());
    }

    [Fact]
    public void With_WhenPropertyNameChanged_LeavesOriginalUnchanged()
    {
        // Arrange
        var original = new ValidationError("Email", "m");

        // Act
        var changed = original with { PropertyName = "Phone" };

        // Assert
        Assert.NotSame(original, changed);
        Assert.Equal("Email", original.PropertyName);
        Assert.Equal(ErrorCode.From("VALIDATION.EMAIL"), original.Code);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void With_WhenPropertyNameIsBlank_ThrowsArgumentException(string propertyName)
    {
        // Arrange
        var original = new ValidationError("Email", "m");

        // Act + Assert
        Assert.Throws<ArgumentException>(() => original with { PropertyName = propertyName });
    }

    [Fact]
    public void With_WhenPropertyNameIsNull_ThrowsArgumentException()
    {
        // Arrange
        var original = new ValidationError("Email", "m");

        // Act + Assert
        Assert.ThrowsAny<ArgumentException>(() => original with { PropertyName = null! });
    }

    private static ValidationError CreateInCulture(string cultureName, Func<ValidationError> create)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            return create();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void With_WhenCodeChangedToAnotherValue_ThrowsArgumentException()
    {
        // Arrange
        var error = new ValidationError("Email", "Email is required.");

        // Act + Assert
        Assert.Throws<ArgumentException>(() => error with { Code = ErrorCode.From("OTHER.CODE") });
    }

    [Fact]
    public void With_WhenCodeChangedThroughTheErrorType_ThrowsArgumentException()
    {
        // Arrange: the with expression binds to Error.Code
        Error error = new ValidationError("Email", "Email is required.");

        // Act + Assert
        Assert.Throws<ArgumentException>(() => error with { Code = ErrorCode.From("OTHER.CODE") });
    }

    [Fact]
    public void With_WhenCodeSetToTheCodeOfItsPropertyName_KeepsTheError()
    {
        // Arrange
        var error = new ValidationError("Email", "Email is required.");

        // Act
        var same = error with { Code = ErrorCode.From("VALIDATION.EMAIL") };

        // Assert
        Assert.Equal(error, same);
    }

    [Fact]
    public void With_WhenCodeIsSetAfterItsNewPropertyName_KeepsBoth()
    {
        // Arrange
        var error = new ValidationError("Email", "Email is required.");

        // Act
        var renamed = error with { PropertyName = "Name", Code = ErrorCode.From("VALIDATION.NAME") };

        // Assert
        Assert.Equal(ErrorCode.From("VALIDATION.NAME"), renamed.Code);
    }

    [Fact]
    public void With_WhenCodeIsSetBeforeItsNewPropertyName_ThrowsArgumentException()
    {
        // Arrange: members of a with expression are set in the order they are written; set PropertyName alone
        var error = new ValidationError("Email", "Email is required.");

        // Act + Assert
        Assert.Throws<ArgumentException>(() => error with { Code = ErrorCode.From("VALIDATION.NAME"), PropertyName = "Name" });
    }

    [Fact]
    public void With_WhenCodeIsDefaultThroughTheSubtype_ThrowsArgumentException()
    {
        // Arrange
        var error = new ValidationError("Email", "Email is required.");

        // Act + Assert
        Assert.Throws<ArgumentException>(() => error with { Code = default });
    }

    [Fact]
    public void With_WhenCodeIsDefaultThroughError_ThrowsArgumentException()
    {
        // Arrange
        Error error = new ValidationError("Email", "Email is required.");

        // Act + Assert
        Assert.Throws<ArgumentException>(() => error with { Code = default });
    }
}

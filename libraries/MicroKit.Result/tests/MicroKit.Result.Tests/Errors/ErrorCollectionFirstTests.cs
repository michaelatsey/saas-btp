using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Errors;

// ErrorCollection.FirstOrDefault is a non-nullable IError: a collection is never empty.
public sealed class ErrorCollectionFirstTests
{
    [Fact]
    public void FirstOrDefault_WhenSeveralErrors_ReturnsFirstError()
    {
        // Arrange
        var first = new TestError("first");
        var collection = ErrorCollection.From(first, new OtherError(), new NotFoundTestError(1));

        // Act
        IError firstError = collection.FirstOrDefault;

        // Assert
        Assert.Same(first, firstError);
        Assert.Same(collection[0], firstError);
    }

    [Fact]
    public void FirstOrDefault_WhenSingleError_ReturnsThatError()
    {
        // Arrange
        var only = new OtherError();
        var collection = ErrorCollection.From(only);

        // Act
        IError firstError = collection.FirstOrDefault;

        // Assert
        Assert.Same(only, firstError);
    }

    [Fact]
    public void FirstOrDefault_WhenBuiltFromEnumerable_ReturnsFirstInEnumerationOrder()
    {
        // Arrange
        IEnumerable<IError> errors = [new NotFoundTestError(2), new TestError("second")];
        var collection = ErrorCollection.From(errors);

        // Act
        IError firstError = collection.FirstOrDefault;

        // Assert — usable without a null check: its code is read directly, with no '!'
        Assert.Equal(2, Assert.IsType<NotFoundTestError>(firstError).Id);
        Assert.Equal("TEST.NOT_FOUND", collection.FirstOrDefault.Code.Value);
    }

    [Fact]
    public void FirstOrDefault_StaticType_IsNonNullableIError()
    {
        // Arrange
        var property = typeof(ErrorCollection).GetProperty(nameof(ErrorCollection.FirstOrDefault))!;
        var nullability = new System.Reflection.NullabilityInfoContext().Create(property);

        // Act
        var readState = nullability.ReadState;

        // Assert
        Assert.Equal(typeof(IError), property.PropertyType);
        Assert.Equal(System.Reflection.NullabilityState.NotNull, readState);
    }
}

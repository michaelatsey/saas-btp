using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Core;

public sealed class ResultCombineTests
{

    #region Combine

    [Fact]
    public void Combine_WhenEmpty_ReturnsSuccess()
    {
        // Act
        var combined = Result.Combine();

        // Assert
        Assert.True(combined.IsSuccess);
    }

    [Fact]
    public void Combine_WhenAllSucceed_ReturnsSuccess()
    {
        // Act
        var combined = Result.Combine(Result.Success(), Result.Success(), Result.Success());

        // Assert
        Assert.True(combined.IsSuccess);
    }

    [Fact]
    public void Combine_WhenSeveralFail_ReturnsFirstFailureBySameError()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();

        // Act
        var combined = Result.Combine(
            Result.Success(),
            Result.Failure(first),
            Result.Failure(second));

        // Assert
        Assert.True(combined.IsFailure);
        Assert.Same(first, combined.Error);
    }

    [Fact]
    public void Combine_WhenAnyIsDefault_ThrowsResultException()
    {
        // Arrange
        Result uninitialized = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => Result.Combine(Result.Success(), uninitialized));
    }

    #endregion

    #region CombineAll

    [Fact]
    public void CombineAll_WhenEmpty_ReturnsSuccess()
    {
        // Act
        var combined = Result.CombineAll();

        // Assert
        Assert.True(combined.IsSuccess);
    }

    [Fact]
    public void CombineAll_WhenAllSucceed_ReturnsSuccess()
    {
        // Act
        var combined = Result.CombineAll(Result.Success(), Result.Success());

        // Assert
        Assert.True(combined.IsSuccess);
    }

    [Fact]
    public void CombineAll_WhenSeveralFail_ReturnsErrorCollectionWithEveryErrorInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();

        // Act
        var combined = Result.CombineAll(
            Result.Failure(first),
            Result.Success(),
            Result.Failure(second));

        // Assert
        Assert.True(combined.IsFailure);
        var collection = Assert.IsType<ErrorCollection>(combined.Error);
        Assert.Equal(2, collection.Count);
        Assert.Same(first, collection[0]);
        Assert.Same(second, collection[1]);
    }

    [Fact]
    public void CombineAll_WhenOneFails_WrapsItsErrorInErrorCollection()
    {
        // Arrange
        var error = new TestError();

        // Act
        var combined = Result.CombineAll(Result.Success(), Result.Failure(error));

        // Assert
        Assert.True(combined.IsFailure);
        var collection = Assert.IsType<ErrorCollection>(combined.Error);
        Assert.Same(error, Assert.Single(collection));
    }

    [Fact]
    public void CombineAll_WhenAnyIsDefault_ThrowsResultException()
    {
        // Arrange
        Result uninitialized = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => Result.CombineAll(Result.Success(), uninitialized));
    }

    #endregion

    #region Combine (two values)

    [Fact]
    public void Combine_TwoValuesWhenBothSucceed_ReturnsTuple()
    {
        // Act
        var combined = Result.Combine(Result.Success(1), Result.Success("two"));

        // Assert
        Assert.True(combined.IsSuccess);
        Assert.Equal((1, "two"), combined.Value);
    }

    [Fact]
    public void Combine_TwoValuesWhenBothFail_ReturnsFirstError()
    {
        // Arrange
        var first = new TestError("first");

        // Act
        var combined = Result.Combine(
            Result.Failure<int>(first),
            Result.Failure<string>(new OtherError()));

        // Assert
        Assert.True(combined.IsFailure);
        Assert.Same(first, combined.Error);
    }

    [Fact]
    public void Combine_TwoValuesWhenSecondFails_ReturnsSecondError()
    {
        // Arrange
        var error = new OtherError();

        // Act
        var combined = Result.Combine(Result.Success(1), Result.Failure<string>(error));

        // Assert
        Assert.True(combined.IsFailure);
        Assert.Same(error, combined.Error);
    }

    [Fact]
    public void Combine_TwoValuesWhenFirstIsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> uninitialized = default;

        // Act + Assert
        Assert.Throws<ResultException>(() =>
            Result.Combine(uninitialized, Result.Success("two")));
    }

    [Fact]
    public void Combine_TwoValuesWhenSecondIsDefault_ThrowsResultException()
    {
        // Arrange
        Result<string> uninitialized = default;

        // Act + Assert
        Assert.Throws<ResultException>(() =>
            Result.Combine(Result.Success(1), uninitialized));
    }

    #endregion

    #region Combine (three values)

    [Fact]
    public void Combine_ThreeValuesWhenAllSucceed_ReturnsTuple()
    {
        // Act
        var combined = Result.Combine(
            Result.Success(1),
            Result.Success("two"),
            Result.Success(3.0));

        // Assert
        Assert.True(combined.IsSuccess);
        Assert.Equal((1, "two", 3.0), combined.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Combine_ThreeValuesWhenOneFails_ReturnsItsError(int failingIndex)
    {
        // Arrange
        var error = new NotFoundTestError(failingIndex);
        var first = failingIndex == 0 ? Result.Failure<int>(error) : Result.Success(1);
        var second = failingIndex == 1 ? Result.Failure<string>(error) : Result.Success("two");
        var third = failingIndex == 2 ? Result.Failure<double>(error) : Result.Success(3.0);

        // Act
        var combined = Result.Combine(first, second, third);

        // Assert
        Assert.True(combined.IsFailure);
        Assert.Same(error, combined.Error);
    }

    [Fact]
    public void Combine_ThreeValuesWhenSeveralFail_ReturnsFirstError()
    {
        // Arrange
        var second = new TestError("second");

        // Act
        var combined = Result.Combine(
            Result.Success(1),
            Result.Failure<string>(second),
            Result.Failure<double>(new OtherError()));

        // Assert
        Assert.True(combined.IsFailure);
        Assert.Same(second, combined.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Combine_ThreeValuesWhenOneIsDefault_ThrowsResultException(int defaultIndex)
    {
        // Arrange
        var first = defaultIndex == 0 ? default : Result.Success(1);
        var second = defaultIndex == 1 ? default : Result.Success("two");
        var third = defaultIndex == 2 ? default : Result.Success(3.0);

        // Act + Assert
        Assert.Throws<ResultException>(() => Result.Combine(first, second, third));
    }

    #endregion

    #region CombineAll (generic)

    [Fact]
    public void CombineAll_GenericWhenEmpty_ReturnsSuccess()
    {
        // Act
        var combined = Result.CombineAll<int>();

        // Assert
        Assert.True(combined.IsSuccess);
    }

    [Fact]
    public void CombineAll_GenericWhenAllSucceed_ReturnsSuccess()
    {
        // Act
        var combined = Result.CombineAll(Result.Success(1), Result.Success(2));

        // Assert
        Assert.True(combined.IsSuccess);
    }

    [Fact]
    public void CombineAll_GenericWhenSeveralFail_ReturnsErrorCollectionWithEveryErrorInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();

        // Act
        var combined = Result.CombineAll(
            Result.Success(1),
            Result.Failure<int>(first),
            Result.Failure<int>(second));

        // Assert
        Assert.True(combined.IsFailure);
        var collection = Assert.IsType<ErrorCollection>(combined.Error);
        Assert.Equal(2, collection.Count);
        Assert.Same(first, collection[0]);
        Assert.Same(second, collection[1]);
    }

    [Fact]
    public void CombineAll_GenericWhenAnyIsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> uninitialized = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => Result.CombineAll(Result.Success(1), uninitialized));
    }

    #endregion

    [Fact]
    public void Combine_Tuple_WhenADefaultComesBeforeAFailure_ThrowsResultException()
    {
        // Arrange
        Result<int> uninitialized = default;
        var failure = Result.Failure<string>(new TestSupport.OtherError());

        // Act + Assert: the default is not hidden behind the later failure
        Assert.Throws<ResultException>(() => Result.Combine(uninitialized, failure));
        Assert.Throws<ResultException>(() => Result.Combine(uninitialized, failure, Result.Success(1)));
    }
}

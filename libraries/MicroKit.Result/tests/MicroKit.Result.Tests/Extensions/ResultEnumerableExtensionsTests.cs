using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Extensions;

public sealed class ResultEnumerableExtensionsTests
{

    #region Partition

    [Fact]
    public void Partition_WhenMixed_SplitsValuesAndErrorsInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();
        Result<int>[] results =
        [
            Result.Success(1),
            Result.Failure<int>(first),
            Result.Success(2),
            Result.Failure<int>(second),
        ];

        // Act
        var (successes, failures) = results.Partition();

        // Assert
        Assert.Equal([1, 2], successes);
        Assert.Equal(2, failures.Count);
        Assert.Same(first, failures[0]);
        Assert.Same(second, failures[1]);
    }

    [Fact]
    public void Partition_WhenAllSucceed_ReturnsNoFailures()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), Result.Success(2)];

        // Act
        var (successes, failures) = results.Partition();

        // Assert
        Assert.Equal([1, 2], successes);
        Assert.Empty(failures);
    }

    [Fact]
    public void Partition_WhenEmpty_ReturnsTwoEmptyLists()
    {
        // Arrange
        var results = Enumerable.Empty<Result<int>>();

        // Act
        var (successes, failures) = results.Partition();

        // Assert
        Assert.Empty(successes);
        Assert.Empty(failures);
    }

    [Fact]
    public void Partition_WhenSequenceContainsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), default];

        // Act + Assert
        Assert.Throws<ResultException>(() => results.Partition());
    }

    [Fact]
    public void Partition_WhenSourceIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<Result<int>> results = null!;

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => results.Partition());
        Assert.Equal("results", thrown.ParamName);
    }

    #endregion

    #region Successes

    [Fact]
    public void Successes_WhenMixed_YieldsSuccessValuesInOrder()
    {
        // Arrange
        Result<int>[] results =
        [
            Result.Success(1),
            Result.Failure<int>(new TestError()),
            Result.Success(2),
        ];

        // Act
        var values = results.Successes().ToList();

        // Assert
        Assert.Equal([1, 2], values);
    }

    [Fact]
    public void Successes_WhenAllFail_YieldsNothing()
    {
        // Arrange
        Result<int>[] results = [Result.Failure<int>(new TestError())];

        // Act
        var values = results.Successes().ToList();

        // Assert
        Assert.Empty(values);
    }

    [Fact]
    public void Successes_WhenSourceIsNull_ThrowsArgumentNullExceptionOnEnumeration()
    {
        // Arrange
        IEnumerable<Result<int>> results = null!;

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => results.Successes().ToList());
        Assert.Equal("results", thrown.ParamName);
    }

    [Fact]
    public void Successes_WhenSequenceContainsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), default];

        // Act + Assert
        Assert.Throws<ResultException>(() => results.Successes().ToList());
    }

    #endregion

    #region Failures

    [Fact]
    public void Failures_WhenMixed_YieldsSameErrorsInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();
        Result<int>[] results =
        [
            Result.Failure<int>(first),
            Result.Success(1),
            Result.Failure<int>(second),
        ];

        // Act
        var errors = results.Failures().ToList();

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.Same(first, errors[0]);
        Assert.Same(second, errors[1]);
    }

    [Fact]
    public void Failures_WhenAllSucceed_YieldsNothing()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), Result.Success(2)];

        // Act
        var errors = results.Failures().ToList();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Failures_WhenSourceIsNull_ThrowsArgumentNullExceptionOnEnumeration()
    {
        // Arrange
        IEnumerable<Result<int>> results = null!;

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => results.Failures().ToList());
        Assert.Equal("results", thrown.ParamName);
    }

    [Fact]
    public void Failures_WhenSequenceContainsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int>[] results = [Result.Failure<int>(new TestError()), default];

        // Act + Assert
        Assert.Throws<ResultException>(() => results.Failures().ToList());
    }

    #endregion

    #region Traverse

    [Fact]
    public void Traverse_WhenEverySelectionSucceeds_ReturnsValuesInOrder()
    {
        // Arrange
        int[] source = [1, 2, 3];

        // Act
        var traversed = source.Traverse(x => Result.Success(x * 10));

        // Assert
        Assert.True(traversed.IsSuccess);
        Assert.Equal([10, 20, 30], traversed.Value);
    }

    [Fact]
    public void Traverse_WhenSelectionFails_ReturnsFirstErrorAndStopsSelecting()
    {
        // Arrange
        var first = new NotFoundTestError(2);
        var selected = new List<int>();
        int[] source = [1, 2, 3, 4];

        // Act
        var traversed = source.Traverse(x =>
        {
            selected.Add(x);
            return x switch
            {
                2 => Result.Failure<int>(first),
                3 => Result.Failure<int>(new OtherError()),
                _ => Result.Success(x),
            };
        });

        // Assert
        Assert.True(traversed.IsFailure);
        Assert.Same(first, traversed.Error);
        Assert.Equal([1, 2], selected);
    }

    [Fact]
    public void Traverse_WhenSourceIsEmpty_ReturnsSuccessWithEmptyList()
    {
        // Arrange
        var called = false;
        var source = Enumerable.Empty<int>();

        // Act
        var traversed = source.Traverse(x =>
        {
            called = true;
            return Result.Success(x);
        });

        // Assert
        Assert.False(called);
        Assert.True(traversed.IsSuccess);
        Assert.Empty(traversed.Value);
    }

    [Fact]
    public void Traverse_WhenSelectorReturnsDefault_ThrowsResultException()
    {
        // Arrange
        int[] source = [1];

        // Act + Assert
        Assert.Throws<ResultException>(() => source.Traverse(_ => default(Result<int>)));
    }

    [Fact]
    public void Traverse_WhenSelectorThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        int[] source = [1];

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => source.Traverse<int, int>(_ => throw boom));
        Assert.Same(boom, thrown);
    }

    [Fact]
    public void Traverse_WhenSourceIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<int> source = null!;

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => source.Traverse(x => Result.Success(x)));
        Assert.Equal("source", thrown.ParamName);
    }

    [Fact]
    public void Traverse_WhenSelectorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        int[] source = [1];

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => source.Traverse<int, int>(null!));
        Assert.Equal("selector", thrown.ParamName);
    }

    #endregion
}

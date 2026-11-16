using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Extensions;

public sealed class ResultLinqExtensionsTests
{
    #region Select

    [Fact]
    public void Select_WhenSuccess_ProjectsValue()
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var projected = result.Select(x => x * 2);

        // Assert
        Assert.True(projected.IsSuccess);
        Assert.Equal(42, projected.Value);
    }

    [Fact]
    public void Select_WithQuerySyntax_ProjectsValue()
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var projected = from x in result select x * 2;

        // Assert
        Assert.True(projected.IsSuccess);
        Assert.Equal(42, projected.Value);
    }

    [Fact]
    public void Select_WhenFailure_PropagatesSameErrorWithoutInvokingSelector()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var projected = result.Select(x =>
        {
            called = true;
            return x;
        });

        // Assert
        Assert.False(called);
        Assert.True(projected.IsFailure);
        Assert.Same(error, projected.Error);
    }

    [Fact]
    public void Select_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Select(x => x));
    }

    [Fact]
    public void Select_WhenSelectorIsNull_ThrowsArgumentNullExceptionNamingSelector()
    {
        // Arrange
        var result = Result.Success(1);

        // Act
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Select<int, int>(null!));

        // Assert
        Assert.Equal("selector", thrown.ParamName);
    }

    #endregion

    #region SelectMany (bind)

    [Fact]
    public void SelectMany_WhenSuccess_ReturnsSelectorResult()
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var bound = result.SelectMany(x => Result.Success(x * 2));

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal(42, bound.Value);
    }

    [Fact]
    public void SelectMany_WhenSelectorFails_ReturnsSelectorError()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success(21);

        // Act
        var bound = result.SelectMany(_ => Result.Failure<int>(error));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void SelectMany_WhenFailure_PropagatesSameErrorWithoutInvokingSelector()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var bound = result.SelectMany(x =>
        {
            called = true;
            return Result.Success(x);
        });

        // Assert
        Assert.False(called);
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void SelectMany_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.SelectMany(x => Result.Success(x)));
    }

    [Fact]
    public void SelectMany_WhenSelectorIsNull_ThrowsArgumentNullExceptionNamingSelector()
    {
        // Arrange
        var result = Result.Success(1);

        // Act
        var thrown = Assert.Throws<ArgumentNullException>(() => result.SelectMany<int, int>(null!));

        // Assert
        Assert.Equal("selector", thrown.ParamName);
    }

    #endregion

    #region SelectMany (bind and project)

    [Fact]
    public void SelectMany_WithProjectionWhenBothSucceed_CombinesValues()
    {
        // Arrange
        var result = Result.Success(2);

        // Act
        var combined = result.SelectMany(x => Result.Success(x * 10), (x, y) => x + y);

        // Assert
        Assert.True(combined.IsSuccess);
        Assert.Equal(22, combined.Value);
    }

    [Fact]
    public void SelectMany_WithQuerySyntaxWhenEverySourceSucceeds_CombinesValues()
    {
        // Arrange
        var first = Result.Success(1);
        var second = Result.Success(20);
        var third = Result.Success(300);

        // Act
        var combined =
            from a in first
            from b in second
            from c in third
            select a + b + c;

        // Assert
        Assert.True(combined.IsSuccess);
        Assert.Equal(321, combined.Value);
    }

    [Fact]
    public void SelectMany_WithQuerySyntaxWhenMiddleSourceFails_ReturnsItsErrorWithoutEvaluatingLaterSteps()
    {
        // Arrange
        var laterCalls = 0;
        var error = new NotFoundTestError(2);

        // Act
        var combined =
            from a in Result.Success(1)
            from b in Result.Failure<int>(error)
            from c in Later(a + b)
            select a + b + c;

        // Assert
        Assert.Equal(0, laterCalls);
        Assert.True(combined.IsFailure);
        Assert.Same(error, combined.Error);

        Result<int> Later(int value)
        {
            laterCalls++;
            return Result.Success(value);
        }
    }

    [Fact]
    public void SelectMany_WithProjectionWhenSourceFails_PropagatesSameErrorWithoutInvokingSelectors()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var combined = result.SelectMany(
            x =>
            {
                called = true;
                return Result.Success(x);
            },
            (x, y) =>
            {
                called = true;
                return x + y;
            });

        // Assert
        Assert.False(called);
        Assert.True(combined.IsFailure);
        Assert.Same(error, combined.Error);
    }

    [Fact]
    public void SelectMany_WithProjectionWhenIntermediateFails_ReturnsIntermediateErrorWithoutProjecting()
    {
        // Arrange
        var projected = false;
        var error = new OtherError();
        var result = Result.Success(1);

        // Act
        var combined = result.SelectMany(
            _ => Result.Failure<int>(error),
            (x, y) =>
            {
                projected = true;
                return x + y;
            });

        // Assert
        Assert.False(projected);
        Assert.True(combined.IsFailure);
        Assert.Same(error, combined.Error);
    }

    [Fact]
    public void SelectMany_WithProjectionWhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.SelectMany(x => Result.Success(x), (x, y) => x + y));
    }

    [Fact]
    public void SelectMany_WithProjectionWhenIntermediateIsDefault_ThrowsResultException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        Assert.Throws<ResultException>(() => result.SelectMany(_ => default(Result<int>), (x, y) => x + y));
    }

    [Fact]
    public void SelectMany_WithProjectionWhenCollectionSelectorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() =>
            result.SelectMany<int, int, int>(null!, (x, y) => x + y));
        Assert.Equal("collectionSelector", thrown.ParamName);
    }

    [Fact]
    public void SelectMany_WithProjectionWhenResultSelectorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() =>
            result.SelectMany<int, int, int>(x => Result.Success(x), null!));
        Assert.Equal("resultSelector", thrown.ParamName);
    }

    #endregion

    #region Where

    [Fact]
    public void Where_WhenPredicateTrue_ReturnsSameResult()
    {
        // Arrange
        var result = Result.Success(10);

        // Act
        var filtered = result.Where(x => x > 5, new TestError());

        // Assert
        Assert.Equal(result, filtered);
    }

    [Fact]
    public void Where_WhenPredicateFalse_ReturnsFailureWithSameError()
    {
        // Arrange
        var error = new TestError("too small");
        var result = Result.Success(1);

        // Act
        var filtered = result.Where(x => x > 5, error);

        // Assert
        Assert.True(filtered.IsFailure);
        Assert.Same(error, filtered.Error);
    }

    [Fact]
    public void Where_WhenFailure_PropagatesOriginalErrorWithoutInvokingPredicate()
    {
        // Arrange
        var called = false;
        var original = new TestError();
        var result = Result.Failure<int>(original);

        // Act
        var filtered = result.Where(
            _ =>
            {
                called = true;
                return false;
            },
            new OtherError());

        // Assert
        Assert.False(called);
        Assert.True(filtered.IsFailure);
        Assert.Same(original, filtered.Error);
    }

    [Fact]
    public void Where_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Where(_ => true, new TestError()));
    }

    [Fact]
    public void Where_WhenPredicateIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Where(null!, new TestError()));
        Assert.Equal("predicate", thrown.ParamName);
    }

    [Fact]
    public void Where_WhenErrorIsNull_ThrowsArgumentNullExceptionNamingErrorOnFalse()
    {
        // Arrange
        var result = Result.Success(1);

        // Act
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Where(_ => true, null!));

        // Assert
        Assert.Equal("errorOnFalse", thrown.ParamName);
    }

    #endregion

    #region AsEnumerable

    [Fact]
    public void AsEnumerable_WhenSuccess_YieldsValueOnce()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var values = result.AsEnumerable().ToList();

        // Assert
        Assert.Equal(42, Assert.Single(values));
    }

    [Fact]
    public void AsEnumerable_WhenFailure_YieldsNothing()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act
        var values = result.AsEnumerable().ToList();

        // Assert
        Assert.Empty(values);
    }

    [Fact]
    public void AsEnumerable_WhenDefault_YieldsNothing()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var values = result.AsEnumerable().ToList();

        // Assert
        Assert.Empty(values);
    }

    #endregion
}

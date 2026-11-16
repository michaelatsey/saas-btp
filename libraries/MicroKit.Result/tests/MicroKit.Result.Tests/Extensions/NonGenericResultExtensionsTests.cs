using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Extensions;

// MicroKit ADR-RESULT-001 D9 (michaelatsey/microkit@a12097b): the non-generic Result has the sync operations Result<T> has, and Bind goes from a value to a
// command. A default Result throws ResultException where Result<T> does (Map, Bind, Ensure, MapError, Compensate),
// without calling the delegate.
public sealed class NonGenericResultExtensionsTests
{
    #region Map

    [Fact]
    public void Map_WhenSuccess_ReturnsSuccessWithMapperValue()
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result<int> mapped = result.Map(() => 42);

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(42, mapped.Value);
    }

    [Fact]
    public void Map_WhenFailure_PropagatesSameErrorWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var mapped = result.Map(() =>
        {
            called = true;
            return 42;
        });

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Same(error, mapped.Error);
        Assert.False(called);
    }

    [Fact]
    public void Map_WhenDefault_ThrowsResultExceptionWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Map(() =>
        {
            called = true;
            return 42;
        }));
        Assert.False(called);
    }

    [Fact]
    public void Map_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Map<int>(null!));
        Assert.Equal("mapper", thrown.ParamName);
    }

    [Fact]
    public void Map_WhenFailureAndMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Map<int>(null!));
        Assert.Equal("mapper", thrown.ParamName);
    }

    [Fact]
    public void Map_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => result.Map<string>(() => null!));
    }

    [Fact]
    public void Map_WhenMapperThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Map<int>(() => throw boom));
        Assert.Same(boom, thrown);
    }

    #endregion

    #region Bind (Result -> Result)

    [Fact]
    public void Bind_WhenSuccess_ReturnsBinderResult()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success();

        // Act
        Result bound = result.Bind(() => Result.Failure(error));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void Bind_WhenSuccessAndBinderSucceeds_ReturnsSuccess()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        Result bound = result.Bind(() =>
        {
            called = true;
            return Result.Success();
        });

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.True(called);
    }

    [Fact]
    public void Bind_WhenFailure_PropagatesSameErrorWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result bound = result.Bind(() =>
        {
            called = true;
            return Result.Success();
        });

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
        Assert.False(called);
    }

    [Fact]
    public void Bind_WhenDefault_ThrowsResultExceptionWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Bind(() =>
        {
            called = true;
            return Result.Success();
        }));
        Assert.False(called);
    }

    [Fact]
    public void Bind_WhenBinderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Bind((Func<Result>)null!));
        Assert.Equal("binder", thrown.ParamName);
    }

    #endregion

    #region Bind (Result -> Result<TOut>)

    [Fact]
    public void BindToValue_WhenSuccess_ReturnsBinderResult()
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result<string> bound = result.Bind(() => Result.Success("created"));

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal("created", bound.Value);
    }

    [Fact]
    public void BindToValue_WhenSuccessAndBinderFails_ReturnsBinderFailure()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success();

        // Act
        Result<string> bound = result.Bind(() => Result.Failure<string>(error));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void BindToValue_WhenFailure_PropagatesSameErrorWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result<string> bound = result.Bind(() =>
        {
            called = true;
            return Result.Success("created");
        });

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
        Assert.False(called);
    }

    [Fact]
    public void BindToValue_WhenDefault_ThrowsResultExceptionWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Bind(() =>
        {
            called = true;
            return Result.Success(1);
        }));
        Assert.False(called);
    }

    [Fact]
    public void BindToValue_WhenBinderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Bind((Func<Result<int>>)null!));
        Assert.Equal("binder", thrown.ParamName);
    }

    #endregion

    #region Bind (Result<TIn> -> Result)

    [Fact]
    public void BindToCommand_WhenSuccess_PassesValueAndReturnsBinderResult()
    {
        // Arrange
        var seen = 0;
        var result = Result.Success(21);

        // Act
        Result bound = result.Bind(x =>
        {
            seen = x;
            return Result.Success();
        });

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal(21, seen);
    }

    [Fact]
    public void BindToCommand_WhenSuccessAndBinderFails_ReturnsBinderFailure()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success(21);

        // Act
        Result bound = result.Bind(_ => Result.Failure(error));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void BindToCommand_WhenFailure_PropagatesSameErrorWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        Result bound = result.Bind(_ =>
        {
            called = true;
            return Result.Success();
        });

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
        Assert.False(called);
    }

    [Fact]
    public void BindToCommand_WhenDefault_ThrowsResultExceptionWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Bind(_ =>
        {
            called = true;
            return Result.Success();
        }));
        Assert.False(called);
    }

    [Fact]
    public void BindToCommand_WhenBinderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Bind((Func<int, Result>)null!));
        Assert.Equal("binder", thrown.ParamName);
    }

    #endregion

    #region Ensure

    [Fact]
    public void Ensure_WhenSuccessAndPredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result ensured = result.Ensure(() => true, new TestError());

        // Assert
        Assert.True(ensured.IsSuccess);
    }

    [Fact]
    public void Ensure_WhenSuccessAndPredicateFalse_ReturnsFailureWithError()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success();

        // Act
        Result ensured = result.Ensure(() => false, error);

        // Assert
        Assert.True(ensured.IsFailure);
        Assert.Same(error, ensured.Error);
    }

    [Fact]
    public void Ensure_WhenFailure_PropagatesOriginalErrorWithoutCallingPredicate()
    {
        // Arrange
        var called = false;
        var original = new TestError();
        var result = Result.Failure(original);

        // Act
        Result ensured = result.Ensure(() =>
        {
            called = true;
            return false;
        }, new OtherError());

        // Assert
        Assert.True(ensured.IsFailure);
        Assert.Same(original, ensured.Error);
        Assert.False(called);
    }

    [Fact]
    public void Ensure_WhenDefault_ThrowsResultExceptionWithoutCallingPredicate()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Ensure(() =>
        {
            called = true;
            return true;
        }, new TestError()));
        Assert.False(called);
    }

    [Fact]
    public void Ensure_WhenPredicateIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Ensure(null!, new TestError()));
        Assert.Equal("predicate", thrown.ParamName);
    }

    [Fact]
    public void Ensure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Ensure(() => true, null!));
        Assert.Equal("error", thrown.ParamName);
    }

    #endregion

    #region MapError

    [Fact]
    public void MapError_WhenFailure_ReturnsFailureWithMappedError()
    {
        // Arrange
        var original = new TestError();
        var replacement = new OtherError();
        IError? seen = null;
        var result = Result.Failure(original);

        // Act
        Result mapped = result.MapError(e =>
        {
            seen = e;
            return replacement;
        });

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Same(replacement, mapped.Error);
        Assert.Same(original, seen);
    }

    [Fact]
    public void MapError_WhenSuccess_ReturnsSuccessWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        Result mapped = result.MapError(e =>
        {
            called = true;
            return e;
        });

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.False(called);
    }

    [Fact]
    public void MapError_WhenDefault_ThrowsResultExceptionWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.MapError(e =>
        {
            called = true;
            return e;
        }));
        Assert.False(called);
    }

    [Fact]
    public void MapError_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.MapError(_ => null!));
        Assert.Equal("error", thrown.ParamName);
    }

    [Fact]
    public void MapError_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.MapError(null!));
        Assert.Equal("mapper", thrown.ParamName);
    }

    #endregion

    #region Compensate

    [Fact]
    public void Compensate_WhenFailure_ReturnsFallbackResult()
    {
        // Arrange
        var original = new TestError();
        IError? seen = null;
        var result = Result.Failure(original);

        // Act
        Result compensated = result.Compensate(e =>
        {
            seen = e;
            return Result.Success();
        });

        // Assert
        Assert.True(compensated.IsSuccess);
        Assert.Same(original, seen);
    }

    [Fact]
    public void Compensate_WhenFailureAndFallbackFails_ReturnsFallbackFailure()
    {
        // Arrange
        var replacement = new OtherError();
        var result = Result.Failure(new TestError());

        // Act
        Result compensated = result.Compensate(_ => Result.Failure(replacement));

        // Assert
        Assert.True(compensated.IsFailure);
        Assert.Same(replacement, compensated.Error);
    }

    [Fact]
    public void Compensate_WhenSuccess_ReturnsSuccessWithoutCallingFallback()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        Result compensated = result.Compensate(_ =>
        {
            called = true;
            return Result.Failure(new TestError());
        });

        // Assert
        Assert.True(compensated.IsSuccess);
        Assert.False(called);
    }

    [Fact]
    public void Compensate_WhenDefault_ThrowsResultExceptionWithoutCallingFallback()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Compensate(_ =>
        {
            called = true;
            return Result.Success();
        }));
        Assert.False(called);
    }

    [Fact]
    public void Compensate_WhenFallbackIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Compensate(null!));
        Assert.Equal("fallback", thrown.ParamName);
    }

    #endregion

    #region Chained

    [Fact]
    public void Chain_WhenEveryStepSucceeds_ReachesTheEnd()
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result<string> chained = result
            .Ensure(() => true, new TestError())
            .Bind(() => Result.Success())
            .Bind(() => Result.Success(20))
            .Map(x => x + 1)
            .Bind(x => x > 0 ? Result.Success() : Result.Failure(new OtherError()))
            .Map(() => "done");

        // Assert
        Assert.True(chained.IsSuccess);
        Assert.Equal("done", chained.Value);
    }

    [Fact]
    public void Chain_WhenAStepFails_SkipsLaterStepsAndKeepsTheError()
    {
        // Arrange
        var error = new OtherError();
        var laterCalls = 0;
        var result = Result.Success();

        // Act
        Result<string> chained = result
            .Ensure(() => false, error)
            .Bind(() =>
            {
                laterCalls++;
                return Result.Success();
            })
            .Map(() =>
            {
                laterCalls++;
                return "done";
            });

        // Assert
        Assert.True(chained.IsFailure);
        Assert.Same(error, chained.Error);
        Assert.Equal(0, laterCalls);
    }

    [Fact]
    public void Chain_WhenFailureIsCompensated_ResumesOnSuccessRail()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act
        Result<int> chained = result
            .MapError(_ => new OtherError())
            .Compensate(e => e is OtherError ? Result.Success() : Result.Failure(e))
            .Map(() => 7);

        // Assert
        Assert.True(chained.IsSuccess);
        Assert.Equal(7, chained.Value);
    }

    #endregion
}

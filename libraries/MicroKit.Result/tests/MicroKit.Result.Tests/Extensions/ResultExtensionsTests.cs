using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Extensions;

public sealed class ResultExtensionsTests
{

    #region Map

    [Fact]
    public void Map_WhenSuccess_TransformsValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(84, mapped.Value);
    }

    [Fact]
    public void Map_WhenSuccess_CanChangeValueType()
    {
        // Arrange
        var result = Result.Success(7);

        // Act
        var mapped = result.Map(x => $"#{x}");

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal("#7", mapped.Value);
    }

    [Fact]
    public void Map_WhenFailure_PropagatesSameError()
    {
        // Arrange
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Same(error, mapped.Error);
    }

    [Fact]
    public void Map_WhenFailure_DoesNotInvokeMapper()
    {
        // Arrange
        var called = false;
        var result = Result.Failure<int>(new TestError());

        // Act
        var mapped = result.Map(x =>
        {
            called = true;
            return x;
        });

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.False(called);
    }

    [Fact]
    public void Map_WhenDefault_ThrowsResultException()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Map(x =>
        {
            called = true;
            return x;
        }));
        Assert.False(called);
    }

    [Fact]
    public void Map_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Map<int, int>(null!));
        Assert.Equal("mapper", thrown.ParamName);
    }

    [Fact]
    public void Map_WhenFailureAndMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Map<int, int>(null!));
        Assert.Equal("mapper", thrown.ParamName);
    }

    [Fact]
    public void Map_WhenMapperThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Map<int, int>(_ => throw boom));
        Assert.Same(boom, thrown);
    }

    #endregion

    #region Bind

    [Fact]
    public void Bind_WhenSuccessAndBinderSucceeds_ReturnsBinderResult()
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var bound = result.Bind(x => Result.Success(x * 2));

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal(42, bound.Value);
    }

    [Fact]
    public void Bind_WhenSuccessAndBinderFails_ReturnsBinderError()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success(21);

        // Act
        var bound = result.Bind(_ => Result.Failure<string>(error));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void Bind_WhenFailure_PropagatesSameError()
    {
        // Arrange
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var bound = result.Bind(x => Result.Success(x * 2));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void Bind_WhenFailure_DoesNotInvokeBinder()
    {
        // Arrange
        var called = false;
        var result = Result.Failure<int>(new TestError());

        // Act
        var bound = result.Bind(x =>
        {
            called = true;
            return Result.Success(x);
        });

        // Assert
        Assert.True(bound.IsFailure);
        Assert.False(called);
    }

    [Fact]
    public void Bind_WhenDefault_ThrowsResultException()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Bind(x =>
        {
            called = true;
            return Result.Success(x);
        }));
        Assert.False(called);
    }

    [Fact]
    public void Bind_WhenBinderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Bind<int, int>(null!));
        Assert.Equal("binder", thrown.ParamName);
    }

    [Fact]
    public void Bind_WhenBinderThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Bind<int, int>(_ => throw boom));
        Assert.Same(boom, thrown);
    }

    #endregion

    #region Match (generic)

    [Fact]
    public void Match_WhenSuccess_ReturnsOnSuccessValue()
    {
        // Arrange
        var failureCalled = false;
        var result = Result.Success(5);

        // Act
        var matched = result.Match(
            v => v * 10,
            _ =>
            {
                failureCalled = true;
                return -1;
            });

        // Assert
        Assert.Equal(50, matched);
        Assert.False(failureCalled);
    }

    [Fact]
    public void Match_WhenFailure_PassesSameErrorToOnFailure()
    {
        // Arrange
        var error = new TestError();
        var successCalled = false;
        IError? received = null;
        var result = Result.Failure<int>(error);

        // Act
        var matched = result.Match(
            v =>
            {
                successCalled = true;
                return v;
            },
            e =>
            {
                received = e;
                return -1;
            });

        // Assert
        Assert.Equal(-1, matched);
        Assert.Same(error, received);
        Assert.False(successCalled);
    }

    [Fact]
    public void Match_WhenDefault_ThrowsResultExceptionWithoutInvokingEitherBranch()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Match(
            v =>
            {
                called = true;
                return v;
            },
            _ =>
            {
                called = true;
                return -1;
            }));
        Assert.False(called);
    }

    [Fact]
    public void Match_WhenOnSuccessIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Match<int, int>(null!, _ => 0));
        Assert.Equal("onSuccess", thrown.ParamName);
    }

    [Fact]
    public void Match_WhenOnFailureIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Match<int, int>(v => v, null!));
        Assert.Equal("onFailure", thrown.ParamName);
    }

    [Fact]
    public void Match_WhenBranchThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Match<int, int>(v => v, _ => throw boom));
        Assert.Same(boom, thrown);
    }

    #endregion

    #region Match (non-generic)

    [Fact]
    public void Match_NonGenericWhenSuccess_ReturnsOnSuccessValue()
    {
        // Arrange
        var failureCalled = false;
        var result = Result.Success();

        // Act
        var matched = result.Match(
            () => "ok",
            _ =>
            {
                failureCalled = true;
                return "ko";
            });

        // Assert
        Assert.Equal("ok", matched);
        Assert.False(failureCalled);
    }

    [Fact]
    public void Match_NonGenericWhenFailure_PassesSameErrorToOnFailure()
    {
        // Arrange
        var error = new TestError();
        var successCalled = false;
        IError? received = null;
        var result = Result.Failure(error);

        // Act
        var matched = result.Match(
            () =>
            {
                successCalled = true;
                return "ok";
            },
            e =>
            {
                received = e;
                return "ko";
            });

        // Assert
        Assert.Equal("ko", matched);
        Assert.Same(error, received);
        Assert.False(successCalled);
    }

    [Fact]
    public void Match_NonGenericWhenDefault_ThrowsResultExceptionWithoutInvokingEitherBranch()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Match(
            () =>
            {
                called = true;
                return 1;
            },
            _ =>
            {
                called = true;
                return -1;
            }));
        Assert.False(called);
    }

    [Fact]
    public void Match_NonGenericWhenOnSuccessIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Match<int>(null!, _ => 0));
        Assert.Equal("onSuccess", thrown.ParamName);
    }

    [Fact]
    public void Match_NonGenericWhenOnFailureIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Match(() => 0, null!));
        Assert.Equal("onFailure", thrown.ParamName);
    }

    [Fact]
    public void Match_NonGenericWhenBranchThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Match<int>(() => throw boom, _ => 0));
        Assert.Same(boom, thrown);
    }

    #endregion

    #region Tap (generic)

    [Fact]
    public void Tap_WhenSuccess_InvokesActionWithValueAndReturnsSameResult()
    {
        // Arrange
        var seen = 0;
        var result = Result.Success(42);

        // Act
        var tapped = result.Tap(v => seen = v);

        // Assert
        Assert.Equal(42, seen);
        Assert.Equal(result, tapped);
        Assert.True(tapped.IsSuccess);
        Assert.Equal(42, tapped.Value);
    }

    [Fact]
    public void Tap_WhenFailure_DoesNotInvokeActionAndPropagatesSameError()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var tapped = result.Tap(_ => called = true);

        // Assert
        Assert.False(called);
        Assert.True(tapped.IsFailure);
        Assert.Same(error, tapped.Error);
    }

    [Fact]
    public void Tap_WhenDefault_DoesNothingAndReturnsDefault()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var tapped = result.Tap(_ => called = true);

        // Assert
        Assert.False(called);
        Assert.False(tapped.IsSuccess);
        Assert.False(tapped.IsFailure);
        Assert.Equal(default(Result<int>), tapped);
    }

    [Fact]
    public void Tap_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Tap((Action<int>)null!));
        Assert.Equal("action", thrown.ParamName);
    }

    [Fact]
    public void Tap_WhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Tap(Throwing<int>(boom)));
        Assert.Same(boom, thrown);
    }

    [Fact]
    public async Task Tap_WithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success(42);

        // Act
        var tapped = await AsyncSettle.Settle(result.Tap(async _ =>
        {
            await Task.Yield();
            sideEffectRan = true;
        }));

        // Assert
        Assert.True(sideEffectRan);
        Assert.Equal(result, tapped);
    }

    [Fact]
    public async Task Tap_WithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(42);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.Tap(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        Assert.Same(boom, thrown);
    }

    #endregion

    #region Tap (non-generic)

    [Fact]
    public void Tap_NonGenericWhenSuccess_InvokesActionAndReturnsSameResult()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        var tapped = result.Tap(() => called = true);

        // Assert
        Assert.True(called);
        Assert.Equal(result, tapped);
        Assert.True(tapped.IsSuccess);
    }

    [Fact]
    public void Tap_NonGenericWhenFailure_DoesNotInvokeActionAndPropagatesSameError()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var tapped = result.Tap(() => called = true);

        // Assert
        Assert.False(called);
        Assert.True(tapped.IsFailure);
        Assert.Same(error, tapped.Error);
    }

    [Fact]
    public void Tap_NonGenericWhenDefault_DoesNothingAndReturnsDefault()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act
        var tapped = result.Tap(() => called = true);

        // Assert
        Assert.False(called);
        Assert.False(tapped.IsSuccess);
        Assert.False(tapped.IsFailure);
        Assert.Equal(default(Result), tapped);
    }

    [Fact]
    public void Tap_NonGenericWhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Tap((Action)null!));
        Assert.Equal("action", thrown.ParamName);
    }

    [Fact]
    public void Tap_NonGenericWhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Tap(Throwing(boom)));
        Assert.Same(boom, thrown);
    }

    [Fact]
    public async Task Tap_NonGenericWithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success();

        // Act
        var tapped = await AsyncSettle.Settle(result.Tap(async () =>
        {
            await Task.Yield();
            sideEffectRan = true;
        }));

        // Assert
        Assert.True(sideEffectRan);
        Assert.Equal(result, tapped);
    }

    [Fact]
    public async Task Tap_NonGenericWithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.Tap(async () =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        Assert.Same(boom, thrown);
    }

    #endregion

    #region TapError (generic)

    [Fact]
    public void TapError_WhenFailure_InvokesActionWithSameErrorAndReturnsSameResult()
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var tapped = result.TapError(e => seen = e);

        // Assert
        Assert.Same(error, seen);
        Assert.Equal(result, tapped);
        Assert.True(tapped.IsFailure);
        Assert.Same(error, tapped.Error);
    }

    [Fact]
    public void TapError_WhenSuccess_DoesNotInvokeActionAndKeepsValue()
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var tapped = result.TapError(_ => called = true);

        // Assert
        Assert.False(called);
        Assert.True(tapped.IsSuccess);
        Assert.Equal(42, tapped.Value);
    }

    [Fact]
    public void TapError_WhenDefault_DoesNothingAndReturnsDefault()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var tapped = result.TapError(_ => called = true);

        // Assert
        Assert.False(called);
        Assert.Equal(default(Result<int>), tapped);
    }

    [Fact]
    public void TapError_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.TapError((Action<IError>)null!));
        Assert.Equal("action", thrown.ParamName);
    }

    [Fact]
    public void TapError_WhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.TapError(Throwing<IError>(boom)));
        Assert.Same(boom, thrown);
    }

    [Fact]
    public async Task TapError_WithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var tapped = await AsyncSettle.Settle(result.TapError(async e =>
        {
            await Task.Yield();
            seen = e;
        }));

        // Assert
        Assert.Same(error, seen);
        Assert.Equal(result, tapped);
    }

    [Fact]
    public async Task TapError_WithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.TapError(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        Assert.Same(boom, thrown);
    }

    #endregion

    #region TapError (non-generic)

    [Fact]
    public void TapError_NonGenericWhenFailure_InvokesActionWithSameErrorAndReturnsSameResult()
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var tapped = result.TapError(e => seen = e);

        // Assert
        Assert.Same(error, seen);
        Assert.Equal(result, tapped);
        Assert.True(tapped.IsFailure);
        Assert.Same(error, tapped.Error);
    }

    [Fact]
    public void TapError_NonGenericWhenSuccess_DoesNotInvokeAction()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        var tapped = result.TapError(_ => called = true);

        // Assert
        Assert.False(called);
        Assert.True(tapped.IsSuccess);
    }

    [Fact]
    public void TapError_NonGenericWhenDefault_DoesNothingAndReturnsDefault()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act
        var tapped = result.TapError(_ => called = true);

        // Assert
        Assert.False(called);
        Assert.Equal(default(Result), tapped);
    }

    [Fact]
    public void TapError_NonGenericWhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.TapError((Action<IError>)null!));
        Assert.Equal("action", thrown.ParamName);
    }

    [Fact]
    public void TapError_NonGenericWhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.TapError(Throwing<IError>(boom)));
        Assert.Same(boom, thrown);
    }

    [Fact]
    public async Task TapError_NonGenericWithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var tapped = await AsyncSettle.Settle(result.TapError(async e =>
        {
            await Task.Yield();
            seen = e;
        }));

        // Assert
        Assert.Same(error, seen);
        Assert.Equal(result, tapped);
    }

    [Fact]
    public async Task TapError_NonGenericWithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure(new TestError());

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.TapError(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        Assert.Same(boom, thrown);
    }

    #endregion

    #region Ensure

    [Fact]
    public void Ensure_WhenSuccessAndPredicateTrue_ReturnsSameResult()
    {
        // Arrange
        var result = Result.Success(10);

        // Act
        var ensured = result.Ensure(x => x > 5, new TestError());

        // Assert
        Assert.Equal(result, ensured);
        Assert.True(ensured.IsSuccess);
        Assert.Equal(10, ensured.Value);
    }

    [Fact]
    public void Ensure_WhenPredicateFalse_ReturnsFailureWithSameError()
    {
        // Arrange
        var error = new TestError("too small");
        var result = Result.Success(1);

        // Act
        var ensured = result.Ensure(x => x > 5, error);

        // Assert
        Assert.True(ensured.IsFailure);
        Assert.Same(error, ensured.Error);
    }

    [Fact]
    public void Ensure_WhenFailure_PropagatesOriginalErrorWithoutInvokingPredicate()
    {
        // Arrange
        var called = false;
        var original = new TestError();
        var result = Result.Failure<int>(original);

        // Act
        var ensured = result.Ensure(
            _ =>
            {
                called = true;
                return false;
            },
            new OtherError());

        // Assert
        Assert.False(called);
        Assert.True(ensured.IsFailure);
        Assert.Same(original, ensured.Error);
    }

    [Fact]
    public void Ensure_WhenDefault_ThrowsResultException()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Ensure(
            _ =>
            {
                called = true;
                return true;
            },
            new TestError()));
        Assert.False(called);
    }

    [Fact]
    public void Ensure_WhenPredicateIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Ensure(null!, new TestError()));
        Assert.Equal("predicate", thrown.ParamName);
    }

    [Fact]
    public void Ensure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Ensure(_ => true, null!));
        Assert.Equal("error", thrown.ParamName);
    }

    [Fact]
    public void Ensure_WhenPredicateThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Ensure(_ => throw boom, new TestError()));
        Assert.Same(boom, thrown);
    }

    #endregion

    #region MapError

    [Fact]
    public void MapError_WhenFailure_ReturnsFailureWithMappedError()
    {
        // Arrange
        var original = new TestError();
        var replacement = new OtherError();
        IError? received = null;
        var result = Result.Failure<int>(original);

        // Act
        var mapped = result.MapError(e =>
        {
            received = e;
            return replacement;
        });

        // Assert
        Assert.Same(original, received);
        Assert.True(mapped.IsFailure);
        Assert.Same(replacement, mapped.Error);
    }

    [Fact]
    public void MapError_WhenSuccess_ReturnsSameResultWithoutInvokingMapper()
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var mapped = result.MapError(e =>
        {
            called = true;
            return e;
        });

        // Assert
        Assert.False(called);
        Assert.Equal(result, mapped);
        Assert.True(mapped.IsSuccess);
        Assert.Equal(42, mapped.Value);
    }

    [Fact]
    public void MapError_WhenDefault_ThrowsResultExceptionWithoutInvokingMapper()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.MapError(e =>
        {
            called = true;
            return e;
        }));
        Assert.False(called);
    }

    [Fact]
    public void MapError_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.MapError(null!));
        Assert.Equal("mapper", thrown.ParamName);
    }

    [Fact]
    public void MapError_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.MapError(_ => null!));
        Assert.Equal("error", thrown.ParamName);
    }

    [Fact]
    public void MapError_WhenMapperThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.MapError(_ => throw boom));
        Assert.Same(boom, thrown);
    }

    #endregion

    #region Compensate

    [Fact]
    public void Compensate_WhenFailure_ReturnsFallbackSuccess()
    {
        // Arrange
        var original = new TestError();
        IError? received = null;
        var result = Result.Failure<int>(original);

        // Act
        var compensated = result.Compensate(e =>
        {
            received = e;
            return Result.Success(-1);
        });

        // Assert
        Assert.Same(original, received);
        Assert.True(compensated.IsSuccess);
        Assert.Equal(-1, compensated.Value);
    }

    [Fact]
    public void Compensate_WhenFailureAndFallbackFails_ReturnsFallbackError()
    {
        // Arrange
        var fallbackError = new OtherError();
        var result = Result.Failure<int>(new TestError());

        // Act
        var compensated = result.Compensate(_ => Result.Failure<int>(fallbackError));

        // Assert
        Assert.True(compensated.IsFailure);
        Assert.Same(fallbackError, compensated.Error);
    }

    [Fact]
    public void Compensate_WhenSuccess_ReturnsSameResultWithoutInvokingFallback()
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var compensated = result.Compensate(_ =>
        {
            called = true;
            return Result.Success(0);
        });

        // Assert
        Assert.False(called);
        Assert.Equal(result, compensated);
        Assert.True(compensated.IsSuccess);
        Assert.Equal(42, compensated.Value);
    }

    [Fact]
    public void Compensate_WhenDefault_ThrowsResultExceptionWithoutInvokingFallback()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Compensate(_ =>
        {
            called = true;
            return Result.Success(0);
        }));
        Assert.False(called);
    }

    [Fact]
    public void Compensate_WhenFallbackIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Compensate(null!));
        Assert.Equal("fallback", thrown.ParamName);
    }

    [Fact]
    public void Compensate_WhenFallbackThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Compensate(_ => throw boom));
        Assert.Same(boom, thrown);
    }

    #endregion

    #region GetValueOrDefault

    [Fact]
    public void GetValueOrDefault_WhenSuccess_ReturnsValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var value = result.GetValueOrDefault(-1);

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void GetValueOrDefault_WhenFailure_ReturnsFallback()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act
        var value = result.GetValueOrDefault(-1);

        // Assert
        Assert.Equal(-1, value);
    }

    [Fact]
    public void GetValueOrDefault_WhenDefault_ReturnsFallback()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var value = result.GetValueOrDefault(-1);

        // Assert
        Assert.Equal(-1, value);
    }

    [Fact]
    public void GetValueOrDefault_WithoutArgumentWhenSuccess_ReturnsValue()
    {
        // Arrange
        var result = Result.Success("value");

        // Act
        var value = result.GetValueOrDefault<string>();

        // Assert
        Assert.Equal("value", value);
    }

    [Fact]
    public void GetValueOrDefault_WithoutArgumentWhenFailure_ReturnsDefaultOfT()
    {
        // Arrange
        var valueResult = Result.Failure<int>(new TestError());
        var referenceResult = Result.Failure<string>(new TestError());

        // Act
        var value = valueResult.GetValueOrDefault();
        string? reference = referenceResult.GetValueOrDefault<string>();

        // Assert
        Assert.Equal(0, value);
        Assert.Null(reference);
    }

    [Fact]
    public void GetValueOrDefault_WithoutArgumentWhenDefault_ReturnsDefaultOfT()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var value = result.GetValueOrDefault();

        // Assert
        Assert.Equal(0, value);
    }

    #endregion

    #region GetValueOrThrow

    [Fact]
    public void GetValueOrThrow_WhenSuccess_ReturnsValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var value = result.GetValueOrThrow();

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void GetValueOrThrow_WithFactoryWhenSuccess_ReturnsValueWithoutInvokingFactory()
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var value = result.GetValueOrThrow(_ =>
        {
            called = true;
            return new InvalidOperationException();
        });

        // Assert
        Assert.Equal(42, value);
        Assert.False(called);
    }

    [Fact]
    public void GetValueOrThrow_WhenFailure_ThrowsResultExceptionCarryingError()
    {
        // Arrange
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var thrown = Assert.Throws<ResultException>(() => result.GetValueOrThrow());

        // Assert
        Assert.Single(thrown.Errors);
        Assert.Same(error, thrown.Errors[0]);
    }

    [Fact]
    public void GetValueOrThrow_WithFactoryWhenFailure_ThrowsFactoryException()
    {
        // Arrange
        var error = new TestError();
        var custom = new InvalidOperationException("custom");
        IError? received = null;
        var result = Result.Failure<int>(error);

        // Act
        var thrown = Assert.Throws<InvalidOperationException>(() => result.GetValueOrThrow(e =>
        {
            received = e;
            return custom;
        }));

        // Assert
        Assert.Same(custom, thrown);
        Assert.Same(error, received);
    }

    [Fact]
    public void GetValueOrThrow_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.GetValueOrThrow());
    }

    [Fact]
    public void GetValueOrThrow_WithFactoryWhenDefault_ThrowsResultExceptionWithoutInvokingFactory()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var thrown = Assert.ThrowsAny<InvalidOperationException>(() => result.GetValueOrThrow(_ =>
        {
            called = true;
            return new InvalidOperationException("custom");
        }));

        // Assert
        Assert.IsType<ResultException>(thrown);
        Assert.False(called);
    }

    [Fact]
    public void GetValueOrThrow_WithFactoryReturningNullWhenFailure_ThrowsInvalidOperationException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act
        var thrown = Record.Exception(() => result.GetValueOrThrow(_ => null!));

        // Assert
        var exact = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains("null", exact.Message);
    }

    [Fact]
    public void GetValueOrThrow_WithFactoryReturningNullWhenSuccess_ReturnsValueWithoutInvokingFactory()
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var value = result.GetValueOrThrow(_ =>
        {
            called = true;
            return null!;
        });

        // Assert
        Assert.Equal(42, value);
        Assert.False(called);
    }

    #endregion

    #region TryGetValue

    [Fact]
    public void TryGetValue_WhenSuccess_ReturnsTrueAndValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var found = result.TryGetValue(out var value);

        // Assert
        Assert.True(found);
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGetValue_WhenFailure_ReturnsFalseAndDefault()
    {
        // Arrange
        var result = Result.Failure<string>(new TestError());

        // Act
        var found = result.TryGetValue(out var value);

        // Assert
        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetValue_WhenDefault_ReturnsFalseAndDefault()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var found = result.TryGetValue(out var value);

        // Assert
        Assert.False(found);
        Assert.Equal(0, value);
    }

    #endregion

    #region Finally (generic)

    [Fact]
    public void Finally_WhenSuccess_InvokesActionWithResultAndReturnsSameResult()
    {
        // Arrange
        Result<int>? seen = null;
        var result = Result.Success(42);

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        Assert.Equal(result, seen);
        Assert.Equal(result, finished);
    }

    [Fact]
    public void Finally_WhenFailure_InvokesActionWithResultAndReturnsSameResult()
    {
        // Arrange
        Result<int>? seen = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        Assert.Equal(result, seen);
        Assert.True(finished.IsFailure);
        Assert.Same(error, finished.Error);
    }

    [Fact]
    public void Finally_WhenDefault_InvokesActionWithDefaultAndReturnsDefault()
    {
        // Arrange
        Result<int>? seen = null;
        Result<int> result = default;

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        Assert.Equal(default(Result<int>), seen);
        Assert.Equal(default(Result<int>), finished);
    }

    [Fact]
    public void Finally_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Finally((Action<Result<int>>)null!));
        Assert.Equal("action", thrown.ParamName);
    }

    [Fact]
    public void Finally_WhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Finally(Throwing<Result<int>>(boom)));
        Assert.Same(boom, thrown);
    }

    [Fact]
    public async Task Finally_WithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success(42);

        // Act
        var finished = await AsyncSettle.Settle(result.Finally(async _ =>
        {
            await Task.Yield();
            sideEffectRan = true;
        }));

        // Assert
        Assert.True(sideEffectRan);
        Assert.Equal(result, finished);
    }

    [Fact]
    public async Task Finally_WithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(42);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.Finally(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        Assert.Same(boom, thrown);
    }

    #endregion

    #region Finally (non-generic)

    [Fact]
    public void Finally_NonGenericWhenSuccess_InvokesActionWithResultAndReturnsSameResult()
    {
        // Arrange
        Result? seen = null;
        var result = Result.Success();

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        Assert.Equal(result, seen);
        Assert.Equal(result, finished);
    }

    [Fact]
    public void Finally_NonGenericWhenFailure_InvokesActionWithResultAndReturnsSameResult()
    {
        // Arrange
        Result? seen = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        Assert.Equal(result, seen);
        Assert.True(finished.IsFailure);
        Assert.Same(error, finished.Error);
    }

    [Fact]
    public void Finally_NonGenericWhenDefault_InvokesActionWithDefaultAndReturnsDefault()
    {
        // Arrange
        Result? seen = null;
        Result result = default;

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        Assert.Equal(default(Result), seen);
        Assert.Equal(default(Result), finished);
    }

    [Fact]
    public void Finally_NonGenericWhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => result.Finally((Action<Result>)null!));
        Assert.Equal("action", thrown.ParamName);
    }

    [Fact]
    public void Finally_NonGenericWhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act + Assert
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Finally(Throwing<Result>(boom)));
        Assert.Same(boom, thrown);
    }

    [Fact]
    public async Task Finally_NonGenericWithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success();

        // Act
        var finished = await AsyncSettle.Settle(result.Finally(async _ =>
        {
            await Task.Yield();
            sideEffectRan = true;
        }));

        // Assert
        Assert.True(sideEffectRan);
        Assert.Equal(result, finished);
    }

    [Fact]
    public async Task Finally_NonGenericWithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.Finally(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        Assert.Same(boom, thrown);
    }

    #endregion

    #region ToResult

    [Fact]
    public void ToResult_WhenSuccess_ReturnsNonGenericSuccess()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var converted = result.ToResult();

        // Assert
        Assert.True(converted.IsSuccess);
        Assert.Equal(Result.Success(), converted);
    }

    [Fact]
    public void ToResult_WhenFailure_ReturnsNonGenericFailureWithSameError()
    {
        // Arrange
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var converted = result.ToResult();

        // Assert
        Assert.True(converted.IsFailure);
        Assert.Same(error, converted.Error);
    }

    [Fact]
    public void ToResult_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Assert.Throws<ResultException>(() => result.ToResult());
    }

    #endregion

    #region Chained

    [Fact]
    public void Pipeline_WhenEveryStepSucceeds_FlowsOnSuccessRail()
    {
        // Arrange
        var tapped = new List<int>();
        var start = Result.Success(2);

        // Act
        var outcome = start
            .Map(x => x + 1)
            .Bind(x => Result.Success(x * 10))
            .Ensure(x => x > 10, new TestError("too small"))
            .Tap(tapped.Add)
            .Match(x => $"value {x}", e => e.Code.Value);

        // Assert
        Assert.Equal("value 30", outcome);
        Assert.Equal(30, Assert.Single(tapped));
    }

    [Fact]
    public void Pipeline_WhenStepFails_ShortCircuitsRemainingSteps()
    {
        // Arrange
        var error = new NotFoundTestError(7);
        var afterFailureCalls = 0;
        IError? tappedError = null;
        var start = Result.Success(7);

        // Act
        var outcome = start
            .Bind(id => Result.Failure<int>(new NotFoundTestError(id)))
            .Map(x =>
            {
                afterFailureCalls++;
                return x * 2;
            })
            .Ensure(
                _ =>
                {
                    afterFailureCalls++;
                    return true;
                },
                new TestError())
            .Tap(_ => afterFailureCalls++)
            .TapError(e => tappedError = e);

        // Assert
        Assert.Equal(0, afterFailureCalls);
        Assert.True(outcome.IsFailure);
        Assert.Equal(error, outcome.Error);
        Assert.Same(outcome.Error, tappedError);
    }

    [Fact]
    public void Pipeline_WhenFailureIsCompensated_ReturnsToSuccessRail()
    {
        // Arrange
        var start = Result.Failure<int>(new NotFoundTestError(1));

        // Act
        var outcome = start
            .MapError(e => new TestError(e.Message))
            .Compensate(_ => Result.Success(0))
            .Map(x => x + 1);

        // Assert
        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, outcome.Value);
    }

    #endregion

    // Typed as Action, so the call keeps binding to the Action overload once Phase 2 adds Func<..., Task> overloads.
    private static Action<T> Throwing<T>(Exception exception) => _ => throw exception;

    private static Action Throwing(Exception exception) => () => throw exception;
}

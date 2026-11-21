using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Extensions;

// MicroKit ADR-RESULT-001 D8 and D9 (michaelatsey/microkit@a12097b): the async operations of the non-generic Result, each in two forms, a Task-returning
// delegate without a token and a (…, CancellationToken) => ValueTask delegate with ct last. Arguments are validated at
// the call; the token is passed to the delegate; a cancellation the delegate throws propagates.
public sealed class NonGenericAsyncResultExtensionsTests
{
    private static readonly string[] Operations =
        ["Map", "Bind", "BindToValue", "Match", "Tap", "TapError", "Ensure", "MapError", "Compensate", "Finally"];

    private static readonly string[] NullArguments =
    [
        "Map.mapper", "Bind.binder", "BindToValue.binder", "Match.onSuccess", "Match.onFailure", "Tap.action",
        "TapError.action", "Ensure.predicate", "Ensure.error", "MapError.mapper", "Compensate.fallback", "Finally.action",
    ];

    public static TheoryData<string, bool> OperationsInBothForms
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var operation in Operations)
            {
                data.Add(operation, false);
                data.Add(operation, true);
            }

            return data;
        }
    }

    public static TheoryData<string> OperationNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var operation in Operations)
                data.Add(operation);
            return data;
        }
    }

    public static TheoryData<string, bool> NullArgumentsInBothForms
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var argument in NullArguments)
            {
                data.Add(argument, false);
                data.Add(argument, true);
            }

            return data;
        }
    }

    #region MapAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapAsync_WhenSuccess_ReturnsSuccessWithMapperValue(bool yield)
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result<int> mapped = await result.MapAsync(() => Later(yield, 42));

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(42, mapped.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapAsync_WithToken_WhenSuccess_ReturnsSuccessAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var result = Result.Success();

        // Act
        Result<int> mapped = await result.MapAsync(async ct =>
        {
            seen = ct;
            return await LaterValue(yield, 42);
        }, cts.Token);

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(42, mapped.Value);
        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public async Task MapAsync_WhenFailure_PropagatesSameErrorWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result<int> mapped = await result.MapAsync(async () =>
        {
            called = true;
            await Task.Yield();
            return 42;
        });

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Same(error, mapped.Error);
        Assert.False(called);
    }

    [Fact]
    public async Task MapAsync_WithToken_WhenFailure_PropagatesSameErrorWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result<int> mapped = await result.MapAsync(async ct =>
        {
            called = true;
            await Task.Yield();
            return 42;
        }, CancellationToken.None);

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Same(error, mapped.Error);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapAsync_WhenDefault_ThrowsResultExceptionWithoutCallingMapper(bool tokenForm)
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Func<Task<Result<int>>> act = async () => tokenForm
            ? await result.MapAsync(_ =>
            {
                called = true;
                return ValueTask.FromResult(1);
            })
            : await result.MapAsync(() =>
            {
                called = true;
                return Task.FromResult(1);
            });
        await Assert.ThrowsAsync<ResultException>(act);
        Assert.False(called);
    }

    #endregion

    #region BindAsync (Result -> Result)

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsync_WhenSuccess_ReturnsBinderResult(bool yield)
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success();

        // Act
        Result bound = await result.BindAsync(() => Later(yield, Result.Failure(error)));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsync_WithToken_WhenSuccess_ReturnsBinderResultAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var result = Result.Success();

        // Act
        Result bound = await result.BindAsync(async ct =>
        {
            seen = ct;
            return await LaterValue(yield, Result.Success());
        }, cts.Token);

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal(cts.Token, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsync_WhenFailure_PropagatesSameErrorWithoutCallingBinder(bool tokenForm)
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result bound = tokenForm
            ? await result.BindAsync(_ =>
            {
                called = true;
                return ValueTask.FromResult(Result.Success());
            }, ct: TestContext.Current.CancellationToken)
            : await result.BindAsync(() =>
            {
                called = true;
                return Task.FromResult(Result.Success());
            });

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsync_WhenDefault_ThrowsResultExceptionWithoutCallingBinder(bool tokenForm)
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Func<Task<Result>> act = async () => tokenForm
            ? await result.BindAsync(_ =>
            {
                called = true;
                return ValueTask.FromResult(Result.Success());
            })
            : await result.BindAsync(() =>
            {
                called = true;
                return Task.FromResult(Result.Success());
            });
        await Assert.ThrowsAsync<ResultException>(act);
        Assert.False(called);
    }

    #endregion

    #region BindAsync (Result -> Result<TOut>)

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsyncToValue_WhenSuccess_ReturnsBinderResult(bool yield)
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result<string> bound = await result.BindAsync(() => Later(yield, Result.Success("created")));

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal("created", bound.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsyncToValue_WithToken_WhenSuccess_ReturnsBinderResultAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var result = Result.Success();

        // Act
        Result<string> bound = await result.BindAsync(async ct =>
        {
            seen = ct;
            return await LaterValue(yield, Result.Success("created"));
        }, cts.Token);

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal("created", bound.Value);
        Assert.Equal(cts.Token, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsyncToValue_WhenFailure_PropagatesSameErrorWithoutCallingBinder(bool tokenForm)
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result<string> bound = tokenForm
            ? await result.BindAsync(_ =>
            {
                called = true;
                return ValueTask.FromResult(Result.Success("created"));
            }, ct: TestContext.Current.CancellationToken)
            : await result.BindAsync(() =>
            {
                called = true;
                return Task.FromResult(Result.Success("created"));
            });

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsyncToValue_WhenDefault_ThrowsResultExceptionWithoutCallingBinder(bool tokenForm)
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Func<Task<Result<int>>> act = async () => tokenForm
            ? await result.BindAsync(_ =>
            {
                called = true;
                return ValueTask.FromResult(Result.Success(1));
            })
            : await result.BindAsync(() =>
            {
                called = true;
                return Task.FromResult(Result.Success(1));
            });
        await Assert.ThrowsAsync<ResultException>(act);
        Assert.False(called);
    }

    #endregion

    #region MatchAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchAsync_WhenSuccess_ReturnsOnSuccessValue(bool yield)
    {
        // Arrange
        var result = Result.Success();

        // Act
        var matched = await result.MatchAsync(() => Later(yield, "ok"), _ => Later(yield, "ko"));

        // Assert
        Assert.Equal("ok", matched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchAsync_WhenFailure_PassesSameErrorToOnFailure(bool yield)
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var matched = await result.MatchAsync(() => Later(yield, "ok"), e =>
        {
            seen = e;
            return Later(yield, "ko");
        });

        // Assert
        Assert.Equal("ko", matched);
        Assert.Same(error, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchAsync_WithToken_WhenSuccess_ReturnsOnSuccessValueAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var result = Result.Success();

        // Act
        var matched = await result.MatchAsync(
            async ct =>
            {
                seen = ct;
                return await LaterValue(yield, "ok");
            },
            (_, _) => LaterValue(yield, "ko"),
            cts.Token);

        // Assert
        Assert.Equal("ok", matched);
        Assert.Equal(cts.Token, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchAsync_WithToken_WhenFailure_PassesSameErrorAndToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        IError? seenError = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var matched = await result.MatchAsync(
            _ => LaterValue(yield, "ok"),
            async (e, ct) =>
            {
                seenError = e;
                seenToken = ct;
                return await LaterValue(yield, "ko");
            },
            cts.Token);

        // Assert
        Assert.Equal("ko", matched);
        Assert.Same(error, seenError);
        Assert.Equal(cts.Token, seenToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchAsync_WhenDefault_ThrowsResultExceptionWithoutCallingEitherBranch(bool tokenForm)
    {
        // Arrange
        var calls = 0;
        Result result = default;

        // Act + Assert
        Func<Task<int>> act = async () => tokenForm
            ? await result.MatchAsync(
                _ =>
                {
                    calls++;
                    return ValueTask.FromResult(1);
                },
                (_, _) =>
                {
                    calls++;
                    return ValueTask.FromResult(2);
                })
            : await result.MatchAsync(
                () =>
                {
                    calls++;
                    return Task.FromResult(1);
                },
                _ =>
                {
                    calls++;
                    return Task.FromResult(2);
                });
        await Assert.ThrowsAsync<ResultException>(act);
        Assert.Equal(0, calls);
    }

    #endregion

    #region TapAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapAsync_WhenSuccess_AwaitsEffectAndReturnsSameResult(bool yield)
    {
        // Arrange
        var done = false;
        var result = Result.Success();

        // Act
        Result tapped = await result.TapAsync(async () =>
        {
            await Later(yield);
            done = true;
        });

        // Assert
        Assert.Equal(result, tapped);
        Assert.True(tapped.IsSuccess);
        Assert.True(done);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapAsync_WithToken_WhenSuccess_AwaitsEffectAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var done = false;
        var result = Result.Success();

        // Act
        Result tapped = await result.TapAsync(async ct =>
        {
            await LaterValue(yield, 0);
            seen = ct;
            done = true;
        }, cts.Token);

        // Assert
        Assert.True(tapped.IsSuccess);
        Assert.True(done);
        Assert.Equal(cts.Token, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapAsync_WhenFailure_ReturnsSameFailureWithoutCallingEffect(bool tokenForm)
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result tapped = tokenForm
            ? await result.TapAsync(_ =>
            {
                called = true;
                return ValueTask.CompletedTask;
            }, ct: TestContext.Current.CancellationToken)
            : await result.TapAsync(() =>
            {
                called = true;
                return Task.CompletedTask;
            });

        // Assert
        Assert.True(tapped.IsFailure);
        Assert.Same(error, tapped.Error);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapAsync_WhenDefault_ReturnsDefaultWithoutCallingEffect(bool tokenForm)
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act
        Result tapped = tokenForm
            ? await result.TapAsync(_ =>
            {
                called = true;
                return ValueTask.CompletedTask;
            }, ct: TestContext.Current.CancellationToken)
            : await result.TapAsync(() =>
            {
                called = true;
                return Task.CompletedTask;
            });

        // Assert
        Assert.False(tapped.IsSuccess);
        Assert.False(tapped.IsFailure);
        Assert.False(called);
    }

    #endregion

    #region TapErrorAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapErrorAsync_WhenFailure_AwaitsEffectWithSameError(bool yield)
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result tapped = await result.TapErrorAsync(async e =>
        {
            await Later(yield);
            seen = e;
        });

        // Assert
        Assert.True(tapped.IsFailure);
        Assert.Same(error, tapped.Error);
        Assert.Same(error, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapErrorAsync_WithToken_WhenFailure_AwaitsEffectAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        IError? seenError = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result tapped = await result.TapErrorAsync(async (e, ct) =>
        {
            await LaterValue(yield, 0);
            seenError = e;
            seenToken = ct;
        }, cts.Token);

        // Assert
        Assert.True(tapped.IsFailure);
        Assert.Same(error, tapped.Error);
        Assert.Same(error, seenError);
        Assert.Equal(cts.Token, seenToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapErrorAsync_WhenSuccess_ReturnsSuccessWithoutCallingEffect(bool tokenForm)
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        Result tapped = tokenForm
            ? await result.TapErrorAsync((_, _) =>
            {
                called = true;
                return ValueTask.CompletedTask;
            }, ct: TestContext.Current.CancellationToken)
            : await result.TapErrorAsync(_ =>
            {
                called = true;
                return Task.CompletedTask;
            });

        // Assert
        Assert.True(tapped.IsSuccess);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapErrorAsync_WhenDefault_ReturnsDefaultWithoutCallingEffect(bool tokenForm)
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act
        Result tapped = tokenForm
            ? await result.TapErrorAsync((_, _) =>
            {
                called = true;
                return ValueTask.CompletedTask;
            }, ct: TestContext.Current.CancellationToken)
            : await result.TapErrorAsync(_ =>
            {
                called = true;
                return Task.CompletedTask;
            });

        // Assert
        Assert.False(tapped.IsSuccess);
        Assert.False(tapped.IsFailure);
        Assert.False(called);
    }

    #endregion

    #region EnsureAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAsync_WhenSuccessAndPredicateTrue_ReturnsSuccess(bool yield)
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result ensured = await result.EnsureAsync(() => Later(yield, true), new TestError());

        // Assert
        Assert.True(ensured.IsSuccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAsync_WhenSuccessAndPredicateFalse_ReturnsFailureWithError(bool yield)
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success();

        // Act
        Result ensured = await result.EnsureAsync(() => Later(yield, false), error);

        // Assert
        Assert.True(ensured.IsFailure);
        Assert.Same(error, ensured.Error);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task EnsureAsync_WithToken_WhenSuccess_AppliesPredicateAndPassesToken(bool yield, bool holds)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var error = new OtherError();
        var result = Result.Success();

        // Act
        Result ensured = await result.EnsureAsync(async ct =>
        {
            seen = ct;
            return await LaterValue(yield, holds);
        }, error, cts.Token);

        // Assert
        Assert.Equal(cts.Token, seen);
        Assert.Equal(holds, ensured.IsSuccess);
        if (!holds)
            Assert.Same(error, ensured.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAsync_WhenFailure_PropagatesOriginalErrorWithoutCallingPredicate(bool tokenForm)
    {
        // Arrange
        var called = false;
        var original = new TestError();
        var result = Result.Failure(original);

        // Act
        Result ensured = tokenForm
            ? await result.EnsureAsync(_ =>
            {
                called = true;
                return ValueTask.FromResult(false);
            }, new OtherError(), ct: TestContext.Current.CancellationToken)
            : await result.EnsureAsync(() =>
            {
                called = true;
                return Task.FromResult(false);
            }, new OtherError());

        // Assert
        Assert.True(ensured.IsFailure);
        Assert.Same(original, ensured.Error);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAsync_WhenDefault_ThrowsResultExceptionWithoutCallingPredicate(bool tokenForm)
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Func<Task<Result>> act = async () => tokenForm
            ? await result.EnsureAsync(_ =>
            {
                called = true;
                return ValueTask.FromResult(true);
            }, new TestError())
            : await result.EnsureAsync(() =>
            {
                called = true;
                return Task.FromResult(true);
            }, new TestError());
        await Assert.ThrowsAsync<ResultException>(act);
        Assert.False(called);
    }

    #endregion

    #region MapErrorAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapErrorAsync_WhenFailure_ReturnsFailureWithMappedError(bool yield)
    {
        // Arrange
        IError? seen = null;
        var original = new TestError();
        IError replacement = new OtherError();
        var result = Result.Failure(original);

        // Act
        Result mapped = await result.MapErrorAsync(e =>
        {
            seen = e;
            return Later(yield, replacement);
        });

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Same(replacement, mapped.Error);
        Assert.Same(original, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapErrorAsync_WithToken_WhenFailure_ReturnsMappedErrorAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        IError replacement = new OtherError();
        var result = Result.Failure(new TestError());

        // Act
        Result mapped = await result.MapErrorAsync(async (_, ct) =>
        {
            seen = ct;
            return await LaterValue(yield, replacement);
        }, cts.Token);

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Same(replacement, mapped.Error);
        Assert.Equal(cts.Token, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapErrorAsync_WhenSuccess_ReturnsSuccessWithoutCallingMapper(bool tokenForm)
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        Result mapped = tokenForm
            ? await result.MapErrorAsync((e, _) =>
            {
                called = true;
                return ValueTask.FromResult(e);
            }, ct: TestContext.Current.CancellationToken)
            : await result.MapErrorAsync(e =>
            {
                called = true;
                return Task.FromResult(e);
            });

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapErrorAsync_WhenDefault_ThrowsResultExceptionWithoutCallingMapper(bool tokenForm)
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Func<Task<Result>> act = async () => tokenForm
            ? await result.MapErrorAsync((e, _) =>
            {
                called = true;
                return ValueTask.FromResult(e);
            })
            : await result.MapErrorAsync(e =>
            {
                called = true;
                return Task.FromResult(e);
            });
        await Assert.ThrowsAsync<ResultException>(act);
        Assert.False(called);
    }

    #endregion

    #region CompensateAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompensateAsync_WhenFailure_ReturnsFallbackResult(bool yield)
    {
        // Arrange
        IError? seen = null;
        var original = new TestError();
        var result = Result.Failure(original);

        // Act
        Result compensated = await result.CompensateAsync(e =>
        {
            seen = e;
            return Later(yield, Result.Success());
        });

        // Assert
        Assert.True(compensated.IsSuccess);
        Assert.Same(original, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompensateAsync_WithToken_WhenFailure_ReturnsFallbackResultAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var replacement = new OtherError();
        var result = Result.Failure(new TestError());

        // Act
        Result compensated = await result.CompensateAsync(async (_, ct) =>
        {
            seen = ct;
            return await LaterValue(yield, Result.Failure(replacement));
        }, cts.Token);

        // Assert
        Assert.True(compensated.IsFailure);
        Assert.Same(replacement, compensated.Error);
        Assert.Equal(cts.Token, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompensateAsync_WhenSuccess_ReturnsSuccessWithoutCallingFallback(bool tokenForm)
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        Result compensated = tokenForm
            ? await result.CompensateAsync((_, _) =>
            {
                called = true;
                return ValueTask.FromResult(Result.Failure(new TestError()));
            }, ct: TestContext.Current.CancellationToken)
            : await result.CompensateAsync(_ =>
            {
                called = true;
                return Task.FromResult(Result.Failure(new TestError()));
            });

        // Assert
        Assert.True(compensated.IsSuccess);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompensateAsync_WhenDefault_ThrowsResultExceptionWithoutCallingFallback(bool tokenForm)
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Func<Task<Result>> act = async () => tokenForm
            ? await result.CompensateAsync((_, _) =>
            {
                called = true;
                return ValueTask.FromResult(Result.Success());
            })
            : await result.CompensateAsync(_ =>
            {
                called = true;
                return Task.FromResult(Result.Success());
            });
        await Assert.ThrowsAsync<ResultException>(act);
        Assert.False(called);
    }

    #endregion

    #region FinallyAsync

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FinallyAsync_OnEitherRail_AwaitsActionWithSameResult(bool yield, bool success)
    {
        // Arrange
        Result? seen = null;
        var result = success ? Result.Success() : Result.Failure(new TestError());

        // Act
        Result returned = await result.FinallyAsync(async r =>
        {
            await Later(yield);
            seen = r;
        });

        // Assert
        Assert.Equal(result, returned);
        Assert.Equal(success, returned.IsSuccess);
        Assert.Equal(result, seen);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FinallyAsync_WithToken_OnEitherRail_AwaitsActionAndPassesToken(bool yield, bool success)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        Result? seen = null;
        var result = success ? Result.Success() : Result.Failure(new TestError());

        // Act
        Result returned = await result.FinallyAsync(async (r, ct) =>
        {
            await LaterValue(yield, 0);
            seen = r;
            seenToken = ct;
        }, cts.Token);

        // Assert
        Assert.Equal(result, returned);
        Assert.Equal(success, returned.IsSuccess);
        Assert.Equal(result, seen);
        Assert.Equal(cts.Token, seenToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinallyAsync_WhenDefault_CallsActionWithDefaultAndReturnsIt(bool tokenForm)
    {
        // Arrange
        Result? seen = null;
        Result result = default;

        // Act
        Result returned = tokenForm
            ? await result.FinallyAsync((r, _) =>
            {
                seen = r;
                return ValueTask.CompletedTask;
            }, ct: TestContext.Current.CancellationToken)
            : await result.FinallyAsync(r =>
            {
                seen = r;
                return Task.CompletedTask;
            });

        // Assert
        Assert.False(returned.IsSuccess);
        Assert.False(returned.IsFailure);
        Assert.Equal(default(Result), seen);
    }

    #endregion

    #region Cross-cutting: null guards, cancellation, default token, chaining

    [Theory]
    [MemberData(nameof(NullArgumentsInBothForms))]
    public void Extension_WithNullArgument_ThrowsArgumentNullExceptionAtTheCall(string argument, bool tokenForm)
    {
        // Arrange
        Task? pending = null;

        // Act
        var thrown = Record.Exception(() =>
        {
            pending = tokenForm ? CallTokenFormWithNull(argument) : CallTaskFormWithNull(argument);
        });

        // Assert
        Assert.Null(pending);
        Assert.Equal(argument[(argument.IndexOf('.', StringComparison.Ordinal) + 1)..], Assert.IsType<ArgumentNullException>(thrown).ParamName);
    }

    [Theory]
    [MemberData(nameof(OperationsInBothForms))]
    public async Task Extension_WhenDelegateThrowsOperationCanceledException_Propagates(string operation, bool tokenForm)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var canceled = new OperationCanceledException(cts.Token);

        // Act
        // Record.ExceptionAsync awaits the task itself: Should.ThrowAsync reports a canceled task as a new
        // TaskCanceledException, not as the exception the delegate threw.
        var thrown = await Record.ExceptionAsync(async () =>
            await Invoke(operation, tokenForm, async _ =>
            {
                await Task.Yield();
                throw canceled;
            }, cts.Token));

        // Assert
        Assert.Same(canceled, thrown);
    }

    [Theory]
    [MemberData(nameof(OperationsInBothForms))]
    public async Task Extension_WhenDelegateThrows_PropagatesSameException(string operation, bool tokenForm)
    {
        // Arrange
        var boom = new InvalidOperationException("boom");

        // Act
        var thrown = await Record.ExceptionAsync(async () =>
            await Invoke(operation, tokenForm, async _ =>
            {
                await Task.Yield();
                throw boom;
            }, CancellationToken.None));

        // Assert
        Assert.Same(boom, thrown);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task Extension_WithTokenButNoTokenArgument_PassesCancellationTokenNone(string operation)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var seen = cts.Token;

        // Act
        var outcome = await InvokeTokenFormWithoutToken(operation, ct =>
        {
            seen = ct;
            return ValueTask.CompletedTask;
        });

        // Assert
        Assert.True(outcome);
        Assert.Equal(CancellationToken.None, seen);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task Extension_WithToken_WhenTokenAlreadyCanceled_StillInvokesDelegate(string operation)
    {
        // Arrange — the extension neither checks the token nor short-circuits on it (MicroKit ADR-RESULT-001 D8 (michaelatsey/microkit@a12097b))
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var invoked = false;

        // Act
        var outcome = await Invoke(operation, true, ct =>
        {
            invoked = ct.IsCancellationRequested;
            return ValueTask.CompletedTask;
        }, cts.Token);

        // Assert
        Assert.True(outcome);
        Assert.True(invoked);
    }

    [Fact]
    public async Task Chain_OfTokenOverloads_PassesSameTokenToEveryStep()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var seen = new List<CancellationToken>();
        var result = Result.Success();

        // Act
        Result ensured = await result.EnsureAsync(async ct =>
        {
            seen.Add(ct);
            await Task.Yield();
            return true;
        }, new TestError(), cts.Token);
        Result tapped = await ensured.TapAsync(async ct =>
        {
            seen.Add(ct);
            await Task.Yield();
        }, cts.Token);
        Result<int> mapped = await tapped.MapAsync(async ct =>
        {
            seen.Add(ct);
            await Task.Yield();
            return 5;
        }, cts.Token);

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(5, mapped.Value);
        Assert.Equal(3, seen.Count);
        Assert.All(seen, t => Assert.True(t == cts.Token));
    }

    #endregion

    #region Helpers

    // Runs the operation on a result whose rail invokes the delegate (a failure for TapError, MapError and Compensate;
    // a success otherwise). The delegate runs body with the token it receives (None in the Task form) and returns a
    // value that keeps the result on its rail. Returns true when the outcome is the expected one.
    private static async Task<bool> Invoke(
        string operation, bool tokenForm, Func<CancellationToken, ValueTask> body, CancellationToken ct)
    {
        var error = new TestError();
        var success = Result.Success();
        var failure = Result.Failure(error);

        if (!tokenForm)
        {
            return operation switch
            {
                "Map" => (await success.MapAsync(async () =>
                {
                    await body(CancellationToken.None);
                    return 1;
                })).IsSuccess,
                "Bind" => (await success.BindAsync(async () =>
                {
                    await body(CancellationToken.None);
                    return Result.Success();
                })).IsSuccess,
                "BindToValue" => (await success.BindAsync(async () =>
                {
                    await body(CancellationToken.None);
                    return Result.Success(1);
                })).IsSuccess,
                "Match" => await success.MatchAsync(
                    async () =>
                    {
                        await body(CancellationToken.None);
                        return true;
                    },
                    _ => Task.FromResult(false)),
                "Tap" => (await success.TapAsync(async () => await body(CancellationToken.None))).IsSuccess,
                "TapError" => (await failure.TapErrorAsync(async _ => await body(CancellationToken.None))).IsFailure,
                "Ensure" => (await success.EnsureAsync(
                    async () =>
                    {
                        await body(CancellationToken.None);
                        return true;
                    },
                    new OtherError())).IsSuccess,
                "MapError" => (await failure.MapErrorAsync(async e =>
                {
                    await body(CancellationToken.None);
                    return e;
                })).IsFailure,
                "Compensate" => (await failure.CompensateAsync(async _ =>
                {
                    await body(CancellationToken.None);
                    return Result.Success();
                })).IsSuccess,
                "Finally" => (await success.FinallyAsync(async _ => await body(CancellationToken.None))).IsSuccess,
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
            };
        }

        return operation switch
        {
            "Map" => (await success.MapAsync(async c =>
            {
                await body(c);
                return 1;
            }, ct)).IsSuccess,
            "Bind" => (await success.BindAsync(async c =>
            {
                await body(c);
                return Result.Success();
            }, ct)).IsSuccess,
            "BindToValue" => (await success.BindAsync(async c =>
            {
                await body(c);
                return Result.Success(1);
            }, ct)).IsSuccess,
            "Match" => await success.MatchAsync(
                async c =>
                {
                    await body(c);
                    return true;
                },
                (_, _) => ValueTask.FromResult(false),
                ct),
            "Tap" => (await success.TapAsync(body, ct)).IsSuccess,
            "TapError" => (await failure.TapErrorAsync((_, c) => body(c), ct)).IsFailure,
            "Ensure" => (await success.EnsureAsync(
                async c =>
                {
                    await body(c);
                    return true;
                },
                new OtherError(),
                ct)).IsSuccess,
            "MapError" => (await failure.MapErrorAsync(async (e, c) =>
            {
                await body(c);
                return e;
            }, ct)).IsFailure,
            "Compensate" => (await failure.CompensateAsync(async (_, c) =>
            {
                await body(c);
                return Result.Success();
            }, ct)).IsSuccess,
            "Finally" => (await success.FinallyAsync((_, c) => body(c), ct)).IsSuccess,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };
    }

    // The token form called without its ct argument: the default parameter applies.
    private static async Task<bool> InvokeTokenFormWithoutToken(string operation, Func<CancellationToken, ValueTask> body)
    {
        var success = Result.Success();
        var failure = Result.Failure(new TestError());

        return operation switch
        {
            "Map" => (await success.MapAsync(async c =>
            {
                await body(c);
                return 1;
            })).IsSuccess,
            "Bind" => (await success.BindAsync(async c =>
            {
                await body(c);
                return Result.Success();
            })).IsSuccess,
            "BindToValue" => (await success.BindAsync(async c =>
            {
                await body(c);
                return Result.Success(1);
            })).IsSuccess,
            "Match" => await success.MatchAsync(
                async c =>
                {
                    await body(c);
                    return true;
                },
                (_, _) => ValueTask.FromResult(false)),
            "Tap" => (await success.TapAsync(body)).IsSuccess,
            "TapError" => (await failure.TapErrorAsync((_, c) => body(c))).IsFailure,
            "Ensure" => (await success.EnsureAsync(
                async c =>
                {
                    await body(c);
                    return true;
                },
                new OtherError())).IsSuccess,
            "MapError" => (await failure.MapErrorAsync(async (e, c) =>
            {
                await body(c);
                return e;
            })).IsFailure,
            "Compensate" => (await failure.CompensateAsync(async (_, c) =>
            {
                await body(c);
                return Result.Success();
            })).IsSuccess,
            "Finally" => (await success.FinallyAsync((_, c) => body(c))).IsSuccess,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };
    }

    private static Task CallTaskFormWithNull(string argument)
    {
        var result = Result.Success();
        return argument switch
        {
            "Map.mapper" => result.MapAsync((Func<Task<int>>)null!).AsTask(),
            "Bind.binder" => result.BindAsync((Func<Task<Result>>)null!).AsTask(),
            "BindToValue.binder" => result.BindAsync((Func<Task<Result<int>>>)null!).AsTask(),
            "Match.onSuccess" => result.MatchAsync(null!, _ => Task.FromResult(0)).AsTask(),
            "Match.onFailure" => result.MatchAsync(() => Task.FromResult(0), null!).AsTask(),
            "Tap.action" => result.TapAsync((Func<Task>)null!).AsTask(),
            "TapError.action" => result.TapErrorAsync((Func<IError, Task>)null!).AsTask(),
            "Ensure.predicate" => result.EnsureAsync((Func<Task<bool>>)null!, new TestError()).AsTask(),
            "Ensure.error" => result.EnsureAsync(() => Task.FromResult(true), null!).AsTask(),
            "MapError.mapper" => result.MapErrorAsync((Func<IError, Task<IError>>)null!).AsTask(),
            "Compensate.fallback" => result.CompensateAsync((Func<IError, Task<Result>>)null!).AsTask(),
            "Finally.action" => result.FinallyAsync((Func<Result, Task>)null!).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(argument), argument, "Unknown argument."),
        };
    }

    private static Task CallTokenFormWithNull(string argument)
    {
        var result = Result.Success();
        var ct = CancellationToken.None;
        return argument switch
        {
            "Map.mapper" => result.MapAsync((Func<CancellationToken, ValueTask<int>>)null!, ct).AsTask(),
            "Bind.binder" => result.BindAsync((Func<CancellationToken, ValueTask<Result>>)null!, ct).AsTask(),
            "BindToValue.binder" => result.BindAsync((Func<CancellationToken, ValueTask<Result<int>>>)null!, ct).AsTask(),
            "Match.onSuccess" => result.MatchAsync(null!, (_, _) => ValueTask.FromResult(0), ct).AsTask(),
            "Match.onFailure" => result.MatchAsync(_ => ValueTask.FromResult(0), null!, ct).AsTask(),
            "Tap.action" => result.TapAsync((Func<CancellationToken, ValueTask>)null!, ct).AsTask(),
            "TapError.action" => result.TapErrorAsync((Func<IError, CancellationToken, ValueTask>)null!, ct).AsTask(),
            "Ensure.predicate" => result.EnsureAsync((Func<CancellationToken, ValueTask<bool>>)null!, new TestError(), ct).AsTask(),
            "Ensure.error" => result.EnsureAsync(_ => ValueTask.FromResult(true), null!, ct).AsTask(),
            "MapError.mapper" => result.MapErrorAsync((Func<IError, CancellationToken, ValueTask<IError>>)null!, ct).AsTask(),
            "Compensate.fallback" => result.CompensateAsync((Func<IError, CancellationToken, ValueTask<Result>>)null!, ct).AsTask(),
            "Finally.action" => result.FinallyAsync((Func<Result, CancellationToken, ValueTask>)null!, ct).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(argument), argument, "Unknown argument."),
        };
    }

    private static async Task<T> Later<T>(bool yield, T value)
    {
        if (yield)
            await Task.Yield();
        return value;
    }

    private static async Task Later(bool yield)
    {
        if (yield)
            await Task.Yield();
    }

    private static async ValueTask<T> LaterValue<T>(bool yield, T value)
    {
        if (yield)
            await Task.Yield();
        return value;
    }

    #endregion
}

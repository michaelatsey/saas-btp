using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Core;

public sealed class ResultTests
{
    private const string SecretMessage = "secret connection string";

    // ── Success ──────────────────────────────────────────────────────────

    [Fact]
    public void Success_WhenCreated_IsSuccessAndNotFailure()
    {
        // Arrange + Act
        Result result = Result.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Error_WhenSuccess_ThrowsResultException()
    {
        // Arrange
        Result result = Result.Success();

        // Act
        var exception = Assert.Throws<ResultException>(() => result.Error);

        // Assert
        Assert.Equal("Cannot access Error on a successful result.", exception.Message);
        Assert.Empty(exception.Errors);
    }

    // ── Failure ──────────────────────────────────────────────────────────

    [Fact]
    public void Failure_WhenCreated_IsFailureAndNotSuccess()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = Result.Failure(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Error_WhenFailure_ReturnsSameError()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = Result.Failure(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Failure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));

        // Assert
        Assert.Equal("error", exception.ParamName);
    }

    // ── default / new Result() ───────────────────────────────────────────

    [Fact]
    public void Default_WhenUninitialized_IsNeitherSuccessNorFailure()
    {
        // Arrange + Act
        Result result = default;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Constructor_WhenParameterless_IsNeitherSuccessNorFailure()
    {
        // Arrange + Act
        var result = new Result();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Error_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result result = default;

        // Act
        var exception = Assert.Throws<ResultException>(() => result.Error);

        // Assert
        Assert.Contains("not initialized", exception.Message);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void Error_WhenParameterlessConstructor_ThrowsResultException()
    {
        // Arrange
        var result = new Result();

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Error);
    }

    // ── Try (no mapper) ──────────────────────────────────────────────────

    [Fact]
    public void Try_WhenActionReturnsSuccess_ReturnsSuccess()
    {
        // Act
        Result result = Result.Try(Result.Success);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Try_WhenActionReturnsFailure_ReturnsSameFailure()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = Result.Try(() => Result.Failure(error));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Try_WhenActionReturnsDefault_ThrowsResultException()
    {
        // Act + Assert
        Assert.Throws<ResultException>(() => Result.Try(() => default(Result)));
    }

    [Fact]
    public void Try_WhenActionThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result result = Result.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
    }

    [Fact]
    public void Try_WhenActionThrows_ErrorMetadataDoesNotExposeExceptionType()
    {
        // Act
        Result result = Result.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert: internal details go to the log, not to Metadata (security guarantee)
        Assert.True(result.IsFailure);
        Assert.False(result.Error.Metadata.ContainsKey("exceptionType"));
    }

    [Fact]
    public void Try_WhenActionThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result result = Result.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Exception, result.Error.Code);
        Assert.DoesNotContain(SecretMessage, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Try_WithoutMapper_WhenOperationCanceled_RethrowsOperationCanceledException()
    {
        // Act + Assert
        Assert.Throws<OperationCanceledException>(() => Result.Try(() => throw new OperationCanceledException()));
    }

    [Fact]
    public void Try_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => Result.Try(null!));

        // Assert
        Assert.Equal("action", exception.ParamName);
    }

    // ── Try (with mapper) ────────────────────────────────────────────────

    [Fact]
    public void Try_WithMapper_WhenActionReturnsSuccess_DoesNotCallMapper()
    {
        // Arrange
        var called = false;

        // Act
        Result result = Result.Try(Result.Success, _ =>
        {
            called = true;
            return new TestError();
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(called);
    }

    [Fact]
    public void Try_WithMapper_WhenActionReturnsFailure_ReturnsSameFailure()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = Result.Try(() => Result.Failure(error), _ => new OtherError());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Try_WithMapper_WhenActionReturnsDefault_ThrowsResultExceptionWithoutCallingMapper()
    {
        // Arrange
        var mapped = new List<Exception>();

        // Act + Assert
        Assert.Throws<ResultException>(() => Result.Try(() => default(Result), ex =>
        {
            mapped.Add(ex);
            return new TestError();
        }));
        Assert.Empty(mapped);
    }

    [Fact]
    public void Try_WithMapper_WhenActionThrows_ReturnsMappedError()
    {
        // Arrange
        var thrown = new InvalidOperationException("boom");
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result result = Result.Try(() => throw thrown, ex =>
        {
            received = ex;
            return mapped;
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(mapped, result.Error);
        Assert.Same(thrown, received);
    }

    [Fact]
    public void Try_WithMapper_WhenOperationCanceled_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result result = Result.Try(() => throw new OperationCanceledException(), _ => mapped);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(mapped, result.Error);
    }

    [Fact]
    public void Try_WithMapper_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => Result.Try(() => throw new InvalidOperationException("boom"), _ => null!));
    }

    [Fact]
    public void Try_WithMapper_WhenMapperThrows_PropagatesMapperException()
    {
        // Arrange
        var mapperException = new FormatException("mapper failed");

        // Act
        var exception = Assert.Throws<FormatException>(() => Result.Try(() => throw new InvalidOperationException("boom"), _ => throw mapperException));

        // Assert
        Assert.Same(mapperException, exception);
    }

    [Fact]
    public void Try_WithMapper_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => Result.Try(null!, _ => new TestError()));

        // Assert
        Assert.Equal("action", exception.ParamName);
    }

    [Fact]
    public void Try_WithMapper_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => Result.Try(Result.Success, null!));

        // Assert
        Assert.Equal("exceptionMapper", exception.ParamName);
    }

    // ── TryAsync (no mapper) ─────────────────────────────────────────────

    [Fact]
    public async Task TryAsync_WhenActionCompletesSynchronously_ReturnsSuccess()
    {
        // Act
        Result result = await Result.TryAsync(() => Task.FromResult(Result.Success()));

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TryAsync_WhenActionCompletesAsynchronously_ReturnsSuccess()
    {
        // Act
        Result result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            return Result.Success();
        });

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TryAsync_WhenActionReturnsFailure_ReturnsSameFailure()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            return Result.Failure(error);
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryAsync_WhenActionReturnsDefault_ThrowsResultException(bool yield)
    {
        // Act + Assert
        await Assert.ThrowsAsync<ResultException>(async () => await Result.TryAsync(async () =>
        {
            if (yield)
                await Task.Yield();
            return default(Result);
        }));
    }

    [Fact]
    public async Task TryAsync_WhenActionReturnsCompletedDefault_ThrowsResultException()
    {
        // Act + Assert
        await Assert.ThrowsAsync<ResultException>(async () =>
            await Result.TryAsync(() => Task.FromResult(default(Result))));
    }

    [Fact]
    public async Task TryAsync_WhenActionThrowsAfterAwait_ReturnsTechnicalFailure()
    {
        // Act
        Result result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException(SecretMessage);
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Fact]
    public async Task TryAsync_WhenActionThrowsBeforeReturningTask_ReturnsTechnicalFailure()
    {
        // Arrange
        Func<Task<Result>> action = () => throw new InvalidOperationException(SecretMessage);

        // Act
        Result result = await Result.TryAsync(action);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Fact]
    public async Task TryAsync_WhenActionThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException(SecretMessage);
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Exception, result.Error.Code);
        Assert.DoesNotContain(SecretMessage, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryAsync_WithoutMapper_WhenOperationCanceled_RethrowsOperationCanceledException()
    {
        // Act + Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Result.TryAsync(async () =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            }));
    }

    [Fact]
    public async Task TryAsync_WithoutMapper_WhenCancelled_RethrowsTaskCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act + Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await Result.TryAsync(async () =>
            {
                await Task.Delay(1, cts.Token);
                return Result.Success();
            }));
    }

    [Fact]
    public async Task TryAsync_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await Result.TryAsync(null!));

        // Assert
        Assert.Equal("action", exception.ParamName);
    }

    // ── TryAsync (with mapper) ───────────────────────────────────────────

    [Fact]
    public async Task TryAsync_WithMapper_WhenActionSucceeds_DoesNotCallMapper()
    {
        // Arrange
        var called = false;

        // Act
        Result result = await Result.TryAsync(
            async () =>
            {
                await Task.Yield();
                return Result.Success();
            },
            _ =>
            {
                called = true;
                return new TestError();
            });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryAsync_WithMapper_WhenActionReturnsDefault_ThrowsResultExceptionWithoutCallingMapper(bool yield)
    {
        // Arrange
        var mapped = new List<Exception>();

        // Act + Assert
        await Assert.ThrowsAsync<ResultException>(async () => await Result.TryAsync(
            async () =>
            {
                if (yield)
                    await Task.Yield();
                return default(Result);
            },
            ex =>
            {
                mapped.Add(ex);
                return new TestError();
            }));
        Assert.Empty(mapped);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenActionReturnsCompletedDefault_ThrowsResultExceptionWithoutCallingMapper()
    {
        // Arrange
        var mapped = new List<Exception>();

        // Act + Assert
        await Assert.ThrowsAsync<ResultException>(async () => await Result.TryAsync(
            () => Task.FromResult(default(Result)),
            ex =>
            {
                mapped.Add(ex);
                return new TestError();
            }));
        Assert.Empty(mapped);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenActionThrows_ReturnsMappedError()
    {
        // Arrange
        var thrown = new InvalidOperationException("boom");
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result result = await Result.TryAsync(
            async () =>
            {
                await Task.Yield();
                throw thrown;
            },
            ex =>
            {
                received = ex;
                return mapped;
            });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(mapped, result.Error);
        Assert.Same(thrown, received);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenCancelled_ReturnsMappedError()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result result = await Result.TryAsync(
            async () =>
            {
                await Task.Delay(1, cts.Token);
                return Result.Success();
            },
            ex =>
            {
                received = ex;
                return mapped;
            });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(mapped, result.Error);
        Assert.IsType<TaskCanceledException>(received);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await Result.TryAsync(
                async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("boom");
                },
                _ => null!));
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await Result.TryAsync(null!, _ => new TestError()));

        // Assert
        Assert.Equal("action", exception.ParamName);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await Result.TryAsync(() => Task.FromResult(Result.Success()), null!));

        // Assert
        Assert.Equal("exceptionMapper", exception.ParamName);
    }

    // ── Equality ─────────────────────────────────────────────────────────

    [Fact]
    public void Equals_WhenBothSuccess_ReturnsTrue()
    {
        // Arrange
        Result left = Result.Success();
        Result right = Result.Success();

        // Act
        var equal = left.Equals(right);

        // Assert
        Assert.True(equal);
        Assert.True((left == right));
        Assert.False((left != right));
        Assert.Equal(right.GetHashCode(), left.GetHashCode());
    }

    [Fact]
    public void Equals_WhenFailuresWithEqualErrors_ReturnsTrue()
    {
        // Arrange
        Result left = Result.Failure(new TestError("same"));
        Result right = Result.Failure(new TestError("same"));

        // Act
        var equal = left.Equals(right);

        // Assert
        Assert.True(equal);
        Assert.True((left == right));
        Assert.Equal(right.GetHashCode(), left.GetHashCode());
    }

    [Fact]
    public void Equals_WhenFailuresWithDifferentErrors_ReturnsFalse()
    {
        // Arrange
        Result left = Result.Failure(new TestError("a"));
        Result right = Result.Failure(new TestError("b"));

        // Act
        var equal = left.Equals(right);

        // Assert
        Assert.False(equal);
        Assert.True((left != right));
    }

    [Fact]
    public void Equals_WhenSuccessAndFailure_ReturnsFalse()
    {
        // Arrange
        Result success = Result.Success();
        Result failure = Result.Failure(new TestError());

        // Act
        var equal = success.Equals(failure);

        // Assert
        Assert.False(equal);
        Assert.False((success == failure));
        Assert.True((success != failure));
    }

    [Fact]
    public void Equals_WhenBothDefault_ReturnsTrue()
    {
        // Arrange
        Result left = default;
        var right = new Result();

        // Act
        var equal = left.Equals(right);

        // Assert
        Assert.True(equal);
        Assert.True((left == right));
        Assert.Equal(right.GetHashCode(), left.GetHashCode());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Equals_WhenDefaultAndInitialized_ReturnsFalse(bool isSuccess)
    {
        // Arrange
        Result uninitialized = default;
        Result initialized = isSuccess ? Result.Success() : Result.Failure(new TestError());

        // Act
        var equal = uninitialized.Equals(initialized);

        // Assert
        Assert.False(equal);
        Assert.True((uninitialized != initialized));
    }

    [Fact]
    public void EqualsObject_WhenBoxedEqualResult_ReturnsTrue()
    {
        // Arrange
        Result result = Result.Failure(new TestError("same"));
        object boxed = Result.Failure(new TestError("same"));

        // Act
        var equal = result.Equals(boxed);

        // Assert
        Assert.True(equal);
    }

    [Fact]
    public void EqualsObject_WhenNull_ReturnsFalse()
    {
        // Arrange
        Result result = Result.Success();

        // Act
        var equal = result.Equals((object?)null); // a bare null binds to Equals(Result) through the conversion from Error (CS0037)

        // Assert
        Assert.False(equal);
    }

    [Fact]
    public void EqualsObject_WhenOtherType_ReturnsFalse()
    {
        // Arrange
        Result result = Result.Success();
        object other = Result.Success(Unit.Value);

        // Act
        var equal = result.Equals(other);

        // Assert
        Assert.False(equal);
    }

    // ── ToString ─────────────────────────────────────────────────────────

    [Fact]
    public void ToString_WhenSuccess_DescribesSuccess()
    {
        // Arrange
        Result result = Result.Success();

        // Act
        var text = result.ToString();

        // Assert
        Assert.Equal("Result { Success }", text);
    }

    [Fact]
    public void ToString_WhenFailure_DescribesCodeAndMessage()
    {
        // Arrange
        Result result = Result.Failure(new TestError("x"));

        // Act
        var text = result.ToString();

        // Assert
        Assert.Equal("Result { Failure: [TEST.ERROR] Test error: x }", text);
    }

    [Fact]
    public void ToString_WhenDefault_DescribesUninitialized()
    {
        // Arrange
        Result result = default;

        // Act
        var text = result.ToString();

        // Assert
        Assert.Equal("Result { Uninitialized }", text);
    }
}

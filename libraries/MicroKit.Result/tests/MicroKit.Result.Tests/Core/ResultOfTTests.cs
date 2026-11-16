using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Core;

public sealed class ResultOfTTests
{
    private const string SecretMessage = "secret connection string";

    // ── Success ──────────────────────────────────────────────────────────

    [Fact]
    public void Success_WhenCreated_IsSuccessWithValue()
    {
        // Act
        Result<int> result = Result<int>.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ResultSuccessOfT_WhenCreated_IsSuccessWithValue()
    {
        // Act
        Result<string> result = Result.Success("hello");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Success_WhenValueIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert — MicroKit ADR-RESULT-001 D6 (michaelatsey/microkit@a12097b): absence is a failure, not a null value
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!)).ParamName);
        Assert.Throws<ArgumentNullException>(() => Result<int?>.Success(null));
        Assert.Throws<ArgumentNullException>(() => { Result<string> converted = (string)null!; return converted; });
    }

    [Fact]
    public void Success_WhenValueIsDefaultOfValueType_IsSuccess()
    {
        // Act
        Result<int> result = Result.Success(0);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void Error_WhenSuccess_ThrowsResultException()
    {
        // Arrange
        Result<int> result = Result.Success(42);

        // Act
        var exception = Assert.Throws<ResultException>(() => result.Error);

        // Assert
        Assert.Equal("Cannot access Error on a successful result.", exception.Message);
        Assert.Empty(exception.Errors);
    }

    // ── Failure ──────────────────────────────────────────────────────────

    [Fact]
    public void Failure_WhenCreated_IsFailureWithError()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result<int> result = Result<int>.Failure(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ResultFailureOfT_WhenCreated_IsFailureWithError()
    {
        // Arrange
        var error = new NotFoundTestError(7);

        // Act
        Result<string> result = Result.Failure<string>(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Failure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));

        // Assert
        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void ResultFailureOfT_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => Result.Failure<int>(null!));
    }

    [Fact]
    public void Value_WhenFailure_ThrowsResultExceptionCarryingError()
    {
        // Arrange
        var error = new TestError("x");
        Result<int> result = Result.Failure<int>(error);

        // Act
        var exception = Assert.Throws<ResultException>(() => result.Value);

        // Assert
        Assert.Equal("Cannot access Value on a failed result. Error: [TEST.ERROR] Test error: x", exception.Message);
        Assert.Same(error, Assert.Single(exception.Errors));
    }

    // ── default / new Result<T>() ────────────────────────────────────────

    [Fact]
    public void Default_WhenUninitialized_IsNeitherSuccessNorFailure()
    {
        // Arrange + Act
        Result<int> result = default;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Constructor_WhenParameterless_IsNeitherSuccessNorFailure()
    {
        // Arrange + Act
        var result = new Result<string>();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Value_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var exception = Assert.Throws<ResultException>(() => result.Value);

        // Assert
        Assert.Contains("not initialized", exception.Message);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public void Error_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var exception = Assert.Throws<ResultException>(() => result.Error);

        // Assert
        Assert.Contains("not initialized", exception.Message);
    }

    [Fact]
    public void Value_WhenParameterlessConstructor_ThrowsResultException()
    {
        // Arrange
        var result = new Result<string>();

        // Act + Assert
        Assert.Throws<ResultException>(() => result.Value);
        Assert.Throws<ResultException>(() => result.Error);
    }

    // ── Implicit conversion ──────────────────────────────────────────────

    [Fact]
    public void ImplicitConversion_FromValue_ReturnsSuccess()
    {
        // Act
        Result<int> result = 42;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromReference_ReturnsSuccessWithSameInstance()
    {
        // Arrange
        var payload = new List<int> { 1, 2 };

        // Act
        Result<List<int>> result = payload;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(payload, result.Value);
    }

    [Fact]
    public void ImplicitConversion_WhenValueIsGenericResultAndTIsObject_ThrowsArgumentException()
    {
        // Arrange
        Result<int> failure = Result.Failure<int>(new TestError());

        // Act + Assert
        Assert.Throws<ArgumentException>(() => ToObjectResult(failure));
    }

    [Fact]
    public void ImplicitConversion_WhenValueIsResultAndTIsObject_ThrowsArgumentException()
    {
        // Arrange
        Result failure = Result.Failure(new TestError());

        // Act + Assert
        Assert.Throws<ArgumentException>(() => ToObjectResult(failure));
    }

    // ── Try (no mapper) ──────────────────────────────────────────────────

    [Fact]
    public void Try_WhenFuncReturns_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = Result<int>.Try(() => 42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Try_WhenFuncThrows_ErrorMetadataDoesNotExposeExceptionType()
    {
        // Act
        Result<int> result = Result<int>.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert: internal details go to the log, not to Metadata (security guarantee)
        Assert.True(result.IsFailure);
        Assert.False(result.Error.Metadata.ContainsKey("exceptionType"));
    }

    [Fact]
    public void Try_WhenFuncThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result<int> result = Result<int>.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
    }

    [Fact]
    public void Try_WhenFuncThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result<int> result = Result<int>.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Exception, result.Error.Code);
        Assert.DoesNotContain(SecretMessage, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Try_WithoutMapper_WhenOperationCanceled_RethrowsOperationCanceledException()
    {
        // Act + Assert
        Assert.Throws<OperationCanceledException>(() => Result<int>.Try(() => throw new OperationCanceledException()));
    }

    [Fact]
    public void Try_WhenFuncIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => Result<int>.Try(null!));

        // Assert
        Assert.Equal("func", exception.ParamName);
    }

    // ── Try (with mapper) ────────────────────────────────────────────────

    [Fact]
    public void Try_WithMapper_WhenFuncReturns_ReturnsSuccessWithoutCallingMapper()
    {
        // Arrange
        var called = false;

        // Act
        Result<int> result = Result<int>.Try(() => 42, _ =>
        {
            called = true;
            return new TestError();
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.False(called);
    }

    [Fact]
    public void Try_WithMapper_WhenFuncThrows_ReturnsMappedError()
    {
        // Arrange
        var thrown = new InvalidOperationException("boom");
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result<int> result = Result<int>.Try(() => throw thrown, ex =>
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
        Result<int> result = Result<int>.Try(() => throw new OperationCanceledException(), _ => mapped);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(mapped, result.Error);
    }

    [Fact]
    public void Try_WithMapper_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => Result<int>.Try(() => throw new InvalidOperationException("boom"), _ => null!));
    }

    [Fact]
    public void Try_WithMapper_WhenMapperThrows_PropagatesMapperException()
    {
        // Arrange
        var mapperException = new FormatException("mapper failed");

        // Act
        var exception = Assert.Throws<FormatException>(() => Result<int>.Try(() => throw new InvalidOperationException("boom"), _ => throw mapperException));

        // Assert
        Assert.Same(mapperException, exception);
    }

    [Fact]
    public void Try_WithMapper_WhenFuncIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => Result<int>.Try(null!, _ => new TestError()));

        // Assert
        Assert.Equal("func", exception.ParamName);
    }

    [Fact]
    public void Try_WithMapper_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => Result<int>.Try(() => 42, null!));

        // Assert
        Assert.Equal("exceptionMapper", exception.ParamName);
    }

    // ── TryAsync (no mapper) ─────────────────────────────────────────────

    [Fact]
    public async Task TryAsync_WhenFuncCompletesSynchronously_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = await Result<int>.TryAsync(() => Task.FromResult(42));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task TryAsync_WhenFuncCompletesAsynchronously_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = await Result<int>.TryAsync(async () =>
        {
            await Task.Yield();
            return 42;
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task TryAsync_WhenFuncThrowsAfterAwait_ReturnsTechnicalFailure()
    {
        // Act
        Result<int> result = await Result<int>.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException(SecretMessage);
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Fact]
    public async Task TryAsync_WhenFuncThrowsBeforeReturningTask_ReturnsTechnicalFailure()
    {
        // Arrange
        Func<Task<int>> func = () => throw new InvalidOperationException(SecretMessage);

        // Act
        Result<int> result = await Result<int>.TryAsync(func);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Fact]
    public async Task TryAsync_WhenFuncThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result<int> result = await Result<int>.TryAsync(async () =>
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
            await Result<int>.TryAsync(async () =>
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
            await Result<int>.TryAsync(async () =>
            {
                await Task.Delay(1, cts.Token);
                return 42;
            }));
    }

    [Fact]
    public async Task TryAsync_WhenFuncIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await Result<int>.TryAsync(null!));

        // Assert
        Assert.Equal("func", exception.ParamName);
    }

    // ── TryAsync (with mapper) ───────────────────────────────────────────

    [Fact]
    public async Task TryAsync_WithMapper_WhenFuncReturns_ReturnsSuccessWithoutCallingMapper()
    {
        // Arrange
        var called = false;

        // Act
        Result<int> result = await Result<int>.TryAsync(
            async () =>
            {
                await Task.Yield();
                return 42;
            },
            _ =>
            {
                called = true;
                return new TestError();
            });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.False(called);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenFuncThrows_ReturnsMappedError()
    {
        // Arrange
        var thrown = new InvalidOperationException("boom");
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result<int> result = await Result<int>.TryAsync(
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
        Result<int> result = await Result<int>.TryAsync(
            async () =>
            {
                await Task.Delay(1, cts.Token);
                return 42;
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
            await Result<int>.TryAsync(
                async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("boom");
                },
                _ => null!));
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenFuncIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await Result<int>.TryAsync(null!, _ => new TestError()));

        // Assert
        Assert.Equal("func", exception.ParamName);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await Result<int>.TryAsync(() => Task.FromResult(42), null!));

        // Assert
        Assert.Equal("exceptionMapper", exception.ParamName);
    }

    // ── Equality ─────────────────────────────────────────────────────────

    [Fact]
    public void Equals_WhenSuccessesWithEqualValues_ReturnsTrue()
    {
        // Arrange
        Result<string> left = Result.Success("value");
        Result<string> right = Result.Success(new string("value".AsSpan()));

        // Act
        var equal = left.Equals(right);

        // Assert
        Assert.True(equal);
        Assert.True((left == right));
        Assert.False((left != right));
        Assert.Equal(right.GetHashCode(), left.GetHashCode());
    }

    [Fact]
    public void Equals_WhenSuccessesWithDifferentValues_ReturnsFalse()
    {
        // Arrange
        Result<int> left = Result.Success(1);
        Result<int> right = Result.Success(2);

        // Act
        var equal = left.Equals(right);

        // Assert
        Assert.False(equal);
        Assert.False((left == right));
        Assert.True((left != right));
    }

    [Fact]
    public void Equals_WhenFailuresWithEqualErrors_ReturnsTrue()
    {
        // Arrange
        Result<int> left = Result.Failure<int>(new TestError("same"));
        Result<int> right = Result.Failure<int>(new TestError("same"));

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
        Result<int> left = Result.Failure<int>(new TestError("a"));
        Result<int> right = Result.Failure<int>(new OtherError());

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
        Result<int> success = Result.Success(0);
        Result<int> failure = Result.Failure<int>(new TestError());

        // Act
        var equal = success.Equals(failure);

        // Assert
        Assert.False(equal);
        Assert.True((success != failure));
    }

    [Fact]
    public void Equals_WhenBothDefault_ReturnsTrue()
    {
        // Arrange
        Result<int> left = default;
        var right = new Result<int>();

        // Act
        var equal = left.Equals(right);

        // Assert
        Assert.True(equal);
        Assert.True((left == right));
        Assert.Equal(right.GetHashCode(), left.GetHashCode());
    }

    [Fact]
    public void Equals_WhenDefaultAndSuccessOfDefaultValue_ReturnsFalse()
    {
        // Arrange
        Result<int> uninitialized = default;
        Result<int> success = Result.Success(0);

        // Act
        var equal = uninitialized.Equals(success);

        // Assert
        Assert.False(equal);
        Assert.True((uninitialized != success));
    }

    [Fact]
    public void Equals_WhenDefaultAndFailure_ReturnsFalse()
    {
        // Arrange
        Result<int> uninitialized = default;
        Result<int> failure = Result.Failure<int>(new TestError());

        // Act
        var equal = uninitialized.Equals(failure);

        // Assert
        Assert.False(equal);
    }

    [Fact]
    public void EqualsObject_WhenBoxedEqualResult_ReturnsTrue()
    {
        // Arrange
        Result<int> result = Result.Success(5);
        object boxed = Result.Success(5);

        // Act
        var equal = result.Equals(boxed);

        // Assert
        Assert.True(equal);
    }

    [Fact]
    public void EqualsObject_WhenNull_ReturnsFalse()
    {
        // Arrange
        Result<int> result = Result.Success(5);

        // Act
        var equal = result.Equals((object?)null); // a bare null binds to Equals(Result) through the conversion from Error (CS0037)

        // Assert
        Assert.False(equal);
    }

    [Fact]
    public void EqualsObject_WhenResultOfOtherType_ReturnsFalse()
    {
        // Arrange
        Result<int> result = Result.Success(5);
        object other = Result.Success(5L);

        // Act
        var equal = result.Equals(other);

        // Assert
        Assert.False(equal);
    }

    // ── ToString ─────────────────────────────────────────────────────────

    [Fact]
    public void ToString_WhenSuccess_DescribesValue()
    {
        // Arrange
        Result<int> result = Result.Success(42);

        // Act
        var text = result.ToString();

        // Assert
        Assert.Equal("Result { Success: 42 }", text);
    }

    [Fact]
    public void ToString_WhenFailure_DescribesCodeAndMessage()
    {
        // Arrange
        Result<int> result = Result.Failure<int>(new TestError("x"));

        // Act
        var text = result.ToString();

        // Assert
        Assert.Equal("Result { Failure: [TEST.ERROR] Test error: x }", text);
    }

    [Fact]
    public void ToString_WhenDefault_DescribesUninitialized()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var text = result.ToString();

        // Assert
        Assert.Equal("Result { Uninitialized }", text);
    }

    // The implicit T -> Result<T> conversion with T = object accepts a result as the value: the
    // Phase 2 defect (`Result<object> r = Result.Failure<int>(error);` is a success today).
    private static Result<object> ToObjectResult(Result<int> value) => value;

    private static Result<object> ToObjectResult(Result value) => value;

    [Fact]
    public void Success_WhenValueIsAResult_ThrowsArgumentException()
    {
        // Arrange
        object failure = Result.Failure<int>(new TestError());

        // Act + Assert
        Assert.Equal("value", Assert.Throws<ArgumentException>(() => Result<object>.Success(failure)).ParamName);
        Assert.Throws<ArgumentException>(() => Result.Success<object>(Result.Success()));
    }

    [Fact]
    public void ImplicitConversion_WhenValueIsAnOrdinaryObject_ReturnsSuccess()
    {
        // Act
        Result<object> result = (object)42;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }
}

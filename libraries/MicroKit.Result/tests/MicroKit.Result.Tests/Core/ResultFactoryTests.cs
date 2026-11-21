using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Core;

public sealed class ResultFactoryTests
{
    [Fact]
    public void Success_WhenCalled_ReturnsSuccess()
    {
        // Act
        Result result = ResultFactory.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void SuccessOfT_WhenCalled_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = ResultFactory.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_WhenCalled_ReturnsFailureWithError()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = ResultFactory.Failure(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void FailureOfT_WhenCalled_ReturnsFailureWithError()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result<int> result = ResultFactory.Failure<int>(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Failure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => ResultFactory.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => ResultFactory.Failure<int>(null!));
    }

    [Fact]
    public void Try_WhenActionThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result result = ResultFactory.Try(() => throw new InvalidOperationException("boom"));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Fact]
    public void Try_WhenActionSucceeds_ReturnsSuccess()
    {
        // Act
        Result result = ResultFactory.Try(Result.Success);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Try_WithMapper_WhenActionThrows_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result result = ResultFactory.Try(() => throw new InvalidOperationException("boom"), _ => mapped);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(mapped, result.Error);
    }

    [Fact]
    public void TryOfT_WhenFuncReturns_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = ResultFactory.Try(() => 42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void TryOfT_WhenFuncThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result<int> result = ResultFactory.Try<int>(() => throw new InvalidOperationException("boom"));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Fact]
    public void TryOfT_WithMapper_WhenFuncThrows_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result<int> result = ResultFactory.Try<int>(() => throw new InvalidOperationException("boom"), _ => mapped);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(mapped, result.Error);
    }

    [Fact]
    public void Try_WhenDelegateIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => ResultFactory.Try((Func<Result>)null!));
        Assert.Throws<ArgumentNullException>(() => ResultFactory.Try<int>(null!));
        Assert.Throws<ArgumentNullException>(() => ResultFactory.Try(Result.Success, null!));
        Assert.Throws<ArgumentNullException>(() => ResultFactory.Try(() => 42, null!));
    }

    [Fact]
    public async Task TryAsync_WhenActionSucceeds_ReturnsSuccess()
    {
        // Act
        Result result = await ResultFactory.TryAsync(async () =>
        {
            await Task.Yield();
            return Result.Success();
        });

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TryAsync_WhenActionThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result result = await ResultFactory.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenActionThrows_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result result = await ResultFactory.TryAsync(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            },
            _ => mapped);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(mapped, result.Error);
    }

    [Fact]
    public async Task TryAsyncOfT_WhenFuncReturns_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = await ResultFactory.TryAsync(async () =>
        {
            await Task.Yield();
            return 42;
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task TryAsyncOfT_WhenFuncThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result<int> result = await ResultFactory.TryAsync<int>(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Technical, result.Error.Category);
    }

    [Fact]
    public async Task TryAsyncOfT_WithMapper_WhenFuncThrows_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result<int> result = await ResultFactory.TryAsync<int>(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            },
            _ => mapped);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(mapped, result.Error);
    }

    [Fact]
    public async Task TryAsync_WhenDelegateIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await ResultFactory.TryAsync((Func<Task<Result>>)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await ResultFactory.TryAsync<int>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await ResultFactory.TryAsync(() => Task.FromResult(Result.Success()), null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await ResultFactory.TryAsync(() => Task.FromResult(42), null!));
    }

    private const string SecretMessage = "secret connection string";

    [Fact]
    public void Try_WithoutMapper_WhenOperationCanceled_RethrowsOperationCanceledException()
    {
        // Act + Assert
        Assert.Throws<OperationCanceledException>(() => ResultFactory.Try(() => throw new OperationCanceledException()));
        Assert.Throws<OperationCanceledException>(() => ResultFactory.Try<int>(() => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task TryAsync_WithoutMapper_WhenOperationCanceled_RethrowsOperationCanceledException()
    {
        // Act + Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await ResultFactory.TryAsync(async () =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            }));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await ResultFactory.TryAsync<int>(async () =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            }));
    }

    [Fact]
    public void Try_WithoutMapper_WhenDelegateThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result result = ResultFactory.Try(() => throw new InvalidOperationException(SecretMessage));
        Result<int> typed = ResultFactory.Try<int>(() => throw new InvalidOperationException(SecretMessage));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Exception, result.Error.Code);
        Assert.DoesNotContain(SecretMessage, result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(typed.IsFailure);
        Assert.Equal(ErrorCode.Exception, typed.Error.Code);
        Assert.DoesNotContain(SecretMessage, typed.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryAsync_WithoutMapper_WhenDelegateThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result result = await ResultFactory.TryAsync(() => Task.FromException<Result>(new InvalidOperationException(SecretMessage)));
        Result<int> typed = await ResultFactory.TryAsync(() => Task.FromException<int>(new InvalidOperationException(SecretMessage)));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Exception, result.Error.Code);
        Assert.DoesNotContain(SecretMessage, result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(typed.IsFailure);
        Assert.Equal(ErrorCode.Exception, typed.Error.Code);
        Assert.DoesNotContain(SecretMessage, typed.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

namespace MicroKit.Result.Tests.Extensions;

// #184 Phase 2: a Task-returning delegate given to Tap, TapError or Finally is awaited, and every async
// extension validates its arguments, the source task included, when it is called.
public sealed class AwaitedSideEffectTests
{
    private static readonly TestSupport.TestError Failure = new();

    [Fact]
    public async Task Tap_WithTaskReturningLambdaThatIsNotAsync_AwaitsIt()
    {
        // Arrange
        var gate = new TaskCompletionSource();
        var completed = false;
        Task SideEffect(int _) => gate.Task.ContinueWith(_ => completed = true, TaskScheduler.Default);

        // Act
        ValueTask<Result<int>> pending = Result.Success(1).Tap(v => SideEffect(v));
        Assert.False(completed);
        gate.SetResult();
        Result<int> tapped = await pending;

        // Assert
        Assert.True(completed);
        Assert.Equal(Result.Success(1), tapped);
    }

    [Fact]
    public async Task TapError_NonGenericWithAsyncLambda_RunsOnlyOnFailure()
    {
        // Arrange
        var calls = 0;

        // Act
        Result success = await Result.Success().TapError(async _ => { await Task.Yield(); calls++; });
        Result failure = await Result.Failure(Failure).TapError(async _ => { await Task.Yield(); calls++; });

        // Assert
        Assert.True(success.IsSuccess);
        Assert.True(failure.IsFailure);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Tap_WithSyncLambda_StillBindsToTheSynchronousOverload()
    {
        // Arrange
        var seen = 0;

        // Act: the return type proves the overload; a Result<int>, not a pending one
        Result<int> tapped = Result.Success(5).Tap(v => seen = v);

        // Assert
        Assert.True(tapped.IsSuccess);
        Assert.Equal(5, seen);
    }

    [Fact]
    public void Map_OnNullSourceTask_ThrowsArgumentNullExceptionAtTheCall()
    {
        // Arrange
        Task<Result<int>> source = null!;

        // Act
        var thrown = Record.Exception(() => { ValueTask<Result<int>> pending = source.Map(v => v); _ = pending.AsTask(); });

        // Assert
        Assert.Equal("resultTask", Assert.IsType<ArgumentNullException>(thrown).ParamName);
    }

    [Fact]
    public void Ensure_OnTaskWithNullError_ThrowsArgumentNullExceptionAtTheCall()
    {
        // Arrange
        Task<Result<int>> source = Task.FromResult(Result.Success(1));

        // Act
        var thrown = Record.Exception(() => { ValueTask<Result<int>> pending = source.Ensure(_ => true, null!); _ = pending.AsTask(); });

        // Assert
        Assert.Equal("error", Assert.IsType<ArgumentNullException>(thrown).ParamName);
    }
}

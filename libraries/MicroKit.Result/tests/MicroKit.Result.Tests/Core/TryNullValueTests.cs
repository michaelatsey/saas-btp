using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Core;

// MicroKit ADR-RESULT-001 D6 (michaelatsey/microkit@a12097b): a null success value throws ArgumentNullException. A Try/TryAsync delegate that returns null
// violates its contract: the exception reaches the caller, outside the catch, and the mapper never sees it.
public sealed class TryNullValueTests
{
    // ── Success ───────────────────────────────────────────────────────────

    [Fact]
    public void Success_WhenNullableValueTypeIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => Result.Success<int?>(null));
        Assert.Equal("value", thrown.ParamName);
    }

    [Fact]
    public void Success_WhenReferenceIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => Result<string>.Success(null!));
        Assert.Equal("value", thrown.ParamName);
    }

    [Fact]
    public void Success_WhenNullableValueTypeHasValue_ReturnsSuccess()
    {
        // Act
        var result = Result.Success<int?>(5);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public void ImplicitFromValue_WhenNull_ThrowsArgumentNullException()
    {
        // Arrange
        string? value = null;

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() =>
        {
            Result<string> result = value!;
            return result;
        });
        Assert.Equal("value", thrown.ParamName);
    }

    // ── Try ───────────────────────────────────────────────────────────────

    [Fact]
    public void Try_WhenFuncReturnsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => Result<string>.Try(() => null!));
        Assert.Equal("value", thrown.ParamName);
    }

    [Fact]
    public void Try_WhenFuncReturnsNullNullableValueType_ThrowsArgumentNullException()
    {
        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => Result<int?>.Try(() => null));
        Assert.Equal("value", thrown.ParamName);
    }

    [Fact]
    public void Try_WithMapper_WhenFuncReturnsNull_ThrowsArgumentNullExceptionWithoutCallingMapper()
    {
        // Arrange
        var mapperCalls = new List<Exception>();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentNullException>(() => Result<string>.Try(() => null!, ex =>
        {
            mapperCalls.Add(ex);
            return new TestError();
        }));
        Assert.Equal("value", thrown.ParamName);
        Assert.Empty(mapperCalls);
    }

    [Fact]
    public void Try_WhenFuncReturnsResult_ThrowsArgumentException()
    {
        // Arrange
        var error = new TestError();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentException>(() => Result<object>.Try(() => Result.Failure<int>(error)));
        Assert.IsType<ArgumentException>(thrown);
        Assert.Equal("value", thrown.ParamName);
    }

    [Fact]
    public void Try_WithMapper_WhenFuncReturnsResult_ThrowsArgumentExceptionWithoutCallingMapper()
    {
        // Arrange
        var mapperCalls = new List<Exception>();

        // Act + Assert
        var thrown = Assert.Throws<ArgumentException>(() => Result<object>.Try(() => Result.Success(), ex =>
        {
            mapperCalls.Add(ex);
            return new TestError();
        }));
        Assert.IsType<ArgumentException>(thrown);
        Assert.Empty(mapperCalls);
    }

    // ── TryAsync ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryAsync_WhenFuncReturnsNull_ThrowsArgumentNullException(bool yield)
    {
        // Act + Assert
        var thrown = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await Result<string>.TryAsync(() => NullAfter(yield)));
        Assert.Equal("value", thrown.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryAsync_WithMapper_WhenFuncReturnsNull_ThrowsArgumentNullExceptionWithoutCallingMapper(bool yield)
    {
        // Arrange
        var mapperCalls = new List<Exception>();

        // Act + Assert
        var thrown = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await Result<string>.TryAsync(() => NullAfter(yield), ex =>
            {
                mapperCalls.Add(ex);
                return new TestError();
            }));
        Assert.Equal("value", thrown.ParamName);
        Assert.Empty(mapperCalls);
    }

    [Fact]
    public async Task TryAsync_WhenFuncReturnsResult_ThrowsArgumentException()
    {
        // Arrange
        var error = new TestError();

        // Act + Assert
        var thrown = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Result<object>.TryAsync(async () =>
            {
                await Task.Yield();
                return Result.Failure<int>(error);
            }));
        Assert.IsType<ArgumentException>(thrown);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenFuncReturnsResult_ThrowsArgumentExceptionWithoutCallingMapper()
    {
        // Arrange
        var mapperCalls = new List<Exception>();

        // Act + Assert
        var thrown = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Result<object>.TryAsync(
                async () =>
                {
                    await Task.Yield();
                    return Result.Failure(new TestError());
                },
                ex =>
                {
                    mapperCalls.Add(ex);
                    return new TestError();
                }));
        Assert.IsType<ArgumentException>(thrown);
        Assert.Empty(mapperCalls);
    }

    private static async Task<string> NullAfter(bool yield)
    {
        if (yield)
            await Task.Yield();
        return null!;
    }
}

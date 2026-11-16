using System.Collections.Immutable;

namespace MicroKit.Result.Tests.Errors;

public sealed class ErrorMetadataTests
{
    [Fact]
    public void Empty_WhenRead_IsEmptyAndShared()
    {
        // Arrange + Act
        var first = ErrorMetadata.Empty;
        var second = ErrorMetadata.Empty;

        // Assert
        Assert.Empty(first);
        Assert.Same(second, first);
    }

    [Fact]
    public void Create_WhenSinglePair_ContainsOnlyThatPair()
    {
        // Arrange
        const string key = "userId";

        // Act
        var metadata = ErrorMetadata.Create(key, 42);

        // Assert
        Assert.Single(metadata);
        Assert.Equal(42, metadata[key]);
    }

    [Fact]
    public void Create_WhenSinglePairWithNullValue_ContainsNullValue()
    {
        // Arrange
        const string key = "correlationId";

        // Act
        var metadata = ErrorMetadata.Create(key, null);

        // Assert
        Assert.True(metadata.ContainsKey(key));
        Assert.Null(metadata[key]);
    }

    [Fact]
    public void Create_WhenSingleKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        string? key = null;

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => ErrorMetadata.Create(key!, 1));
    }

    [Fact]
    public void Create_WhenSeveralPairs_ContainsAllPairs()
    {
        // Arrange
        var first = new KeyValuePair<string, object?>("a", 1);
        var second = new KeyValuePair<string, object?>("b", "two");

        // Act
        var metadata = ErrorMetadata.Create(first, second);

        // Assert
        Assert.Equal(2, metadata.Count);
        Assert.Equal(1, metadata["a"]);
        Assert.Equal("two", metadata["b"]);
    }

    [Fact]
    public void Create_WhenDuplicateKey_LaterPairWins()
    {
        // Arrange
        var first = new KeyValuePair<string, object?>("key", "first");
        var second = new KeyValuePair<string, object?>("key", "second");

        // Act
        var metadata = ErrorMetadata.Create(first, second);

        // Assert
        Assert.Single(metadata);
        Assert.Equal("second", metadata["key"]);
    }

    [Fact]
    public void Create_WhenNoPairs_ReturnsEmpty()
    {
        // Arrange
        ReadOnlySpan<KeyValuePair<string, object?>> pairs = [];

        // Act
        var metadata = ErrorMetadata.Create(pairs);

        // Assert
        Assert.Empty(metadata);
    }

    [Fact]
    public void Create_WhenPairKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var pair = new KeyValuePair<string, object?>(null!, 1);

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => ErrorMetadata.Create(pair, pair));
    }

    [Fact]
    public void Create_WhenSinglePair_ReturnsImmutableDictionary()
    {
        // Act
        var metadata = ErrorMetadata.Create("userId", 42);

        // Assert
        Assert.IsType<ImmutableDictionary<string, object?>>(metadata);
    }

    [Fact]
    public void Create_WhenSinglePairCastToMutableDictionary_RejectsWrite()
    {
        // Arrange
        var metadata = ErrorMetadata.Create("userId", 42);
        var writable = (IDictionary<string, object?>)metadata;

        // Act + Assert
        Assert.Throws<NotSupportedException>(() =>
        {
            writable["other"] = 1;
        });
        Assert.Single(metadata);
    }

    [Fact]
    public void Create_WhenSeveralPairs_ReturnsImmutableDictionary()
    {
        // Arrange
        var first = new KeyValuePair<string, object?>("a", 1);
        var second = new KeyValuePair<string, object?>("b", "two");

        // Act
        var metadata = ErrorMetadata.Create(first, second);

        // Assert
        Assert.IsType<ImmutableDictionary<string, object?>>(metadata);
    }

    [Fact]
    public void Create_WhenSeveralPairsCastToMutableDictionary_RejectsWrite()
    {
        // Arrange
        var first = new KeyValuePair<string, object?>("a", 1);
        var second = new KeyValuePair<string, object?>("b", "two");
        var metadata = ErrorMetadata.Create(first, second);
        var writable = (IDictionary<string, object?>)metadata;

        // Act + Assert
        Assert.Throws<NotSupportedException>(() =>
        {
            writable["other"] = 1;
        });
        Assert.Equal(2, metadata.Count);
    }
}

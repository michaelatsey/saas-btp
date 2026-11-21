using System.Collections.Immutable;
using System.Diagnostics;

namespace MicroKit.Result.Tests.Errors;

public sealed class ErrorMetadataBuilderTests
{
    [Fact]
    public void Build_WhenNothingAdded_ReturnsEmpty()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var metadata = builder.Build();

        // Assert
        Assert.Empty(metadata);
    }

    [Fact]
    public void WithTimestamp_WhenCalled_AddsCurrentUtcTime()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();
        var before = DateTimeOffset.UtcNow;

        // Act
        var metadata = builder.WithTimestamp().Build();
        var after = DateTimeOffset.UtcNow;

        // Assert
        var timestamp = Assert.IsType<DateTimeOffset>(metadata["timestamp"]);
        Assert.Equal(TimeSpan.Zero, timestamp.Offset);
        Assert.True(timestamp >= before);
        Assert.True(timestamp <= after);
    }

    [Fact]
    public void WithCorrelationId_WhenValueGiven_AddsCorrelationId()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var metadata = builder.WithCorrelationId("corr-123").Build();

        // Assert
        Assert.Equal("corr-123", metadata["correlationId"]);
    }

    [Fact]
    public void WithCorrelationId_WhenNull_StoresExplicitNull()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var metadata = builder.WithCorrelationId(null).Build();

        // Assert
        Assert.True(metadata.ContainsKey("correlationId"));
        Assert.Null(metadata["correlationId"]);
    }

    [Fact]
    public void WithTraceId_WhenActivityStarted_AddsActivityTraceId()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();
        using var activity = new Activity("error-metadata-test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        // Act
        var metadata = builder.WithTraceId().Build();

        // Assert
        Assert.Equal(activity.TraceId.ToString(), metadata["traceId"]);
        Assert.Equal(32, Assert.IsType<string>(metadata["traceId"]).Length);
    }

    [Fact]
    public void WithTraceId_WhenNoActivity_StoresNull()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();
        var previous = Activity.Current;
        Activity.Current = null;

        try
        {
            // Act
            var metadata = builder.WithTraceId().Build();

            // Assert
            Assert.True(metadata.ContainsKey("traceId"));
            Assert.Null(metadata["traceId"]);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void Add_WhenKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => builder.Add(null!, 1));

        // Assert
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Add_WhenKeyAlreadyPresent_OverwritesValue()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder().Add("key", 1);

        // Act
        var metadata = builder.Add("key", 2).Build();

        // Assert
        Assert.Single(metadata);
        Assert.Equal(2, metadata["key"]);
    }

    [Fact]
    public void Add_WhenChained_ReturnsSameBuilder()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var returned = builder.Add("key", 1);

        // Assert
        Assert.Same(builder, returned);
        Assert.Same(builder, builder.WithTimestamp());
        Assert.Same(builder, builder.WithCorrelationId("c"));
        Assert.Same(builder, builder.WithTraceId());
        Assert.Same(builder, builder.Merge(ErrorMetadata.Empty));
    }

    [Fact]
    public void Merge_WhenKeysOverlap_OtherValuesWinAndNewKeysAdded()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder().Add("shared", "mine").Add("own", 1);
        var other = ErrorMetadata.Create(
            new KeyValuePair<string, object?>("shared", "theirs"),
            new KeyValuePair<string, object?>("extra", true));

        // Act
        var metadata = builder.Merge(other).Build();

        // Assert
        Assert.Equal(3, metadata.Count);
        Assert.Equal("theirs", metadata["shared"]);
        Assert.Equal(1, metadata["own"]);
        Assert.True(metadata["extra"] is true);
    }

    [Fact]
    public void Merge_WhenOtherIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => builder.Merge(null!));

        // Assert
        Assert.Equal("other", exception.ParamName);
    }

    [Fact]
    public void Build_WhenBuilderChangedAfterwards_KeepsBuiltSnapshot()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder().Add("first", 1);
        var snapshot = builder.Build();

        // Act
        var later = builder.Add("second", 2).Build();

        // Assert
        Assert.Single(snapshot);
        Assert.False(snapshot.ContainsKey("second"));
        Assert.Equal(2, later.Count);
    }

    [Fact]
    public void ImplicitConversion_WhenBuilderGiven_ReturnsBuiltDictionary()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder().Add("key", "value");

        // Act
        ImmutableDictionary<string, object?> metadata = builder;

        // Assert
        Assert.Single(metadata);
        Assert.Equal("value", metadata["key"]);
    }

    [Fact]
    public void ImplicitConversion_WhenBuilderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        ErrorMetadataBuilder? builder = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => (ImmutableDictionary<string, object?>)builder!);

        // Assert
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void Build_WhenUsedAsErrorMetadata_IsReadableThroughError()
    {
        // Arrange
        var metadata = new ErrorMetadataBuilder().Add("userId", 7).Build();

        // Act
        IError error = new BuiltMetadataError(metadata);

        // Assert
        Assert.Equal(7, error.TryGetMetadata<int>("userId"));
    }

    private sealed record BuiltMetadataError(IReadOnlyDictionary<string, object?> Values)
        : Error(ErrorCode.From("TEST.BUILT"), "Built metadata error")
    {
        public override IReadOnlyDictionary<string, object?> Metadata => Values;
    }
}

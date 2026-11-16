namespace MicroKit.Persistence.Tests.Query;

/// <summary>
/// Core's delegate-based ordering extension must be callable with extension syntax: named
/// <c>OrderBy</c>, it lost to the <see cref="QueryOptions{TAggregate}.OrderBy"/> property and was
/// reachable only as a static call.
/// </summary>
public sealed class QueryOptionsExtensionsTests
{
    [Fact]
    public void WithOrdering_CalledAsAnExtension_StoresTheOrderingDelegate()
    {
        var opts = new QueryOptions<TestAggregate>()
            .WithOrdering(q => q.OrderByDescending(a => a.Priority).ThenBy(a => a.Name));

        Assert.NotNull(opts.OrderBy);
        var ordered = opts.OrderBy(new[]
        {
            new TestAggregate { Name = "b", Priority = 1 },
            new TestAggregate { Name = "a", Priority = 2 },
            new TestAggregate { Name = "c", Priority = 2 },
        }.AsQueryable()).Select(a => a.Name).ToList();

        Assert.Equal(["a", "c", "b"], ordered);
    }

    [Fact]
    public void WithOrdering_ReplacesAPreviousOrdering_AndLeavesTheSourceUnchanged()
    {
        var original = new QueryOptions<TestAggregate>().WithOrdering(q => q.OrderBy(a => a.Name));
        Func<IQueryable<TestAggregate>, IOrderedQueryable<TestAggregate>> ordering = q => q.OrderBy(a => a.Priority);

        var updated = original.WithOrdering(ordering);

        Assert.Same(ordering, updated.OrderBy);
        Assert.NotSame(ordering, original.OrderBy);
    }

    [Fact]
    public void WithDeleted_SetsIncludeDeleted_AndLeavesTheSourceUnchanged()
    {
        var original = new QueryOptions<TestAggregate>().WithOrdering(q => q.OrderBy(a => a.Name));

        var updated = original.WithDeleted();

        Assert.True(updated.IncludeDeleted);
        Assert.Same(original.OrderBy, updated.OrderBy);
        Assert.False(original.IncludeDeleted);
        Assert.Throws<ArgumentNullException>(() => ((QueryOptions<TestAggregate>)null!).WithDeleted());
    }

    [Fact]
    public void WithOrdering_WhenOptsIsNull_ThrowsArgumentNullException()
    {
        QueryOptions<TestAggregate> opts = null!;
        Assert.Throws<ArgumentNullException>(() => opts.WithOrdering(q => q.OrderBy(a => a.Name)));
    }

    [Fact]
    public void WithOrdering_WhenTheDelegateIsNull_ThrowsArgumentNullException()
    {
        var opts = new QueryOptions<TestAggregate>();
        Assert.Throws<ArgumentNullException>(() => opts.WithOrdering(null!));
    }

    private sealed class TestAggregate : IAggregateRoot
    {
        public string Name { get; init; } = "";

        public int Priority { get; init; }
    }
}

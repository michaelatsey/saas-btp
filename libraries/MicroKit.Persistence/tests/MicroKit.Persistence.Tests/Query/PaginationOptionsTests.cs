namespace MicroKit.Persistence.Tests.Query;

/// <summary>#196 D6: a page is one-based and non-empty, and its offset cannot overflow.</summary>
public sealed class PaginationOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Ctor_WhenPageIsBelowOne_Throws(int page) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaginationOptions(page, 10));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Ctor_WhenPageSizeIsBelowOne_Throws(int pageSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaginationOptions(1, pageSize));

    [Fact]
    public void Ctor_WhenTheOffsetWouldOverflow_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaginationOptions(int.MaxValue, 2));

    [Fact]
    public void Skip_IsTheNumberOfItemsBeforeThePage() =>
        Assert.Equal(40, new PaginationOptions(3, 20).Skip);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public void WithPagination_WithAnInvalidPage_Throws(int page, int pageSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QueryOptions<Item>().WithPagination(page, pageSize));

    // Any aggregate: MicroKit used Testing.TestItem, from MicroKit.Persistence.Testing, not copied.
    private sealed class Item : IAggregateRoot;
}

/// <summary>#196 D6: the limits a read repository enforces are themselves validated.</summary>
public sealed class PaginationLimitsTests
{
    [Theory]
    [InlineData(0, 500)]
    [InlineData(-1, 500)]
    [InlineData(50, 0)]
    [InlineData(600, 500)]
    public void Ctor_WithAnInvalidConfiguration_Throws(int defaultPageSize, int maxPageSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaginationLimits(defaultPageSize, maxPageSize));

    [Fact]
    public void Default_IsAPageOfFiftyAndAMaximumOfFiveHundred()
    {
        Assert.Equal(50, PaginationLimits.Default.DefaultPageSize);
        Assert.Equal(500, PaginationLimits.Default.MaxPageSize);
    }

    [Fact]
    public void ForPagedQuery_WithoutPagination_ReturnsTheFirstDefaultPage() =>
        Assert.Equal(new PaginationOptions(1, 20), new PaginationLimits(20, 100).ForPagedQuery(null));

    [Fact]
    public void ForPagedQuery_AtTheMaximum_ReturnsTheRequest()
    {
        var requested = new PaginationOptions(3, 100);
        Assert.Same(requested, new PaginationLimits(20, 100).ForPagedQuery(requested));
    }

    [Fact]
    public void ForPagedQuery_AboveTheMaximum_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PaginationLimits(20, 100).ForPagedQuery(new PaginationOptions(1, 101)));
}

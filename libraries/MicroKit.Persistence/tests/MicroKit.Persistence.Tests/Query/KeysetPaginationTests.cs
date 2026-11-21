namespace MicroKit.Persistence.Tests.Query;

/// <summary>
/// #197 phase 5: a keyset pagination is built from non-nullable sort keys, owns the ordering and the page of the
/// options it is set on, and is bounded by <see cref="PaginationLimits"/>.
/// </summary>
public sealed class KeysetPaginationTests
{
    [Fact]
    public void TheKeys_AreKeptInOrder_WithTheirDirections()
    {
        var keyset = KeysetPagination<Order>.OrderByDescending(o => o.PlacedAt).ThenBy(o => o.Number).ThenByDescending(o => o.Lines);

        Assert.Equal([("o => o.PlacedAt", true), ("o => o.Number", false), ("o => o.Lines", true)], keyset.Keys.Select(k => (k.Selector.ToString(), k.Descending)));
        Assert.Null(keyset.PageSize);
        Assert.Null(keyset.After);
    }

    [Fact]
    public void ANullableSortKey_IsRefused_WhenTheKeysetIsBuilt()
    {
        Assert.Contains("nullable", Assert.Throws<ArgumentException>(() => KeysetPagination<Order>.OrderBy(o => o.ShippedAt)).Message);
        Assert.Throws<ArgumentException>(() => KeysetPagination<Order>.OrderBy(o => o.Number).ThenByDescending(o => o.ShippedAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageSizeBelowOne_IsRefused(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => KeysetPagination<Order>.OrderBy(o => o.Number) with { PageSize = size });

    [Fact]
    public void WithKeyset_SetsAndClearsTheKeyset()
    {
        var keyset = KeysetPagination<Order>.OrderBy(o => o.Number);

        var opts = new QueryOptions<Order>().WithKeyset(keyset);

        Assert.Same(keyset, opts.Keyset);
        Assert.Null(opts.WithKeyset(null).Keyset);
    }

    [Fact]
    public void Require_ReturnsTheKeyset_AndRefusesOptionsWithoutOne()
    {
        var keyset = KeysetPagination<Order>.OrderBy(o => o.Number);

        Assert.Same(keyset, KeysetRules.Require(new QueryOptions<Order>().WithKeyset(keyset)));
        Assert.Contains("WithKeyset", Assert.Throws<ArgumentException>(() => KeysetRules.Require(new QueryOptions<Order>())).Message);
    }

    [Fact]
    public void AKeyset_OwnsTheOrderingAndThePage()
    {
        var opts = new QueryOptions<Order>().WithKeyset(KeysetPagination<Order>.OrderBy(o => o.Number));

        Assert.Contains("OrderBy", Assert.Throws<ArgumentException>(() => KeysetRules.Require(opts.WithOrdering(q => q.OrderBy(o => o.Number))))
            .Message);
        Assert.Contains("Pagination", Assert.Throws<ArgumentException>(() => KeysetRules.Require(opts.WithPagination(1, 10))).Message);
        Assert.Contains("ListKeysetAsync", Assert.Throws<ArgumentException>(() => KeysetRules.EnsureNone(opts, "ListAsync")).Message);
        Assert.Null(Record.Exception(() => KeysetRules.EnsureNone(new QueryOptions<Order>(), "ListAsync")));
    }

    [Fact]
    public void TheKeysetPageSize_DefaultsToTheDefaultPage_AndIsBoundedByTheMaximum()
    {
        var limits = new PaginationLimits(defaultPageSize: 20, maxPageSize: 100);

        Assert.Equal(20, limits.ForKeysetQuery(null));
        Assert.Equal(100, limits.ForKeysetQuery(100));
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.ForKeysetQuery(101));
    }

    [Fact]
    public void AKeysetPage_HasANextPage_OnlyWithACursor()
    {
        Assert.False(new KeysetPage<int>([1], 1, Next: null).HasNextPage);
        Assert.True(new KeysetPage<int>([1], 1, KeysetCursor.Create("Order", [("Id", false)], [1])).HasNextPage);
    }

    private sealed class Order : IAggregateRoot
    {
        public string Number { get; init; } = "";

        public DateTimeOffset PlacedAt { get; init; }

        public DateTimeOffset? ShippedAt { get; init; }

        public int Lines { get; init; }
    }
}

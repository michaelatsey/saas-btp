using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// PR #200 review, #197 Spec phase 5: an ordering whose worst-case cursor exceeds
/// <c>KeysetCursor.MaxTokenLength</c> is refused when the query is built, naming its keys. The servers' cases are in
/// <c>ReadSideScenarios</c>; this one needs only a model.
/// </summary>
public sealed class KeysetCursorBoundTests
{
    [Fact]
    public async Task AStringKeyWhoseMaximumLengthIsTooLarge_IsRefused_NamingItsLength()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = new BoundDbContext(new DbContextOptionsBuilder<BoundDbContext>().UseSqlite(connection).UseMicroKitPersistence().Options);
        var reads = new EfReadRepository<BoundArticle, BoundDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reads.ListKeysetAsync(new QueryOptions<BoundArticle>().WithKeyset(KeysetPagination<BoundArticle>.OrderBy(a => a.Title)), TestContext.Current.CancellationToken));

        Assert.Contains("'BoundArticle.Title' (maximum length 500)", thrown.Message);
        Assert.Contains("MaxTokenLength (1024)", thrown.Message);
    }

    public sealed class BoundArticle : MicroKit.Domain.Aggregates.IAggregateRoot
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public string Title { get; set; } = string.Empty;
    }

    public sealed class BoundDbContext(DbContextOptions<BoundDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<BoundArticle>().Property(a => a.Title).HasMaxLength(500);
    }
}

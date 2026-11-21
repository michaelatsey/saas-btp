using System.Buffers.Text;
using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #197 phase 5: projections to DTOs from a specification, keyset (cursor) pagination with the root's key
/// appended, and the compiled finds, which evaluate a context-state filter per context. Every scenario runs on
/// PostgreSQL and on SQL Server.
/// </summary>
public abstract class ReadSideScenarios
{
    protected abstract void UseProvider(DbContextOptionsBuilder builder, string database);

    protected abstract IDatabaseFailureClassifier Classifier { get; }

    // ---- Projection --------------------------------------------------------------------------------

    [Fact]
    public async Task AProjection_AppliesTheSpecificationOrderingAndPagination_InTheDatabase_Untracked()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var opts = new QueryOptions<RsOrder>(new PriorityAtLeast(2))
            .WithIncludes(q => q.Include(o => o.Lines)) // dropped by a projection
            .WithOrdering(q => q.OrderByDescending(o => o.Priority).ThenBy(o => o.Number))
            .WithPagination(page: 1, pageSize: 3);

        var rows = await Reads(context).ListAsync(opts, o => new RsOrderRow(o.Number, o.Priority, o.Lines.Count), TestContext.Current.CancellationToken);

        Assert.Equal([new RsOrderRow("O1", 3, 2), new RsOrderRow("O5", 3, 2), new RsOrderRow("O3", 2, 2)], rows);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task APagedProjection_CountsEveryMatch_AndProjectsThePage()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var opts = new QueryOptions<RsOrder>(new PriorityAtLeast(2)).WithOrdering(q => q.OrderBy(o => o.Number)).WithPagination(page: 2, pageSize: 2);

        var page = await Reads(context).ListPagedAsync(opts, o => o.Number, TestContext.Current.CancellationToken);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["O4", "O5"], page.Items);
    }

    [Fact]
    public async Task AProjection_HidesSoftDeletedAggregates_UnlessWithDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        await using (var context = db.Create())
        {
            var id = await context.Orders.Where(o => o.Number == "O1").Select(o => o.Id).SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
            var orders = Orders(context);
            await orders.DeleteAsync(Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
            await new EfUnitOfWork<RsDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var reads = db.Create();
        Assert.DoesNotContain("O1", (await Reads(reads).ListAsync(new QueryOptions<RsOrder>(), o => o.Number, TestContext.Current.CancellationToken)));
        Assert.Contains("O1", (await Reads(reads).ListAsync(new QueryOptions<RsOrder>().WithDeleted(), o => o.Number, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AProjectionOfTwoCollections_KeepsTheSplitHint()
    {
        await using var db = await CreateDatabaseAsync();
        var commands = new CommandLog();
        await using var context = db.Create(interceptor: commands);
        var opts = new QueryOptions<RsOrder>().WithOrdering(q => q.OrderBy(o => o.Number)).AsSplitQuery();

        var rows = await Reads(context).ListAsync(opts, o => new RsOrderCollections(
            o.Number, o.Lines.Select(l => l.Sku).ToList(), o.Payments.Select(p => p.Amount).ToList()), TestContext.Current.CancellationToken);

        Assert.Equal(RsSeed.Orders, rows.Count);
        Assert.All(rows, r => Assert.True(r.Lines.Count == 2 && r.Payments.Count == 2));
        Assert.Equal(3, commands.Texts.Count);
    }

    // ---- Keyset ------------------------------------------------------------------------------------

    [Fact]
    public async Task AKeysetProjectionOfTwoCollections_KeepsTheSplitHint()
    {
        await using var db = await CreateDatabaseAsync();
        var commands = new CommandLog();
        await using var context = db.Create(interceptor: commands);
        var opts = new QueryOptions<RsOrder>().AsSplitQuery().WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Number) with { PageSize = 3 });

        var page = await Reads(context).ListKeysetAsync(opts, o => new RsOrderCollections(
            o.Number, o.Lines.Select(l => l.Sku).ToList(), o.Payments.Select(p => p.Amount).ToList()), TestContext.Current.CancellationToken);

        Assert.Equal(["O1", "O2", "O3"], page.Items.Select(r => r.Number));
        Assert.Equal(3, commands.Texts.Count);
    }

    [Theory]
    [InlineData(false, ">=")]
    [InlineData(true, "<=")]
    public async Task TheKeysetPredicate_BoundsTheFirstKey_SoAnIndexCanSeek(bool descending, string bound)
    {
        await using var db = await CreateDatabaseAsync();
        var commands = new CommandLog();
        await using var context = db.Create(interceptor: commands);
        var keyset = (descending ? KeysetPagination<RsOrder>.OrderByDescending(o => o.Priority) : KeysetPagination<RsOrder>.OrderBy(o => o.Priority))
            with { PageSize = 2 };
        var next = (await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset), TestContext.Current.CancellationToken)).Next;
        commands.Texts.Clear();

        await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset with { After = next }), TestContext.Current.CancellationToken);

        Assert.Contains(bound, Assert.Single(commands.Texts));
    }

    [Fact]
    public async Task ASortKeyReadThroughAnInterface_InGenericCode_IsAccepted()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var expected = await context.Orders.OrderByDescending(o => o.PlacedAt).ThenBy(o => o.Id).Select(o => o.Number).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The shape a compiler emits for a member reached through an interface: ((IRsPlaced)x).PlacedAt.
        var x = System.Linq.Expressions.Expression.Parameter(typeof(RsOrder), "x");
        var throughInterface = System.Linq.Expressions.Expression.Lambda<Func<RsOrder, DateTimeOffset>>(
            System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Convert(x, typeof(IRsPlaced)), nameof(IRsPlaced.PlacedAt)), x);

        var (walked, _) = await WalkAsync(Reads(context), new QueryOptions<RsOrder>().WithKeyset(KeysetPagination<RsOrder>.OrderByDescending(throughInterface) with { PageSize = 3 }), o => o.Number);
        var (generic, _) = await WalkAsync(Reads(context), new QueryOptions<RsOrder>().WithKeyset(RsKeysets.Newest<RsOrder>() with { PageSize = 3 }), o => o.Number);

        Assert.Equal(expected, walked);
        Assert.Equal(expected, generic);
    }

    [Fact]
    public async Task AnyAndCount_RefuseAKeyset_LikeTheLists()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var opts = new QueryOptions<RsOrder>().WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Number));

        Assert.Contains("ListKeysetAsync", (await Assert.ThrowsAsync<ArgumentException>(async () => await Reads(context).AnyAsync(opts, TestContext.Current.CancellationToken))).Message);
        Assert.Contains("ListKeysetAsync", (await Assert.ThrowsAsync<ArgumentException>(async () => await Reads(context).CountAsync(opts, TestContext.Current.CancellationToken))).Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(10)]
    public async Task WalkingKeysetPages_ReturnsEveryRowOnce_InTheDatabasesOrder_WithTheKeyBreakingTies(int pageSize)
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var expected = await context.Orders.OrderByDescending(o => o.Priority).ThenBy(o => o.PlacedAt).ThenBy(o => o.Id)
            .Select(o => o.Number).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var keyset = KeysetPagination<RsOrder>.OrderByDescending(o => o.Priority).ThenBy(o => o.PlacedAt) with { PageSize = pageSize };

        var (walked, pages) = await WalkAsync(Reads(context), new QueryOptions<RsOrder>().WithKeyset(keyset), o => o.Number);

        Assert.Equal(expected, walked);
        Assert.Equal(RsSeed.Orders, walked.Count);
        Assert.Equal((RsSeed.Orders + pageSize - 1) / pageSize, pages);
    }

    [Fact]
    public async Task AKeysetOnAStronglyTypedId_WalksEveryRow_WithTheSpecificationApplied()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var expected = await context.Orders.Where(o => o.Priority >= 2).OrderBy(o => o.Id).Select(o => o.Number).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opts = new QueryOptions<RsOrder>(new PriorityAtLeast(2)).WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Id) with { PageSize = 2 });

        var (walked, _) = await WalkAsync(Reads(context), opts, o => o.Number);

        Assert.Equal(expected, walked);
    }

    [Fact]
    public async Task AKeysetOnACompositeKeyAndADateTimeOffset_WalksEveryRow()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var expected = await context.Shipments.OrderByDescending(s => s.At).ThenBy(s => s.Region).ThenBy(s => s.Number)
            .Select(s => s.Region + s.Number).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var reads = new EfReadRepository<RsShipment, RsDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);
        var opts = new QueryOptions<RsShipment>().WithKeyset(KeysetPagination<RsShipment>.OrderByDescending(s => s.At) with { PageSize = 2 });

        var (walked, _) = await WalkAsync(reads, opts, s => s.Region + s.Number);

        Assert.Equal(expected, walked);
        Assert.Equal(RsSeed.Shipments, walked.Count);
    }

    [Fact]
    public async Task AKeysetPageOfAggregates_AppliesTheIncludes_Untracked()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var opts = new QueryOptions<RsOrder>().WithIncludes(q => q.Include(o => o.Lines))
            .WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Number) with { PageSize = 3 });

        var page = await Reads(context).ListKeysetAsync(opts, TestContext.Current.CancellationToken);

        Assert.Equal(["O1", "O2", "O3"], page.Items.Select(o => o.Number));
        Assert.All(page.Items, o => Assert.True(o.Lines.Count == 2));
        Assert.True(page.HasNextPage);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ATamperedToken_IsRefused_ByTryParse_OrAtQueryTime()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var keyset = KeysetPagination<RsOrder>.OrderBy(o => o.Priority) with { PageSize = 2 };
        var token = Present.NotNull((await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset), TestContext.Current.CancellationToken)).Next).ToString();

        Assert.False(KeysetCursor.TryParse(token[..^2], out _), "truncated");
        Assert.False(KeysetCursor.TryParse(token + "!", out _), "not Base64Url");

        // Well formed, but the first value's tag says uint where the key is stored as int.
        var bytes = Base64Url.DecodeFromChars(token);
        Assert.Equal((byte)6, bytes[10]);
        bytes[10] = 7;
        Assert.True(KeysetCursor.TryParse(Base64Url.EncodeToString(bytes), out var forged));
        var thrown = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset with { After = forged }), TestContext.Current.CancellationToken));
        Assert.Contains("does not match the sort key 'RsOrder.Priority'", thrown.Message);
    }

    [Fact]
    public async Task AToken_ReusedWithAnotherOrdering_OrForAnotherAggregateType_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var byId = KeysetPagination<RsOrder>.OrderBy(o => o.Id) with { PageSize = 1 };
        var next = Present.NotNull((await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(byId), TestContext.Current.CancellationToken)).Next);

        Assert.Contains("another ordering", (await Assert.ThrowsAsync<ArgumentException>(async () => await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(KeysetPagination<RsOrder>.OrderByDescending(o => o.Id) with { After = next }), TestContext.Current.CancellationToken)))
            .Message);
        Assert.Contains("another ordering", (await Assert.ThrowsAsync<ArgumentException>(async () => await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Priority) with { After = next }), TestContext.Current.CancellationToken)))
            .Message);

        // Same key name (Id), same provider type (Guid), another aggregate.
        var invoices = new EfReadRepository<RsInvoice, RsDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);
        Assert.Contains("another aggregate type", (await Assert.ThrowsAsync<ArgumentException>(async () => await invoices.ListKeysetAsync(new QueryOptions<RsInvoice>().WithKeyset(KeysetPagination<RsInvoice>.OrderBy(i => i.Id) with { After = next }), TestContext.Current.CancellationToken)))
            .Message);
    }

    public enum BadKey
    {
        NullableColumn,
        NotAProperty,
        UnsupportedProviderType,
        PrimitiveCollection,
        UnboundedString,
    }

    [Theory]
    [InlineData(BadKey.NullableColumn, "'RsOrder.Note' is nullable")]
    [InlineData(BadKey.NotAProperty, "is not a scalar property of 'RsOrder'")]
    [InlineData(BadKey.UnsupportedProviderType, "'RsOrder.Duration' is stored as 'TimeSpan'")]
    [InlineData(BadKey.PrimitiveCollection, "'RsOrder.Codes' is a primitive collection")]
    [InlineData(BadKey.UnboundedString, "'RsOrder.Tenant' is a string without a maximum length")]
    public async Task ASortKeyTheCursorCannotCarry_IsRefused_NamingIt(BadKey bad, string reason)
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var keyset = bad switch
        {
            BadKey.NullableColumn => KeysetPagination<RsOrder>.OrderBy(o => o.Note),
            BadKey.NotAProperty => KeysetPagination<RsOrder>.OrderBy(o => o.Number.Length),
            BadKey.UnsupportedProviderType => KeysetPagination<RsOrder>.OrderBy(o => o.Duration),
            BadKey.UnboundedString => KeysetPagination<RsOrder>.OrderBy(o => o.Tenant),
            _ => KeysetPagination<RsOrder>.OrderBy(o => o.Codes),
        };

        var thrown = await Assert.ThrowsAnyAsync<Exception>(async () => await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset), TestContext.Current.CancellationToken));

        Assert.IsAssignableFrom(bad is BadKey.UnsupportedProviderType or BadKey.PrimitiveCollection or BadKey.UnboundedString
            ? typeof(InvalidOperationException)
            : typeof(ArgumentException), thrown);
        Assert.Contains(reason, thrown.Message);
    }

    // PR #200 review, #197 Spec phase 5: the cursor's size is bounded when the query is built, never on the page
    // that happens to carry a long value. Number is HasMaxLength(20): its worst case fits, so the walk ends.
    [Fact]
    public async Task ABoundedStringKeyThatFits_PagesToTheEnd_WithItsLongestValues()
    {
        await using var db = await CreateDatabaseAsync();
        await using (var seed = db.Create())
        {
            for (var i = 0; i < 5; i++)
                seed.Orders.Add(new RsOrder { Number = new string('€', 19) + i, PlacedAt = DateTimeOffset.UnixEpoch });
            await new EfUnitOfWork<RsDbContext>(seed).CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var context = db.Create();
        var keyset = KeysetPagination<RsOrder>.OrderBy(o => o.Number) with { PageSize = 2 };
        var seen = new List<string>();
        for (var page = await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset), TestContext.Current.CancellationToken); ;
             page = await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset with { After = page.Next }), TestContext.Current.CancellationToken))
        {
            seen.AddRange(page.Items.Select(o => o.Number));
            if (page.Next is null)
                break;
        }

        Assert.Equal(await context.Orders.OrderBy(o => o.Number).ThenBy(o => o.Id).Select(o => o.Number).ToListAsync(cancellationToken: TestContext.Current.CancellationToken), seen);
        Assert.Contains(new string('€', 19) + 4, seen);
    }

    [Fact]
    public async Task AKeyset_OwnsTheOrderingAndThePage_AndIsBounded()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var keyset = KeysetPagination<RsOrder>.OrderBy(o => o.Number);
        var reads = new EfReadRepository<RsOrder, RsDbContext>(context, new EfSpecificationEvaluator(), new PaginationLimits(defaultPageSize: 2, maxPageSize: 3));

        Assert.Equal(2, (await reads.ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset), TestContext.Current.CancellationToken)).Items.Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await reads.ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset with { PageSize = 4 }), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(async () => await reads.ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset).WithOrdering(q => q.OrderBy(o => o.Number)), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(async () => await reads.ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset).WithPagination(1, 2), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(async () => await reads.ListAsync(new QueryOptions<RsOrder>().WithKeyset(keyset), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(async () => await reads.ListPagedAsync(new QueryOptions<RsOrder>().WithKeyset(keyset), o => o.Number, TestContext.Current.CancellationToken));
    }

    // ---- Compiled finds ----------------------------------------------------------------------------

    [Fact]
    public async Task TheCompiledFinds_EvaluateAContextStateFilter_PerContext()
    {
        await using var db = await CreateDatabaseAsync();
        RsOrderId inA, inB;
        await using (var seed = db.Create())
        {
            inA = await seed.Orders.Where(o => o.Tenant == "a").Select(o => o.Id).FirstAsync(cancellationToken: TestContext.Current.CancellationToken);
            inB = await seed.Orders.Where(o => o.Tenant == "b").Select(o => o.Id).FirstAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var tenantA = db.Create(tenant: "a");
        await using var tenantB = db.Create(tenant: "b");

        // The same compiled queries, two contexts, two tenants: each sees its own only.
        Assert.NotNull((await Reads(tenantA).FindAsync([inA], TestContext.Current.CancellationToken)));
        Assert.Null((await Reads(tenantA).FindAsync([inB], TestContext.Current.CancellationToken)));
        Assert.NotNull((await Reads(tenantB).FindAsync([inB], TestContext.Current.CancellationToken)));
        Assert.Null((await Reads(tenantB).FindAsync([inA], TestContext.Current.CancellationToken)));

        Assert.Null((await Orders(tenantA).FindAsync(inB, TestContext.Current.CancellationToken)));
        Assert.Equal(2, Present.NotNull((await Orders(tenantB).FindAsync(inB, TestContext.Current.CancellationToken))).Lines.Count);
        Assert.NotNull((await Orders(tenantA).FindAsync(inA, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task TheCompiledFinds_ServeEachModel_OfOneContextType()
    {
        await using var db = await CreateDatabaseAsync();
        await using var first = db.Create();
        await using var other = db.Create(variant: 1);
        var id = await first.Orders.Select(o => o.Id).FirstAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull((await Reads(first).FindAsync([id], TestContext.Current.CancellationToken)));
        Assert.NotNull((await Reads(other).FindAsync([id], TestContext.Current.CancellationToken)));
        Assert.NotNull((await Orders(first).FindAsync(id, TestContext.Current.CancellationToken)));
        Assert.NotNull((await Orders(other).FindAsync(id, TestContext.Current.CancellationToken)));
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    private static EfReadRepository<RsOrder, RsDbContext> Reads(RsDbContext context) =>
        new(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

    private static EfRepository<RsOrder, RsOrderId, RsDbContext> Orders(RsDbContext context) => new(context);

    private static async Task<(List<TResult> Walked, int Pages)> WalkAsync<TAggregate, TResult>(
        IReadRepository<TAggregate> reads,
        QueryOptions<TAggregate> opts,
        System.Linq.Expressions.Expression<Func<TAggregate, TResult>> selector)
        where TAggregate : class, IAggregateRoot
    {
        var walked = new List<TResult>();
        var pages = 0;
        KeysetCursor? after = null;
        do
        {
            // Through the token, as a client would send it back.
            var page = await reads.ListKeysetAsync(opts.WithKeyset(opts.Keyset! with { After = after }), selector);
            walked.AddRange(page.Items);
            pages++;
            after = page.Next is { } next && KeysetCursor.TryParse(next.ToString(), out var parsed) ? parsed : null;
            if (pages > 50)
                throw new InvalidOperationException("the walk does not end");
        }
        while (after is not null);

        return (walked, pages);
    }

    private async Task<Database> CreateDatabaseAsync()
    {
        var db = new Database(this, "readside_" + Guid.NewGuid().ToString("N"));
        await using var context = db.Create();
        await context.Database.EnsureCreatedAsync();
        RsSeed.Into(context);
        await context.SaveChangesAsync();
        return db;
    }

    private sealed class Database(ReadSideScenarios owner, string name) : IAsyncDisposable
    {
        public RsDbContext Create(string? tenant = null, int variant = 0, CommandLog? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<RsDbContext>();
            owner.UseProvider(builder, name);
            if (interceptor is not null)
                builder.AddInterceptors(interceptor);
            ModelVariantCacheKeyFactory.Use(builder);
            builder.UseMicroKitPersistence(owner.Classifier);
            return new RsDbContext(builder.Options, variant) { Tenant = tenant };
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Create();
            await context.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>Records the text of every command that reads.</summary>
    public sealed class CommandLog : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class PriorityAtLeast(int priority) : MicroKit.Domain.Specifications.Specification<RsOrder>
    {
        public override System.Linq.Expressions.Expression<Func<RsOrder, bool>> ToExpression() => o => o.Priority >= priority;
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class ReadSidePostgreSqlTests(PostgreSqlSuiteFixture fixture) : ReadSideScenarios
{
    protected override IDatabaseFailureClassifier Classifier => MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlFailureClassifier.Instance;

    protected override void UseProvider(DbContextOptionsBuilder builder, string database) =>
        builder.UseNpgsql(fixture.GetConnectionString(database));
}

public readonly record struct RsOrderId(Guid Value) : IEntityId;

/// <summary>A capability an aggregate exposes through an interface, for generic read code.</summary>
public interface IRsPlaced
{
    DateTimeOffset PlacedAt { get; }
}

public static class RsKeysets
{
    // The compiler reads x.PlacedAt as ((IRsPlaced)x).PlacedAt: the key's target is a Convert of the parameter.
    public static KeysetPagination<T> Newest<T>()
        where T : class, IAggregateRoot, IRsPlaced =>
        KeysetPagination<T>.OrderByDescending(x => x.PlacedAt);
}

public sealed class RsOrder : IAggregateRoot, IRsPlaced
{
    public RsOrderId Id { get; init; } = new(Guid.NewGuid());

    public string Number { get; set; } = string.Empty;

    public int Priority { get; set; }

    public DateTimeOffset PlacedAt { get; set; }

    public string Tenant { get; set; } = "a";

    public string? Note { get; set; }

    public TimeSpan Duration { get; set; }

    public List<int> Codes { get; set; } = [];

    public List<RsLine> Lines { get; } = [];

    public List<RsPayment> Payments { get; } = [];
}

public sealed class RsPayment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public decimal Amount { get; set; }
}

public sealed record RsOrderCollections(string Number, List<string> Lines, List<decimal> Payments);

public sealed class RsLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;
}

/// <summary>Another aggregate whose key has the same name and provider type as <see cref="RsOrder"/>'s.</summary>
public sealed class RsInvoice : IAggregateRoot
{
    public RsOrderId Id { get; init; } = new(Guid.NewGuid());
}

/// <summary>An aggregate with a composite primary key.</summary>
public sealed class RsShipment : IAggregateRoot
{
    public string Region { get; init; } = string.Empty;

    public int Number { get; init; }

    public DateTimeOffset At { get; set; }
}

public sealed record RsOrderRow(string Number, int Priority, int Lines);

public static class RsSeed
{
    public const int Orders = 7;

    public const int Shipments = 5;

    private static readonly DateTimeOffset Start = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    // Ties on Priority and on PlacedAt: the appended id orders them.
    public static void Into(RsDbContext context)
    {
        int[] priorities = [3, 1, 2, 2, 3, 1, 2];
        for (var i = 0; i < Orders; i++)
        {
            var order = new RsOrder
            {
                Number = "O" + (i + 1),
                Priority = priorities[i],
                PlacedAt = Start.AddMinutes(i % 3),
                Tenant = i < 2 ? "b" : "a",
                Note = i % 2 == 0 ? null : "note",
            };
            order.Lines.Add(new RsLine { Sku = "A" });
            order.Lines.Add(new RsLine { Sku = "B" });
            order.Payments.Add(new RsPayment { Amount = 10m });
            order.Payments.Add(new RsPayment { Amount = 5m });
            context.Orders.Add(order);
        }

        context.Add(new RsInvoice());
        context.Add(new RsShipment { Region = "EU", Number = 2, At = Start });
        context.Add(new RsShipment { Region = "EU", Number = 1, At = Start });
        context.Add(new RsShipment { Region = "US", Number = 1, At = Start.AddHours(1) });
        context.Add(new RsShipment { Region = "AS", Number = 9, At = Start });
        context.Add(new RsShipment { Region = "US", Number = 3, At = Start.AddHours(-1) });
    }
}

public sealed class RsDbContext(DbContextOptions<RsDbContext> options, int variant = 0) : DbContext(options), IModelVariant
{
    public string? Tenant { get; set; }

    public DbSet<RsOrder> Orders => Set<RsOrder>();

    public DbSet<RsShipment> Shipments => Set<RsShipment>();

    object IModelVariant.Variant => variant;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RsOrder>(b =>
        {
            b.Property(o => o.Id).HasConversion(id => id.Value, v => new RsOrderId(v)).ValueGeneratedNever();
            b.Property(o => o.Number).HasMaxLength(variant == 1 ? 40 : 20);
            b.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
            b.HasMany(o => o.Payments).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
            b.HasSoftDelete();
            b.HasQueryFilter("Tenant", o => Tenant == null || o.Tenant == Tenant);
        });
        modelBuilder.Entity<RsLine>().Property(l => l.Id).ValueGeneratedNever();
        modelBuilder.Entity<RsPayment>(b =>
        {
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Amount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<RsInvoice>().Property(i => i.Id).HasConversion(id => id.Value, v => new RsOrderId(v)).ValueGeneratedNever();
        modelBuilder.Entity<RsShipment>(b =>
        {
            b.HasKey(s => new { s.Region, s.Number });
            b.Property(s => s.Region).HasMaxLength(10);
        });
    }
}

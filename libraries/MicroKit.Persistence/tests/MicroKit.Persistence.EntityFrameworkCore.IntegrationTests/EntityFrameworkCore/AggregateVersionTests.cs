using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #197 phase 2: every aggregate root carries an application-managed version, bumped by every save
/// that writes any entity of its aggregate. Every scenario runs on PostgreSQL and on SQL Server.
/// </summary>
public abstract class AggregateVersionScenarios
{
    /// <summary>Configures the provider on <paramref name="database"/>, with a retrying strategy when asked.</summary>
    protected abstract void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null);

    // ---- The model ---------------------------------------------------------------------------

    [Fact]
    public void TheVersion_IsAShadowLongConcurrencyToken_OnTheTopMostRootOnly()
    {
        using var carts = new CartDbContext(Options<CartDbContext>("unused"));
        var token = Present.NotNull(carts.Model.FindEntityType(typeof(Cart))!.FindProperty(AggregateVersion.PropertyName));
        Assert.True(token.IsShadowProperty());
        Assert.Equal(typeof(long), token.ClrType);
        Assert.True(token.IsConcurrencyToken);
        Assert.Null(carts.Model.FindEntityType(typeof(CartLine))!.FindProperty(AggregateVersion.PropertyName));
        Assert.Null(carts.Model.FindEntityType(typeof(AuditRow))!.FindProperty(AggregateVersion.PropertyName));

        using var pets = new PetDbContext(Options<PetDbContext>("unused"));
        Assert.NotNull(pets.Model.FindEntityType(typeof(Pet))!.FindDeclaredProperty(AggregateVersion.PropertyName));
        Assert.Null(pets.Model.FindEntityType(typeof(Dog))!.FindDeclaredProperty(AggregateVersion.PropertyName));
    }

    [Fact]
    public void TheVersionColumn_FollowsTheConsumersNamingConvention()
    {
        using var context = new SnakeCaseCartDbContext(Options<SnakeCaseCartDbContext>("unused"));
        var cart = context.Model.FindEntityType(typeof(Cart))!;

        Assert.Equal("aggregate_version", cart.FindProperty(AggregateVersion.PropertyName)!
            .GetColumnName(StoreObjectIdentifier.Table(cart.GetTableName()!, cart.GetSchema())));
    }

    [Theory]
    [InlineData(VersionMisuse.ClrMember, "maps it to a CLR member")]
    [InlineData(VersionMisuse.NotLong, "not 'long'")]
    [InlineData(VersionMisuse.NotAToken, "not to be a concurrency token")]
    [InlineData(VersionMisuse.SameColumn, "is also mapped by")]
    [InlineData(VersionMisuse.SameColumnUnderTpc, "is also mapped by")]
    [InlineData(VersionMisuse.DefaultValue, "a default value")]
    [InlineData(VersionMisuse.DefaultValueSql, "a default value")]
    [InlineData(VersionMisuse.ComputedSql, "a default value, or a computed one")]
    [InlineData(VersionMisuse.ValueGenerated, "configured as generated")]
    [InlineData(VersionMisuse.Converted, "a value converter")]
    [InlineData(VersionMisuse.ProviderType, "a value converter")]
    public void AMisusedVersion_FailsModelBuilding_NamingTheRoot(VersionMisuse misuse, string reason)
    {
        using var context = new MisusedVersionDbContext(Options<MisusedVersionDbContext>("unused"), misuse);

        var thrown = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("'AggregateVersion' is reserved on the aggregate root", thrown.Message);
        Assert.Contains(reason, thrown.Message);
    }

    [Theory]
    [InlineData(VersionMisuse.OtherTable)]
    [InlineData(VersionMisuse.OtherTpcTable)]
    [InlineData(VersionMisuse.SiblingRoots)]
    public void AnAggregateVersionPropertyInAnotherTable_IsNotACollision(VersionMisuse variant)
    {
        using var context = new MisusedVersionDbContext(Options<MisusedVersionDbContext>("unused"), variant);

        Assert.Null(Record.Exception(() => _ = context.Model));
    }

    [Fact]
    public async Task AColumnRenamedByTheApplication_HoldsTheVersion()
    {
        await using var db = await CreateDatabaseAsync<RenamedVersionDbContext>(o => new RenamedVersionDbContext(o));
        var id = new CartId(Guid.NewGuid());
        await using (var context = db.Create())
        {
            await new EfRepository<Cart, CartId, RenamedVersionDbContext>(context).AddAsync(new Cart { Id = id, Owner = "a" }, TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using (var context = db.Create())
        {
            var cart = Present.NotNull((await new EfRepository<Cart, CartId, RenamedVersionDbContext>(context).FindAsync(id, TestContext.Current.CancellationToken)));
            cart.Owner = "b";
            await Commit(context);
            var type = context.Model.FindEntityType(typeof(Cart))!;
            Assert.Equal("row_version", type.FindProperty(AggregateVersion.PropertyName)!
                .GetColumnName(StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema())));
        }

        await using var verify = db.Create();
        Assert.Equal(2, (await verify.Set<Cart>().Where(c => c.Id == id).Select(c => EF.Property<long>(c, AggregateVersion.PropertyName)).SingleAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    // ---- What bumps --------------------------------------------------------------------------

    [Fact]
    public async Task AnInsertedAggregate_StartsAtOne_AndAnAddedRootReadsZeroUntilItsCommit()
    {
        await using var db = await CreateCartDatabaseAsync();
        await using var context = db.Create();
        var carts = Carts(context);
        var cart = new Cart { Id = new CartId(Guid.NewGuid()), Owner = "new" };
        await carts.AddAsync(cart, TestContext.Current.CancellationToken);

        Assert.Equal(0, carts.GetVersion(cart));
        await Commit(context);

        Assert.Equal(1, carts.GetVersion(cart));
        Assert.Equal(1, (await StoredVersionAsync(db, cart.Id)));
    }

    public enum CartChange
    {
        RootProperty,
        ChildProperty,
        GrandChildProperty,
        OwnedProperty,
        ChildAdded,
        ChildDeleted,
        LinkAdded,
        LinkRemoved,
        PrincipalSideChildProperty,
        PrincipalSideChildReplaced,
    }

    [Theory]
    [InlineData(CartChange.RootProperty)]
    [InlineData(CartChange.ChildProperty)]
    [InlineData(CartChange.GrandChildProperty)]
    [InlineData(CartChange.OwnedProperty)]
    [InlineData(CartChange.ChildAdded)]
    [InlineData(CartChange.ChildDeleted)]
    [InlineData(CartChange.LinkAdded)]
    [InlineData(CartChange.LinkRemoved)]
    [InlineData(CartChange.PrincipalSideChildProperty)]
    [InlineData(CartChange.PrincipalSideChildReplaced)]
    public async Task AnyWriteInsideTheBoundary_BumpsTheRootOnce(CartChange change)
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var cart = Present.NotNull((await Carts(context).FindAsync(id, TestContext.Current.CancellationToken)));
            Apply(context, cart, change);
            await Commit(context);
        }

        Assert.Equal(2, (await StoredVersionAsync(db, id)));
    }

    [Fact]
    public async Task AGrandChildUnderAChildWithAByteArrayKey_BumpsTheRoot()
    {
        await using var db = await CreateDatabaseAsync<ArchiveDbContext>(o => new ArchiveDbContext(o));
        var archive = new Archive { Id = new CartId(Guid.NewGuid()) };
        var document = new ArchivedDocument { Code = [1, 2, 3], Title = "minutes" };
        document.Pages.Add(new ArchivedPage { Text = "page one" });
        archive.Documents.Add(document);
        await using (var context = db.Create())
        {
            await new EfRepository<Archive, CartId, ArchiveDbContext>(context).AddAsync(archive, TestContext.Current.CancellationToken);
            await new EfUnitOfWork<ArchiveDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = db.Create())
        {
            var loaded = Present.NotNull((await new EfRepository<Archive, CartId, ArchiveDbContext>(context).FindAsync(archive.Id, TestContext.Current.CancellationToken)));
            // The page's foreign key is another byte[] instance than its document's key: matched by
            // EF Core's key comparer, not by reference.
            loaded.Documents.Single().Pages.Single().Text = "page one, amended";
            await new EfUnitOfWork<ArchiveDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = db.Create();
        Assert.Equal(2, (await verify.Set<Archive>().Where(a => a.Id == archive.Id).Select(a => EF.Property<long>(a, AggregateVersion.PropertyName)).SingleAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task TwoSaves_BumpTwice()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        await using var context = db.Create();
        var carts = Carts(context);
        var cart = Present.NotNull((await carts.FindAsync(id, TestContext.Current.CancellationToken)));

        cart.Owner = "first";
        await Commit(context);
        cart.Lines[0].Sku = "second";
        await Commit(context);

        Assert.Equal(3, carts.GetVersion(cart));
        Assert.Equal(3, (await StoredVersionAsync(db, id)));
    }

    [Fact]
    public async Task ASaveThatFailsAndIsRepeated_BumpsOnce()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        var failOnce = new FailingInsertInterceptor(failures: 1, failure: () => new InvalidOperationException("the insert failed"));
        await using var context = db.Create(o => o.AddInterceptors(failOnce));
        var cart = Present.NotNull((await Carts(context).FindAsync(id, TestContext.Current.CancellationToken)));
        cart.Lines.Add(new CartLine { Sku = "added" });

        await Assert.ThrowsAsync<PersistenceException>(() => Commit(context));
        Assert.Equal(1, Carts(context).GetVersion(cart));
        await Commit(context);

        Assert.Equal(1, failOnce.Thrown);
        Assert.Equal(2, (await StoredVersionAsync(db, id)));
    }

    // ---- Moves, and roots that are not tracked -------------------------------------------------

    [Fact]
    public async Task AChildMovedBetweenTwoRecordedAggregates_BumpsBoth()
    {
        await using var db = await CreateCartDatabaseAsync();
        var from = await SeedCartAsync(db);
        var to = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var carts = Carts(context);
            var source = Present.NotNull((await carts.FindAsync(from, TestContext.Current.CancellationToken)));
            var target = Present.NotNull((await carts.FindAsync(to, TestContext.Current.CancellationToken)));
            var line = source.Lines[0];
            source.Lines.Remove(line);
            target.Lines.Add(line);
            await Commit(context);
        }

        Assert.Equal(2, (await StoredVersionAsync(db, from)));
        Assert.Equal(2, (await StoredVersionAsync(db, to)));
    }

    [Fact]
    public async Task AChildMovedToAnAggregateNotLoadedByItsRepository_FailsTheSave_NamingTheRoots()
    {
        await using var db = await CreateCartDatabaseAsync();
        var from = await SeedCartAsync(db);
        var to = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var source = Present.NotNull((await Carts(context).FindAsync(from, TestContext.Current.CancellationToken)));
            var target = await context.Carts.Include(c => c.Lines).SingleAsync(c => c.Id == to, cancellationToken: TestContext.Current.CancellationToken); // not recorded
            var line = source.Lines[0];
            source.Lines.Remove(line);
            target.Lines.Add(line);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Commit(context));
            Assert.Contains("'CartLine' moves from 'Cart'", thrown.Message);
            Assert.Contains(to.ToString(), thrown.Message);
        }

        Assert.Equal(1, (await StoredVersionAsync(db, from)));
        await using var verify = db.Create();
        Assert.Equal(2, (await verify.Set<CartLine>().CountAsync(l => EF.Property<CartId>(l, "CartId") == from, cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AChangedChildWhoseRootIsNotTracked_FailsTheSave()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        await using var context = db.Create();
        var line = await context.Set<CartLine>().FirstAsync(l => EF.Property<CartId>(l, "CartId") == id, cancellationToken: TestContext.Current.CancellationToken);
        line.Sku = "orphaned";

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Commit(context));

        Assert.Contains("'CartLine' changed, but the root of its aggregate, 'Cart', is not tracked", thrown.Message);
    }

    [Fact]
    public async Task AChildMovedIntoAnAggregateWhoseRootIsNotTracked_FailsTheSave()
    {
        await using var db = await CreateCartDatabaseAsync();
        var from = await SeedCartAsync(db);
        var to = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var source = Present.NotNull((await Carts(context).FindAsync(from, TestContext.Current.CancellationToken)));
            context.Entry(source.Lines[0]).Property("CartId").CurrentValue = to; // the target cart is not loaded

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Commit(context));
            Assert.Contains("is not tracked", thrown.Message);
        }

        Assert.Equal(1, (await StoredVersionAsync(db, to)));
    }

    [Fact]
    public async Task AChildTrackedAloneAndMovedIntoALoadedAggregate_FailsTheSave()
    {
        await using var db = await CreateCartDatabaseAsync();
        var from = await SeedCartAsync(db);
        var to = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var line = await context.Set<CartLine>().FirstAsync(l => EF.Property<CartId>(l, "CartId") == from, cancellationToken: TestContext.Current.CancellationToken); // its root is not tracked
            var target = Present.NotNull((await Carts(context).FindAsync(to, TestContext.Current.CancellationToken)));
            target.Lines.Add(line);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Commit(context));
            Assert.Contains("is not tracked", thrown.Message);
        }

        Assert.Equal(1, (await StoredVersionAsync(db, from)));
        Assert.Equal(1, (await StoredVersionAsync(db, to)));
    }

    [Fact]
    public async Task ALinkThroughASkipNavigationMarkedOutside_IsNeitherLoadedNorVersioned()
    {
        await using var db = await CreateDatabaseAsync<LabelledBasketDbContext>(o => new LabelledBasketDbContext(o));
        var basket = new Basket { Id = new CartId(Guid.NewGuid()) };
        basket.Labels.Add(new BasketTag { Text = "seeded" });
        await using (var context = db.Create())
        {
            await new EfRepository<Basket, CartId, LabelledBasketDbContext>(context).AddAsync(basket, TestContext.Current.CancellationToken);
            await new EfUnitOfWork<LabelledBasketDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = db.Create())
        {
            var loaded = Present.NotNull((await new EfRepository<Basket, CartId, LabelledBasketDbContext>(context).FindAsync(basket.Id, TestContext.Current.CancellationToken)));
            Assert.Empty(loaded.Labels);
            loaded.Labels.Add(new BasketTag { Text = "fragile" });
            await new EfUnitOfWork<LabelledBasketDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = db.Create();
        Assert.Equal(1, (await verify.Set<Basket>().Where(b => b.Id == basket.Id).Select(b => EF.Property<long>(b, AggregateVersion.PropertyName)).SingleAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AnInterceptorAddedByTheApplication_RunsAfterTheVersionStep()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        var probe = new VersionProbeInterceptor();
        await using var context = db.Create(o => o.AddInterceptors(probe));
        var cart = Present.NotNull((await Carts(context).FindAsync(id, TestContext.Current.CancellationToken)));
        cart.Owner = "changed";

        await Commit(context);

        Assert.Equal(2L, probe.SeenVersion);
    }

    // ---- References between aggregates ---------------------------------------------------------

    [Fact]
    public async Task LinksAreLoadedWithTheOrder_AndTheLinkedRootLoadsThroughItsOwnRepository()
    {
        await using var db = await CreateDatabaseAsync<TaggedOrderDbContext>(o => new TaggedOrderDbContext(o));
        var (orderId, labelId, _) = await SeedTaggedOrderAsync(db);
        await using var context = db.Create();

        var order = Present.NotNull((await new EfRepository<TaggedOrder, CartId, TaggedOrderDbContext>(context).FindAsync(orderId, TestContext.Current.CancellationToken)));
        Assert.Equal(labelId, Assert.Single(order.TagLinks).LabelId);
        Assert.Empty(context.ChangeTracker.Entries<Label>());

        // The link references the other root without tracking it: its own repository loads it whole.
        var label = await new EfRepository<Label, LabelId, TaggedOrderDbContext>(context).FindAsync(labelId, TestContext.Current.CancellationToken);
        Assert.Equal("urgent", Present.NotNull(label).Name);
    }

    [Fact]
    public async Task DeletingARootStillLinkedFromAnotherAggregate_Fails_AndNothingChanges()
    {
        await using var db = await CreateDatabaseAsync<TaggedOrderDbContext>(o => new TaggedOrderDbContext(o));
        var (orderId, labelId, _) = await SeedTaggedOrderAsync(db);

        await using (var context = db.Create())
        {
            var labels = new EfRepository<Label, LabelId, TaggedOrderDbContext>(context);
            await labels.DeleteAsync(Present.NotNull((await labels.FindAsync(labelId, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<PersistenceException>(() => new EfUnitOfWork<TaggedOrderDbContext>(context).CommitAsync(TestContext.Current.CancellationToken).AsTask());
        }

        await using var verify = db.Create();
        Assert.Equal(1, (await verify.Set<Label>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(1, (await verify.Set<TagLink>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(1, (await verify.Set<TaggedOrder>().Where(o => o.Id == orderId).Select(o => EF.Property<long>(o, AggregateVersion.PropertyName)).SingleAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task DeletingARootStillReferencedByAnotherRoot_Fails_AndNothingChanges()
    {
        await using var db = await CreateDatabaseAsync<TaggedOrderDbContext>(o => new TaggedOrderDbContext(o));
        var (orderId, _, clientId) = await SeedTaggedOrderAsync(db);

        await using (var context = db.Create())
        {
            var clients = new EfRepository<Client, ClientId, TaggedOrderDbContext>(context);
            await clients.DeleteAsync(Present.NotNull((await clients.FindAsync(clientId, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<PersistenceException>(() => new EfUnitOfWork<TaggedOrderDbContext>(context).CommitAsync(TestContext.Current.CancellationToken).AsTask());
        }

        await using var verify = db.Create();
        Assert.Equal(1, (await verify.Set<Client>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(1, (await verify.Set<TaggedOrder>().CountAsync(o => o.Id == orderId, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(1, (await verify.Set<TaggedOrderLine>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(ReferenceMisuse.ExplicitCascade, "'TagLink.LabelId'", "Cascade")]
    [InlineData(ReferenceMisuse.ExplicitSetNull, "'TaggedOrder.ReferrerId'", "SetNull")]
    public void AnExplicitDatabaseCascadeAcrossAggregates_FailsModelBuilding_NamingTheKey(
        ReferenceMisuse misuse,
        string key,
        string behavior)
    {
        using var context = new TaggedOrderDbContext(Options<TaggedOrderDbContext>("unused"), misuse);

        var thrown = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains(key, thrown.Message);
        Assert.Contains(behavior, thrown.Message);
    }

    [Fact]
    public async Task AChildInsideTheBoundary_StillCascadesWithItsRoot()
    {
        await using var db = await CreateDatabaseAsync<TaggedOrderDbContext>(o => new TaggedOrderDbContext(o));
        var (orderId, _, _) = await SeedTaggedOrderAsync(db);

        await using (var context = db.Create())
        {
            Assert.Equal(DeleteBehavior.Cascade, context.Model.FindEntityType(typeof(TaggedOrderLine))!.GetForeignKeys().Single().DeleteBehavior);
            // The root alone, its lines and links not tracked: only the database can cascade.
            context.Remove(await context.Set<TaggedOrder>().SingleAsync(o => o.Id == orderId, cancellationToken: TestContext.Current.CancellationToken));
            await new EfUnitOfWork<TaggedOrderDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = db.Create();
        Assert.Equal(0, (await verify.Set<TaggedOrderLine>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(0, (await verify.Set<TagLink>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(1, (await verify.Set<Label>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void ABidirectionalManyToManyBetweenTwoRoots_NeitherSideMarked_FailsModelBuilding()
    {
        using var context = new BidirectionalTagsDbContext(Options<BidirectionalTagsDbContext>("unused"), markOrderTags: false);

        var thrown = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("many-to-many navigation", thrown.Message);
        Assert.Contains("an explicit link entity, a child of the root", thrown.Message);
        Assert.True((thrown.Message.Contains("'BiOrder.Tags'", StringComparison.Ordinal)
            || thrown.Message.Contains("'BiTag.Orders'", StringComparison.Ordinal)), thrown.Message);
    }

    [Fact]
    public async Task ABidirectionalManyToManyMarkedOnOneSide_IsOutsideBothAggregates_NeitherLoadedNorVersioned()
    {
        // Only Order.Tags is marked: the relationship is outside, Tag.Orders included.
        await using var db = await CreateDatabaseAsync<BidirectionalTagsDbContext>(o => new BidirectionalTagsDbContext(o, markOrderTags: true));
        var order = new BiOrder { Id = new CartId(Guid.NewGuid()) };
        var tag = new BiTag { Id = new LabelId(Guid.NewGuid()) };
        order.Tags.Add(tag);
        await using (var context = db.Create())
        {
            await new EfRepository<BiOrder, CartId, BidirectionalTagsDbContext>(context).AddAsync(order, TestContext.Current.CancellationToken);
            await new EfRepository<BiTag, LabelId, BidirectionalTagsDbContext>(context).AddAsync(tag, TestContext.Current.CancellationToken);
            await new EfUnitOfWork<BidirectionalTagsDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
        }

        var other = new BiOrder { Id = new CartId(Guid.NewGuid()) };
        await using (var context = db.Create())
        {
            var loadedTag = Present.NotNull((await new EfRepository<BiTag, LabelId, BidirectionalTagsDbContext>(context).FindAsync(tag.Id, TestContext.Current.CancellationToken)));
            Assert.Empty(loadedTag.Orders);
            var loadedOrder = Present.NotNull((await new EfRepository<BiOrder, CartId, BidirectionalTagsDbContext>(context).FindAsync(order.Id, TestContext.Current.CancellationToken)));
            Assert.Empty(loadedOrder.Tags);

            await new EfRepository<BiOrder, CartId, BidirectionalTagsDbContext>(context).AddAsync(other, TestContext.Current.CancellationToken);
            loadedTag.Orders.Add(other); // a new join row, from the tag's side
            await new EfUnitOfWork<BidirectionalTagsDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = db.Create();
        Assert.Equal(1, (await verify.Set<BiTag>().Where(t => t.Id == tag.Id).Select(t => EF.Property<long>(t, AggregateVersion.PropertyName)).SingleAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(1, (await verify.Set<BiOrder>().Where(o => o.Id == order.Id).Select(o => EF.Property<long>(o, AggregateVersion.PropertyName)).SingleAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(2, (await verify.Set<BiTag>().Where(t => t.Id == tag.Id).SelectMany(t => t.Orders).CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    // ---- Entities outside every boundary -----------------------------------------------------

    [Fact]
    public async Task AnAggregateChangeAndAnUnrelatedEntityInOneSave_BumpTheRootOnce()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var cart = Present.NotNull((await Carts(context).FindAsync(id, TestContext.Current.CancellationToken)));
            cart.Owner = "changed";
            context.Add(new AuditRow { Text = "cart changed" });
            await Commit(context);
        }

        Assert.Equal(2, (await StoredVersionAsync(db, id)));
        await using var verify = db.Create();
        Assert.Equal(1, (await verify.Set<AuditRow>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ASaveOfAnUnrelatedEntityOnly_BumpsNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            _ = Present.NotNull((await Carts(context).FindAsync(id, TestContext.Current.CancellationToken)));
            context.Add(new AuditRow { Text = "read only" });
            await Commit(context);
        }

        Assert.Equal(1, (await StoredVersionAsync(db, id)));
    }

    // ---- Conflicts ---------------------------------------------------------------------------

    [Fact]
    public async Task TwoCommandsChangingDifferentChildren_Conflict_AndTheSecondWritesNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        await using var first = db.Create();
        await using var second = db.Create();
        var mine = Present.NotNull((await Carts(first).FindAsync(id, TestContext.Current.CancellationToken)));
        var theirs = Present.NotNull((await Carts(second).FindAsync(id, TestContext.Current.CancellationToken)));

        mine.Lines.Single(l => l.Sku == "BOOK").Sku = "BOOK-first";
        theirs.Lines.Single(l => l.Sku == "PEN").Sku = "PEN-second";
        await Commit(first);
        var thrown = await Assert.ThrowsAsync<PersistenceException>(() => Commit(second));

        Assert.IsType<DbUpdateConcurrencyException>(thrown.InnerException);
        Assert.Equal(2, (await StoredVersionAsync(db, id)));
        await using var verify = db.Create();
        Assert.False((await verify.Set<CartLine>().AnyAsync(l => l.Sku == "PEN-second", cancellationToken: TestContext.Current.CancellationToken)), "nothing of the second save is written");
        Assert.True((await verify.Set<CartLine>().AnyAsync(l => l.Sku == "BOOK-first", cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ARetriedAttempt_RecomputesTheVersionFromTheReloadedState()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        var failOnce = new FailingInsertInterceptor(failures: 1);
        await using var context = db.Create(o => o.AddInterceptors(failOnce), maxRetryCount: 3);
        var unitOfWork = new EfUnitOfWork<CartDbContext>(context);
        var attempt = new RetryState(Carts(context), unitOfWork, id);

        await unitOfWork.ExecuteAsync(static async (s, ct) =>
            {
                s.Attempts++;
                var cart = Present.NotNull((await s.Carts.FindAsync(s.Id, ct)));
                cart.Lines.Add(new CartLine { Sku = $"attempt-{s.Attempts}" });
                await s.UnitOfWork.CommitAsync(ct);
            }, attempt, TestContext.Current.CancellationToken);

        Assert.Equal(1, failOnce.Thrown);
        Assert.Equal(2, attempt.Attempts);
        Assert.Equal(2, (await StoredVersionAsync(db, id)));
    }

    // ---- Across requests: the expected version --------------------------------------------------

    [Fact]
    public async Task ACommandStatingAStaleVersion_Conflicts_AndWritesNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        long etag;
        await using (var read = db.Create())
        {
            var carts = Carts(read);
            etag = carts.GetVersion(Present.NotNull((await carts.FindAsync(id, TestContext.Current.CancellationToken))));
        }

        await using (var concurrent = db.Create())
        {
            Present.NotNull((await Carts(concurrent).FindAsync(id, TestContext.Current.CancellationToken))).Owner = "concurrent";
            await Commit(concurrent);
        }

        await using (var command = db.Create())
        {
            var cart = Present.NotNull((await Carts(command).FindAsync(id, expectedVersion: etag, ct: TestContext.Current.CancellationToken)));
            cart.Lines[0].Sku = "stale";
            var thrown = await Assert.ThrowsAsync<PersistenceException>(() => Commit(command));
            Assert.IsType<DbUpdateConcurrencyException>(thrown.InnerException);
        }

        Assert.Equal(etag + 1, (await StoredVersionAsync(db, id)));
        await using var verify = db.Create();
        Assert.False((await verify.Set<CartLine>().AnyAsync(l => l.Sku == "stale", cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ACommandStatingTheCurrentVersion_Succeeds()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var command = db.Create())
        {
            var cart = Present.NotNull((await Carts(command).FindAsync(id, expectedVersion: 1, ct: TestContext.Current.CancellationToken)));
            cart.Lines[0].Sku = "fresh";
            await Commit(command);
        }

        Assert.Equal(2, (await StoredVersionAsync(db, id)));
    }

    [Fact]
    public async Task AnExpectedVersionOnARootNotYetInserted_MustBeZero()
    {
        await using var db = await CreateCartDatabaseAsync();
        await using var context = db.Create();
        var carts = Carts(context);
        var cart = new Cart { Id = new CartId(Guid.NewGuid()), Owner = "new" };
        await carts.AddAsync(cart, TestContext.Current.CancellationToken);

        var thrown = await Assert.ThrowsAsync<PersistenceException>(async () => await carts.FindAsync(cart.Id, expectedVersion: 5, ct: TestContext.Current.CancellationToken));
        Assert.Contains("not inserted yet", Assert.IsType<AggregateVersionConflictException>(thrown.InnerException).Message);
        Assert.Same(cart, (await carts.FindAsync(cart.Id, expectedVersion: 0, ct: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ACommandStatingTheCurrentVersionButChangingNothing_WritesNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        var commands = new List<string>();

        await using (var command = db.Create(o => o.LogTo(commands.Add, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuting])))
        {
            _ = Present.NotNull((await Carts(command).FindAsync(id, expectedVersion: 1, ct: TestContext.Current.CancellationToken)));
            commands.Clear();
            await Commit(command);
        }

        Assert.Empty(commands);
        Assert.Equal(1, (await StoredVersionAsync(db, id)));
    }

    [Fact]
    public async Task ACommandStatingAStaleVersionButChangingNothing_StillConflicts_AndWritesNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var command = db.Create())
        {
            // The original value now differs from the loaded one: EF Core writes the root's token,
            // WHERE AggregateVersion = 0, which matches no row.
            _ = Present.NotNull((await Carts(command).FindAsync(id, expectedVersion: 0, ct: TestContext.Current.CancellationToken)));
            var thrown = await Assert.ThrowsAsync<PersistenceException>(() => Commit(command));
            Assert.IsType<DbUpdateConcurrencyException>(thrown.InnerException);
        }

        Assert.Equal(1, (await StoredVersionAsync(db, id)));
    }

    [Fact]
    public async Task TheVersion_IsReadableFromAProjection()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        await using var context = db.Create();

        var summary = await context.Carts.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Owner, Version = EF.Property<long>(c, AggregateVersion.PropertyName) })
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.Version);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static void Apply(CartDbContext context, Cart cart, CartChange change)
    {
        switch (change)
        {
            case CartChange.RootProperty: cart.Owner = "changed"; break;
            case CartChange.ChildProperty: cart.Lines[0].Sku = "changed"; break;
            case CartChange.GrandChildProperty: cart.Lines.Single(l => l.Options.Count > 0).Options[0].Name = "changed"; break;
            case CartChange.OwnedProperty: cart.Shipping!.City = "changed"; break;
            case CartChange.ChildAdded: cart.Lines.Add(new CartLine { Sku = "added" }); break;
            case CartChange.ChildDeleted: cart.Lines.Remove(cart.Lines.Single(l => l.Options.Count == 0)); break;
            case CartChange.LinkAdded:
                var coupon = new Coupon { Code = "NEW" }; // another aggregate
                context.Add(coupon);
                cart.CouponLinks.Add(new CartCouponLink { CartId = cart.Id, CouponId = coupon.Id });
                break;
            case CartChange.LinkRemoved: cart.CouponLinks.Remove(cart.CouponLinks.Single()); break;
            case CartChange.PrincipalSideChildProperty: cart.PrimaryContact!.Name = "changed"; break;
            case CartChange.PrincipalSideChildReplaced:
                var old = cart.PrimaryContact!;
                cart.PrimaryContact = new CartContact { Name = "replacement" };
                context.Remove(old);
                break;
        }
    }

    private static EfRepository<Cart, CartId, CartDbContext> Carts(CartDbContext context) => new(context);

    private static Task Commit(DbContext context) =>
        context switch
        {
            RenamedVersionDbContext renamed => new EfUnitOfWork<RenamedVersionDbContext>(renamed).CommitAsync().AsTask(),
            CartDbContext carts => new EfUnitOfWork<CartDbContext>(carts).CommitAsync().AsTask(),
            _ => throw new NotSupportedException(),
        };

    private static async Task<long> StoredVersionAsync(Database<CartDbContext> db, CartId id)
    {
        await using var context = db.Create();
        return await context.Carts.AsNoTracking().Where(c => c.Id == id)
            .Select(c => EF.Property<long>(c, AggregateVersion.PropertyName)).SingleAsync();
    }

    private static async Task<(CartId Order, LabelId Label, ClientId Client)> SeedTaggedOrderAsync(Database<TaggedOrderDbContext> db)
    {
        var client = new Client { Id = new ClientId(Guid.NewGuid()), Name = "ACME" };
        var label = new Label { Id = new LabelId(Guid.NewGuid()), Name = "urgent" };
        var order = new TaggedOrder { Id = new CartId(Guid.NewGuid()), ClientId = client.Id };
        order.Lines.Add(new TaggedOrderLine { Sku = "BOOK" });
        order.TagLinks.Add(new TagLink { OrderId = order.Id, LabelId = label.Id });

        await using var context = db.Create();
        context.AddRange(client, label);
        await new EfRepository<TaggedOrder, CartId, TaggedOrderDbContext>(context).AddAsync(order);
        await new EfUnitOfWork<TaggedOrderDbContext>(context).CommitAsync();
        return (order.Id, label.Id, client.Id);
    }

    private static async Task<CartId> SeedCartAsync(Database<CartDbContext> db)
    {
        var book = new CartLine { Sku = "BOOK" };
        book.Options.Add(new LineOption { Name = "gift wrap" });
        var cart = new Cart
        {
            Id = new CartId(Guid.NewGuid()),
            Owner = "Ada",
            Shipping = new CartAddress { City = "Lyon" },
            PrimaryContact = new CartContact { Name = "Grace" },
        };
        cart.Lines.Add(book);
        cart.Lines.Add(new CartLine { Sku = "PEN" });
        var welcome = new Coupon { Code = "WELCOME" }; // another aggregate, linked through a child link entity
        cart.CouponLinks.Add(new CartCouponLink { CartId = cart.Id, CouponId = welcome.Id });

        await using var context = db.Create();
        context.Add(welcome);
        await Carts(context).AddAsync(cart);
        await Commit(context);
        return cart.Id;
    }

    private DbContextOptions<TContext> Options<TContext>(
        string database,
        Action<DbContextOptionsBuilder>? configure = null,
        int? maxRetryCount = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        UseProvider(builder, database, maxRetryCount);
        configure?.Invoke(builder);
        builder.UseMicroKitPersistence();
        return builder.Options;
    }

    private Task<Database<CartDbContext>> CreateCartDatabaseAsync() =>
        CreateDatabaseAsync<CartDbContext>(o => new CartDbContext(o));

    private async Task<Database<TContext>> CreateDatabaseAsync<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        var db = new Database<TContext>(this, "versions_" + Guid.NewGuid().ToString("N"), factory);
        await using var context = db.Create();
        await context.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>One database of the provider, dropped on dispose.</summary>
    private sealed class Database<TContext>(
        AggregateVersionScenarios owner,
        string name,
        Func<DbContextOptions<TContext>, TContext> factory) : IAsyncDisposable
        where TContext : DbContext
    {
        public TContext Create(Action<DbContextOptionsBuilder>? configure = null, int? maxRetryCount = null) =>
            factory(owner.Options<TContext>(name, configure, maxRetryCount));

        public async ValueTask DisposeAsync()
        {
            await using var context = Create();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class RetryState(EfRepository<Cart, CartId, CartDbContext> carts, EfUnitOfWork<CartDbContext> unitOfWork, CartId id)
    {
        public EfRepository<Cart, CartId, CartDbContext> Carts { get; } = carts;

        public EfUnitOfWork<CartDbContext> UnitOfWork { get; } = unitOfWork;

        public CartId Id { get; } = id;

        public int Attempts { get; set; }
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class AggregateVersionPostgreSqlTests(PostgreSqlSuiteFixture fixture) : AggregateVersionScenarios
{
    protected override void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null) =>
        builder.UseNpgsql(
            fixture.GetConnectionString(database),
            npgsql =>
            {
                if (maxRetryCount is { } retries)
                    npgsql.ExecutionStrategy(d => new MarkerRetryingExecutionStrategy(d, retries));
            });
}

public readonly record struct CartId(Guid Value) : IEntityId;

/// <summary>
/// An aggregate with a child collection and a grand-child, an owned type in the root's table, a
/// many-to-many collection, and a contact the root references (the root holds the foreign key).
/// </summary>
public sealed class Cart : IAggregateRoot
{
    public CartId Id { get; init; }

    public string Owner { get; set; } = string.Empty;

    public List<CartLine> Lines { get; } = [];

    public CartAddress? Shipping { get; set; }

    /// <summary>The links to coupons — another aggregate — as child entities of the cart.</summary>
    public List<CartCouponLink> CouponLinks { get; } = [];

    public Guid? PrimaryContactId { get; set; }

    public CartContact? PrimaryContact { get; set; }
}

public sealed class CartLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;

    public List<LineOption> Options { get; } = [];
}

public sealed class LineOption
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

public sealed class CartAddress
{
    public string City { get; set; } = string.Empty;
}

/// <summary>Another aggregate root, referenced by the cart through its link entities.</summary>
public sealed class Coupon : IAggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Code { get; set; } = string.Empty;
}

/// <summary>A link entity: a child of the cart, holding the reference to a coupon.</summary>
public sealed class CartCouponLink
{
    public CartId CartId { get; init; }

    public Guid CouponId { get; init; }
}

public sealed class CartContact
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>Outside every aggregate: an outbox-like row written in the same save.</summary>
public sealed class AuditRow
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

public class CartDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Cart> Carts => Set<Cart>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cart>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, v => new CartId(v)).ValueGeneratedNever();
            b.HasMany(c => c.Lines).WithOne().HasForeignKey("CartId").OnDelete(DeleteBehavior.Cascade);
            b.OwnsOne(c => c.Shipping);
            b.HasMany(c => c.CouponLinks).WithOne().HasForeignKey(l => l.CartId);
            b.HasOne(c => c.PrimaryContact).WithOne().HasForeignKey<Cart>(c => c.PrimaryContactId); // one-to-one
        });
        modelBuilder.Entity<CartLine>(b =>
        {
            b.Property(l => l.Id).ValueGeneratedNever();
            b.HasMany(l => l.Options).WithOne().HasForeignKey("LineId");
        });
        modelBuilder.Entity<LineOption>().Property(o => o.Id).ValueGeneratedNever();
        modelBuilder.Entity<Coupon>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<CartCouponLink>(l =>
        {
            l.HasKey(x => new { x.CartId, x.CouponId });
            l.Property(x => x.CartId).HasConversion(id => id.Value, v => new CartId(v));
            l.HasOne<Coupon>().WithMany().HasForeignKey(x => x.CouponId);
        });
        modelBuilder.Entity<CartContact>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<AuditRow>().Property(a => a.Id).ValueGeneratedNever();
    }
}

/// <summary>The cart model, with the version's column renamed by the application.</summary>
public sealed class RenamedVersionDbContext(DbContextOptions<RenamedVersionDbContext> options) : CartDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Cart>().Property<long>(AggregateVersion.PropertyName).HasColumnName("row_version");
    }
}

/// <summary>The cart model under a consumer naming convention (a stand-in for EFCore.NamingConventions).</summary>
public sealed class SnakeCaseCartDbContext(DbContextOptions<SnakeCaseCartDbContext> options) : CartDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Conventions.Add(_ => new SnakeCaseColumnConvention());

    private sealed class SnakeCaseColumnConvention : IPropertyAddedConvention
    {
        public void ProcessPropertyAdded(
            IConventionPropertyBuilder propertyBuilder,
            IConventionContext<IConventionPropertyBuilder> context)
        {
            // Owned types are left alone: unlike the real package, this stand-in does not keep a
            // table-split owned key on its owner's column.
            if (propertyBuilder.Metadata.DeclaringType is IConventionEntityType { } type && type.IsOwned())
                return;
            propertyBuilder.HasColumnName(string.Concat(propertyBuilder.Metadata.Name.Select(
                (ch, i) => i > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString())));
        }
    }
}

public enum VersionMisuse
{
    ClrMember,
    NotLong,
    NotAToken,
    SameColumn,
    OtherTable,
    SameColumnUnderTpc,
    OtherTpcTable,
    SiblingRoots,
    DefaultValue,
    DefaultValueSql,
    ComputedSql,
    ValueGenerated,
    Converted,
    ProviderType,
}

/// <summary>A TPC root whose abstract base has no table: the version's columns live in each concrete type's table.</summary>
public abstract class TpcVersionRoot : IAggregateRoot
{
    public Guid Id { get; init; }
}

public sealed class TpcVersionLeaf : TpcVersionRoot
{
    public long Legacy { get; set; }
}

/// <summary>A mapped TPH base that is no aggregate root, under two roots: each has its own version, in one column.</summary>
public abstract class VersionDocument
{
    public Guid Id { get; init; }
}

public sealed class VersionInvoice : VersionDocument, IAggregateRoot;

public sealed class VersionQuote : VersionDocument, IAggregateRoot;

/// <summary>Not an aggregate: a TPC hierarchy whose concrete row carries its own AggregateVersion column.</summary>
public abstract class TpcOutboxBase
{
    public Guid Id { get; init; }
}

public sealed class TpcOutboxRow : TpcOutboxBase
{
    public long AggregateVersion { get; set; }
}

public sealed class ClrVersionRoot : IAggregateRoot
{
    public Guid Id { get; init; }

    public long AggregateVersion { get; set; }
}

public sealed class PlainVersionRoot : IAggregateRoot
{
    public Guid Id { get; init; }

    public long Other { get; set; }
}

public sealed class MisusedVersionDbContext(DbContextOptions<MisusedVersionDbContext> options, VersionMisuse misuse)
    : DbContext(options), IModelVariant
{
    public VersionMisuse Misuse { get; } = misuse;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        switch (Misuse)
        {
            case VersionMisuse.ClrMember:
                modelBuilder.Entity<ClrVersionRoot>();
                break;
            case VersionMisuse.NotLong:
                modelBuilder.Entity<PlainVersionRoot>().Property<int>(AggregateVersion.PropertyName);
                break;
            case VersionMisuse.NotAToken:
                modelBuilder.Entity<PlainVersionRoot>().Property<long>(AggregateVersion.PropertyName).IsConcurrencyToken(false);
                break;
            case VersionMisuse.SameColumn:
                modelBuilder.Entity<PlainVersionRoot>().Property(r => r.Other).HasColumnName(AggregateVersion.PropertyName);
                break;
            case VersionMisuse.OtherTable:
                modelBuilder.Entity<PlainVersionRoot>();
                modelBuilder.Entity<VersionedOutboxRow>(); // not a root: its own AggregateVersion, in its own table
                break;
            case VersionMisuse.SameColumnUnderTpc:
                modelBuilder.Entity<TpcVersionRoot>().UseTpcMappingStrategy();
                modelBuilder.Entity<TpcVersionLeaf>().Property(l => l.Legacy).HasColumnName(AggregateVersion.PropertyName);
                break;
            case VersionMisuse.OtherTpcTable:
                modelBuilder.Entity<PlainVersionRoot>();
                modelBuilder.Entity<TpcOutboxBase>().UseTpcMappingStrategy();
                modelBuilder.Entity<TpcOutboxRow>();
                break;
            case VersionMisuse.SiblingRoots:
                modelBuilder.Entity<VersionDocument>();
                modelBuilder.Entity<VersionInvoice>();
                modelBuilder.Entity<VersionQuote>();
                break;
            case VersionMisuse.DefaultValue:
                modelBuilder.Entity<PlainVersionRoot>().Property<long>(AggregateVersion.PropertyName).HasDefaultValue(1L);
                break;
            case VersionMisuse.DefaultValueSql:
                modelBuilder.Entity<PlainVersionRoot>().Property<long>(AggregateVersion.PropertyName).HasDefaultValueSql("1");
                break;
            case VersionMisuse.ComputedSql:
                modelBuilder.Entity<PlainVersionRoot>().Property<long>(AggregateVersion.PropertyName).HasComputedColumnSql("1");
                break;
            case VersionMisuse.ValueGenerated:
                modelBuilder.Entity<PlainVersionRoot>().Property<long>(AggregateVersion.PropertyName).ValueGeneratedOnAddOrUpdate();
                break;
            case VersionMisuse.Converted:
                modelBuilder.Entity<PlainVersionRoot>().Property<long>(AggregateVersion.PropertyName)
                    .HasConversion(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture), v => long.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case VersionMisuse.ProviderType:
                modelBuilder.Entity<PlainVersionRoot>().Property<long>(AggregateVersion.PropertyName).HasConversion<string>();
                break;
        }
    }

    object IModelVariant.Variant => Misuse;

    // One model per variant: the shared cache key factory includes it.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => ModelVariantCacheKeyFactory.Use(optionsBuilder);
}

/// <summary>Not an aggregate: a row that happens to carry its own AggregateVersion column.</summary>
public sealed class VersionedOutboxRow
{
    public Guid Id { get; init; }

    public long AggregateVersion { get; set; }
}

/// <summary>An aggregate whose many-to-many link goes through an explicit join entity with a third foreign key.</summary>
public sealed class Basket : IAggregateRoot
{
    public CartId Id { get; init; }

    public List<BasketTag> Labels { get; } = [];
}

public sealed class BasketTag
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

public sealed class Clerk
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class BasketTagLink
{
    public CartId BasketId { get; set; }

    public Guid TagId { get; set; }

    public Guid? AddedById { get; set; }

    public Clerk? AddedBy { get; set; }
}

public sealed class LabelledBasketDbContext(DbContextOptions<LabelledBasketDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Basket>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasConversion(id => id.Value, v => new CartId(v)).ValueGeneratedNever();
            b.HasMany(x => x.Labels).WithMany().UsingEntity<BasketTagLink>(
                right => right.HasOne<BasketTag>().WithMany().HasForeignKey(l => l.TagId),
                left => left.HasOne<Basket>().WithMany().HasForeignKey(l => l.BasketId),
                join =>
                {
                    join.HasKey(l => new { l.BasketId, l.TagId });
                    join.Property(l => l.BasketId).HasConversion(id => id.Value, v => new CartId(v));
                    join.HasOne(l => l.AddedBy).WithMany().HasForeignKey(l => l.AddedById);
                });
        });
        modelBuilder.Entity<Basket>().Navigation(x => x.Labels).IsOutsideAggregate(); // many-to-many: reference data
        modelBuilder.Entity<BasketTag>().Property(t => t.Id).ValueGeneratedNever();
        modelBuilder.Entity<Clerk>().Property(c => c.Id).ValueGeneratedNever();
    }
}

/// <summary>Records the version the root holds when the application's own interceptor runs.</summary>
public sealed class VersionProbeInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public long? SeenVersion { get; private set; }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context!;
        var auto = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        SeenVersion = context.ChangeTracker.Entries<Cart>().Single().Property<long>(AggregateVersion.PropertyName).CurrentValue;
        context.ChangeTracker.AutoDetectChangesEnabled = auto;
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>An aggregate whose child has a byte[] key, the key of a grand-child's foreign key.</summary>
public sealed class Archive : IAggregateRoot
{
    public CartId Id { get; init; }

    public List<ArchivedDocument> Documents { get; } = [];
}

public sealed class ArchivedDocument
{
    public byte[] Code { get; init; } = [];

    public string Title { get; set; } = string.Empty;

    public List<ArchivedPage> Pages { get; } = [];
}

public sealed class ArchivedPage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

public sealed class ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Archive>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion(id => id.Value, v => new CartId(v)).ValueGeneratedNever();
            b.HasMany(a => a.Documents).WithOne().HasForeignKey("ArchiveId");
        });
        modelBuilder.Entity<ArchivedDocument>(b =>
        {
            b.HasKey(d => d.Code);
            b.Property(d => d.Code).HasMaxLength(16).ValueGeneratedNever();
            b.HasMany(d => d.Pages).WithOne().HasForeignKey("DocumentCode");
        });
        modelBuilder.Entity<ArchivedPage>().Property(p => p.Id).ValueGeneratedNever();
    }
}

public readonly record struct ClientId(Guid Value) : IEntityId;

public readonly record struct LabelId(Guid Value) : IEntityId;

/// <summary>Another aggregate root, which an order references by a required foreign key.</summary>
public sealed class Client : IAggregateRoot
{
    public ClientId Id { get; init; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>Another aggregate root, which an order links to through <see cref="TagLink"/>.</summary>
public sealed class Label : IAggregateRoot
{
    public LabelId Id { get; init; }

    public string Name { get; set; } = string.Empty;
}

public sealed class TaggedOrder : IAggregateRoot
{
    public CartId Id { get; init; }

    public ClientId ClientId { get; set; }

    public ClientId? ReferrerId { get; set; }

    public List<TaggedOrderLine> Lines { get; } = [];

    public List<TagLink> TagLinks { get; } = [];
}

public sealed class TaggedOrderLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;
}

/// <summary>The link entity: a child of the order (composite key), referencing another root.</summary>
public sealed class TagLink
{
    public CartId OrderId { get; init; }

    public LabelId LabelId { get; init; }
}

public enum ReferenceMisuse
{
    None,
    ExplicitCascade,
    ExplicitSetNull,
}

public sealed class TaggedOrderDbContext(DbContextOptions<TaggedOrderDbContext> options, ReferenceMisuse misuse = ReferenceMisuse.None)
    : DbContext(options), IModelVariant
{
    public ReferenceMisuse Misuse { get; } = misuse;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, v => new ClientId(v)).ValueGeneratedNever();
        });
        modelBuilder.Entity<Label>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).HasConversion(id => id.Value, v => new LabelId(v)).ValueGeneratedNever();
        });
        modelBuilder.Entity<TaggedOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion(id => id.Value, v => new CartId(v)).ValueGeneratedNever();
            b.Property(o => o.ClientId).HasConversion(id => id.Value, v => new ClientId(v));
            b.Property(o => o.ReferrerId).HasConversion(new ValueConverter<ClientId, Guid>(id => id.Value, v => new ClientId(v)));
            b.HasOne<Client>().WithMany().HasForeignKey(o => o.ClientId); // required: EF Core would cascade
            var referrer = b.HasOne<Client>().WithMany().HasForeignKey(o => o.ReferrerId);
            if (Misuse == ReferenceMisuse.ExplicitSetNull)
                referrer.OnDelete(DeleteBehavior.SetNull);
            b.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").IsRequired(); // inside: cascades
            b.HasMany(o => o.TagLinks).WithOne().HasForeignKey(l => l.OrderId);
        });
        modelBuilder.Entity<TaggedOrderLine>().Property(l => l.Id).ValueGeneratedNever();
        modelBuilder.Entity<TagLink>(b =>
        {
            b.HasKey(l => new { l.OrderId, l.LabelId });
            b.Property(l => l.OrderId).HasConversion(id => id.Value, v => new CartId(v));
            b.Property(l => l.LabelId).HasConversion(id => id.Value, v => new LabelId(v));
            var label = b.HasOne<Label>().WithMany().HasForeignKey(l => l.LabelId); // required: EF Core would cascade
            if (Misuse == ReferenceMisuse.ExplicitCascade)
                label.OnDelete(DeleteBehavior.Cascade);
        });
    }

    object IModelVariant.Variant => Misuse;

    // One model per variant: the shared cache key factory includes it.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => ModelVariantCacheKeyFactory.Use(optionsBuilder);
}

/// <summary>Two roots linked by a many-to-many declared on both sides, both visible.</summary>
public sealed class BiOrder : IAggregateRoot
{
    public CartId Id { get; init; }

    public List<BiTag> Tags { get; } = [];
}

public sealed class BiTag : IAggregateRoot
{
    public LabelId Id { get; init; }

    public List<BiOrder> Orders { get; } = [];
}

public sealed class BidirectionalTagsDbContext(DbContextOptions<BidirectionalTagsDbContext> options, bool markOrderTags = true)
    : DbContext(options), IModelVariant
{
    public bool MarkOrderTags { get; } = markOrderTags;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BiOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion(id => id.Value, v => new CartId(v)).ValueGeneratedNever();
            b.HasMany(o => o.Tags).WithMany(t => t.Orders);
            if (MarkOrderTags)
                b.Navigation(o => o.Tags).IsOutsideAggregate(); // Tag.Orders is not marked
        });
        modelBuilder.Entity<BiTag>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).HasConversion(id => id.Value, v => new LabelId(v)).ValueGeneratedNever();
        });
    }

    object IModelVariant.Variant => MarkOrderTags;

    // One model per variant: the shared cache key factory includes it.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => ModelVariantCacheKeyFactory.Use(optionsBuilder);
}

using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #197 phase 4: an aggregate whose root has <c>HasSoftDelete()</c> is never deleted: deleting the root
/// marks every entity of the aggregate, and the named filter hides them. Every scenario runs on PostgreSQL
/// and on SQL Server.
/// </summary>
public abstract class SoftDeleteScenarios
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 30, 0, TimeSpan.Zero);

    // One application service provider for every context: EF Core keys its internal providers on it.
    private static readonly IServiceProvider s_applicationServices =
        new ServiceCollection().AddSingleton<TimeProvider>(new FixedTimeProvider(Now)).BuildServiceProvider();

    /// <summary>Configures the provider on <paramref name="database"/>.</summary>
    protected abstract void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null);

    /// <summary>The provider's classifier, for the Result path.</summary>
    protected abstract IDatabaseFailureClassifier Classifier { get; }

    // ---- What a soft delete writes ------------------------------------------------------------------

    [Fact]
    public async Task ASoftDeletedRoot_MarksEveryEntityOfItsAggregate_AndDeletesNoRow()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            await orders.DeleteAsync(Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        var order = await Unfiltered(verify.Set<SdOrder>()).Include(o => o.Notes).SingleAsync(o => o.Id == id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Now, Mark(verify, order));
        Assert.Equal(TimeSpan.Zero, Mark(verify, order)!.Value.Offset);
        Assert.Equal(2, verify.Entry(order).Property<long>(AggregateVersion.PropertyName).CurrentValue);
        Assert.Equal("Lyon", Present.NotNull(order.Address).City);
        Assert.Equal("fragile", Assert.Single(order.Notes).Text);

        await AllMarkedAsync<SdLine>(verify, expected: 3);
        await AllMarkedAsync<SdOption>(verify, expected: 1);
        await AllMarkedAsync<SdContact>(verify, expected: 1);
        await AllMarkedAsync<SdMemo>(verify, expected: 1);
        var remark = await Unfiltered(verify.Set<SdRemark>()).SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Now, Mark(verify, remark));
        Assert.Equal(id, remark.OrderId);
    }

    [Fact]
    public async Task AChildRemovedFromALiveRoot_IsDeletedForReal()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var order = Present.NotNull((await Orders(context).FindAsync(id, TestContext.Current.CancellationToken)));
            order.Lines.Remove(order.Lines.First(l => l.Sku == "PEN"));
            await Commit(context);
        }

        await using var verify = db.Create();
        Assert.Equal(2, (await Unfiltered(verify.Set<SdLine>()).CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(2, (await verify.Set<SdLine>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AChildRemovedInTheSaveThatSoftDeletesItsRoot_IsMarked_NotDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var order = Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken)));
            order.Lines.Remove(order.Lines.First(l => l.Sku == "PEN"));
            await orders.DeleteAsync(order, TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        await AllMarkedAsync<SdLine>(verify, expected: 3);
    }

    [Fact]
    public async Task TwoAggregatesInOneSave_OneSoftDeleted_TheOtherLosingAChildForReal()
    {
        await using var db = await CreateDatabaseAsync();
        var orderId = await SeedOrderAsync(db, "SO-1");
        var basketId = await SeedBasketAsync(db);
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            await orders.DeleteAsync(Present.NotNull((await orders.FindAsync(orderId, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
            var basket = Present.NotNull((await Baskets(context).FindAsync(basketId, TestContext.Current.CancellationToken)));
            basket.Items.RemoveAt(0);
            await Commit(context);
        }

        await using var verify = db.Create();
        await AllMarkedAsync<SdLine>(verify, expected: 3);
        Assert.Equal(1, (await verify.Set<SdBasketItem>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AnAggregateWithoutSoftDelete_IsDeletedForReal()
    {
        await using var db = await CreateDatabaseAsync();
        var basketId = await SeedBasketAsync(db);
        await using (var context = db.Create())
        {
            var baskets = Baskets(context);
            await baskets.DeleteAsync(Present.NotNull((await baskets.FindAsync(basketId, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        Assert.Equal(0, (await verify.Set<SdBasket>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(0, (await verify.Set<SdBasketItem>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    // ---- Delete behaviours and timings --------------------------------------------------------------

    public enum Timing
    {
        CascadeOnSaveChanges,
        OrphansOnSaveChanges,
    }

    [Theory]
    [InlineData(Timing.CascadeOnSaveChanges)]
    [InlineData(Timing.OrphansOnSaveChanges)]
    public async Task UnderOnSaveChangesTimings_EveryEntityEndsMarked_AndNoRowIsDeleted(Timing timing)
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            if (timing == Timing.CascadeOnSaveChanges)
                context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
            else
                context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;

            var orders = Orders(context);
            var order = Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken)));
            if (timing == Timing.OrphansOnSaveChanges)
                order.Lines.Remove(order.Lines.First(l => l.Sku == "PEN")); // a conceptual null on a required key
            await orders.DeleteAsync(order, TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        Assert.Equal(1, (await Unfiltered(verify.Set<SdOrder>()).CountAsync(o => o.Id == id, cancellationToken: TestContext.Current.CancellationToken)));
        await AllMarkedAsync<SdLine>(verify, expected: 3);
        await AllMarkedAsync<SdOption>(verify, expected: 1);
        await AllMarkedAsync<SdMemo>(verify, expected: 1);
        await AllMarkedAsync<SdRemark>(verify, expected: 1);
    }

    [Fact]
    public async Task ARequiredClientNoActionChild_IsMarked_AndNotDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            Present.NotNull((await Orders(context).FindAsync(id, TestContext.Current.CancellationToken))).Stamps.Add(new SdStamp { Text = "paid" });
            await Commit(context);
        }

        await SoftDeleteAsync(db, id);

        await using var verify = db.Create();
        await AllMarkedAsync<SdStamp>(verify, expected: 1);
        Assert.Equal(id, (await Unfiltered(verify.Set<SdStamp>()).SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).OrderId);
    }

    [Fact]
    public async Task AChildMovedToALiveRoot_FollowsIt_AndIsNotMarked()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var liveId = await SeedOrderAsync(db, "SO-2");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var deleted = Present.NotNull((await orders.FindAsync(deletedId, TestContext.Current.CancellationToken)));
            var live = Present.NotNull((await orders.FindAsync(liveId, TestContext.Current.CancellationToken)));
            var moved = deleted.Lines.First(l => l.Sku == "PEN");
            deleted.Lines.Remove(moved);
            live.Lines.Add(moved);
            await orders.DeleteAsync(deleted, TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        var survivor = await Orders(verify).FindAsync(liveId, TestContext.Current.CancellationToken);
        Assert.Equal(4, Present.NotNull(survivor).Lines.Count);
        Assert.Equal(6, (await Unfiltered(verify.Set<SdLine>()).CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AChildAddedInTheSaveThatSoftDeletesItsRoot_IsInsertedMarked()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges; // Immediate detaches an Added child
            var orders = Orders(context);
            var order = Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken)));
            order.Lines.Add(new SdLine { Sku = "LATE" });
            await orders.DeleteAsync(order, TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        await AllMarkedAsync<SdLine>(verify, expected: 4);
    }

    // ---- Reads --------------------------------------------------------------------------------------

    [Fact]
    public async Task ADirectQueryOnAChildSet_ReturnsNoChildOfASoftDeletedRoot()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        await SeedOrderAsync(db, "SO-2");
        await SoftDeleteAsync(db, deletedId);

        await using var verify = db.Create();
        Assert.Equal(3, (await verify.Set<SdLine>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(1, (await verify.Set<SdOption>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task TheReadSide_ReturnsSoftDeletedAggregatesOnlyWithDeleted_AndKeepsOtherNamedFilters()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        await SeedOrderAsync(db, "SO-2");
        var hiddenId = await SeedOrderAsync(db, "SO-3");
        await SoftDeleteAsync(db, deletedId);
        await SoftDeleteAsync(db, hiddenId);
        await using (var setup = db.Create())
        {
            // Another tenant's soft-deleted order: the write side cannot reach it, so move it there afterwards.
            await setup.Set<SdOrder>().IgnoreQueryFilters([SoftDelete.FilterName])
                .Where(o => o.Id == hiddenId)
                .ExecuteUpdateAsync(u => u.SetProperty(o => o.Tenant, SoftDeleteDbContext.HiddenTenant), cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var context = db.Create();
        var reads = new EfReadRepository<SdOrder, SoftDeleteDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        var live = await reads.ListAsync(new QueryOptions<SdOrder>(), TestContext.Current.CancellationToken);
        var all = await reads.ListAsync(new QueryOptions<SdOrder>().WithDeleted(), TestContext.Current.CancellationToken);

        Assert.Equal(["SO-2"], live.Select(o => o.Number));
        Assert.Equal(["SO-1", "SO-2"], all.Select(o => o.Number).OrderBy(n => n, StringComparer.Ordinal).ToList()); // the tenant filter still hides SO-3
        Assert.Equal(1, (await reads.CountAsync(new QueryOptions<SdOrder>(), TestContext.Current.CancellationToken)));
        Assert.Equal(2, (await reads.CountAsync(new QueryOptions<SdOrder>().WithDeleted(), TestContext.Current.CancellationToken)));
        Assert.False((await reads.AnyAsync(new QueryOptions<SdOrder>(new NumberIs("SO-1")), TestContext.Current.CancellationToken)));
        Assert.True((await reads.AnyAsync(new QueryOptions<SdOrder>(new NumberIs("SO-1")).WithDeleted(), TestContext.Current.CancellationToken)));
        Assert.Equal(2, (await reads.ListPagedAsync(new QueryOptions<SdOrder>().WithDeleted().WithOrdering(q => q.OrderBy(o => o.Number)), TestContext.Current.CancellationToken)).TotalCount);
        Assert.Null((await reads.FindAsync([deletedId], TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task FindAsync_AfterTheCommit_ReturnsNull_InTheSameScopeAndInANewOne()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            await orders.DeleteAsync(Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
            await Commit(context);

            Assert.Empty(context.ChangeTracker.Entries());
            Assert.Null((await orders.FindAsync(id, TestContext.Current.CancellationToken)));
        }

        await using var fresh = db.Create();
        Assert.Null((await Orders(fresh).FindAsync(id, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AFilteredUniqueIndex_AdmitsASoftDeletedDuplicate_AndRejectsTwoLiveOnes()
    {
        await using var db = await CreateDatabaseAsync();
        var first = await SeedOrderAsync(db, "SO-1");
        await SoftDeleteAsync(db, first);

        await SeedOrderAsync(db, "SO-1"); // the soft-deleted one does not hold the number

        await Assert.ThrowsAsync<PersistenceException>(() => SeedOrderAsync(db, "SO-1"));
    }

    // ---- Refusals and versions ---------------------------------------------------------------------

    [Fact]
    public async Task SoftDeletingARootThatWasNotLoadedWhole_FailsTheSave()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using var context = db.Create();
        var stub = new SdOrder { Id = id, Number = "SO-1", Tenant = "main" };
        context.Attach(stub);
        context.Remove(stub);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("not loaded whole", thrown.Message);
        await using var verify = db.Create();
        Assert.Equal(1, (await verify.Set<SdOrder>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    // PR #200 review, #197 Spec phase 1: deleting an instance the repository did not load would reach the
    // database with an unknown version (a false conflict) — DeleteAsync refuses it, with or without soft delete.
    [Theory]
    [InlineData(true, "stub")]
    [InlineData(true, "query")]
    [InlineData(false, "stub")]
    [InlineData(false, "query")]
    public async Task DeleteAsync_OnARootTheRepositoryDidNotLoad_IsRefusedBeforeAnySql(bool softDeletable, string how)
    {
        await using var db = await CreateDatabaseAsync();
        var orderId = await SeedOrderAsync(db, "SO-1");
        var basketId = await SeedBasketAsync(db);
        var commands = new CommandCounter();
        await using var context = db.Create(o => o.AddInterceptors(commands));

        Func<Task> delete;
        if (softDeletable)
        {
            var order = how == "stub"
                ? new SdOrder { Id = orderId, Number = "SO-1", Tenant = "main" }
                : await context.Set<SdOrder>().SingleAsync(o => o.Id == orderId, cancellationToken: TestContext.Current.CancellationToken);
            delete = () => Orders(context).DeleteAsync(order).AsTask();
        }
        else
        {
            var basket = how == "stub"
                ? new SdBasket { Id = basketId }
                : await context.Set<SdBasket>().SingleAsync(b => b.Id == basketId, cancellationToken: TestContext.Current.CancellationToken);
            delete = () => Baskets(context).DeleteAsync(basket).AsTask();
        }

        var before = commands.Count;
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(delete);

        Assert.Contains(softDeletable ? "'SdOrder'" : "'SdBasket'", thrown.Message);
        Assert.Equal(before, commands.Count);
        Assert.DoesNotContain(context.ChangeTracker.Entries(), e => e.State == EntityState.Deleted);
        await using var verify = db.Create();
        Assert.Equal(1, (await verify.Set<SdOrder>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(1, (await verify.Set<SdBasket>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ASoftDeleteAtAStaleVersion_Conflicts()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var other = db.Create())
        {
            (await Orders(other).FindAsync(id, TestContext.Current.CancellationToken))!.Number = "SO-1B";
            await Commit(other);
        }

        await using var context = db.Create();
        var orders = Orders(context);
        await orders.DeleteAsync(Present.NotNull((await orders.FindAsync(id, expectedVersion: 1, ct: TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);

        var thrown = await Assert.ThrowsAsync<PersistenceException>(() => Commit(context));

        Assert.IsType<DbUpdateConcurrencyException>(thrown.InnerException);
        await using var verify = db.Create();
        Assert.Equal(1, (await verify.Set<SdOrder>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AChangeMadeBeforeTheDelete_IsWrittenWithTheMark(bool autoDetectChanges)
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            // Without automatic detection, nothing detects again after the step: it flags the changes itself.
            context.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
            var orders = Orders(context);
            var order = Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken)));
            order.Number = "SO-1-CANCELLED";
            order.Lines[0].Sku = "BOOK-RETURNED";
            order.Total = new SdMoney(42m); // a complex property
            context.ChangeTracker.DetectChanges();
            await orders.DeleteAsync(order, TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        var stored = await Unfiltered(verify.Set<SdOrder>()).SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("SO-1-CANCELLED", stored.Number);
        Assert.Equal(42m, stored.Total.Amount);
        Assert.Equal(1, (await Unfiltered(verify.Set<SdLine>()).CountAsync(l => l.Sku == "BOOK-RETURNED", cancellationToken: TestContext.Current.CancellationToken)));
    }

    // ---- Review findings: resolution, outside references, lifecycle -------------------------------

    [Fact]
    public async Task AChildMovedIntoTheRootBeforeItsSoftDeletion_IsMarked_NotDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var liveId = await SeedOrderAsync(db, "SO-2");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var deleted = Present.NotNull((await orders.FindAsync(deletedId, TestContext.Current.CancellationToken)));
            var live = Present.NotNull((await orders.FindAsync(liveId, TestContext.Current.CancellationToken)));
            var moved = live.Lines.First(l => l.Sku == "PEN");
            live.Lines.Remove(moved);
            deleted.Lines.Add(moved);
            await orders.DeleteAsync(deleted, TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        Assert.Equal(6, (await Unfiltered(verify.Set<SdLine>()).CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(2, Present.NotNull((await Orders(verify).FindAsync(liveId, TestContext.Current.CancellationToken))).Lines.Count);
    }

    [Fact]
    public async Task APrincipalSideChildTakenByALiveRoot_IsNotMarked()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var liveId = await SeedOrderAsync(db, "SO-2", withContact: false);
        Guid contactId;
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var deleted = Present.NotNull((await orders.FindAsync(deletedId, TestContext.Current.CancellationToken)));
            var live = Present.NotNull((await orders.FindAsync(liveId, TestContext.Current.CancellationToken)));
            var contact = Present.NotNull(deleted.Contact);
            contactId = contact.Id;
            deleted.Contact = null;
            live.Contact = contact;
            await orders.DeleteAsync(deleted, TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        Assert.Equal(contactId, Present.NotNull(Present.NotNull((await Orders(verify).FindAsync(liveId, TestContext.Current.CancellationToken))).Contact).Id);
    }

    [Fact]
    public async Task ATrackedEntityOutsideTheAggregate_KeepsItsReferenceToTheSoftDeletedRoot()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        var ticketId = await SeedTicketAsync(db, id, line: null);
        await using (var context = db.Create())
        {
            _ = await context.Set<SdTicket>().SingleAsync(t => t.Id == ticketId, cancellationToken: TestContext.Current.CancellationToken); // tracked
            var orders = Orders(context);
            await orders.DeleteAsync(Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        Assert.Equal(id, (await verify.Set<SdTicket>().SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).OrderId);
    }

    [Fact]
    public async Task AChildRemovedFromALiveRoot_WhileAnOutsideEntityReferencesIt_ReturnsReference()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        Guid penId;
        await using (var lookup = db.Create())
            penId = (await Orders(lookup).FindAsync(id, TestContext.Current.CancellationToken))!.Lines.Single(l => l.Sku == "PEN").Id;
        await SeedTicketAsync(db, order: null, line: penId);
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<SoftDeleteDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(static async (s, ct) =>
            {
                var order = await s.Orders.FindAsync(s.Id, ct);
                order!.Lines.Remove(order.Lines.Single(l => l.Sku == "PEN")); // a real delete: the order stays live
                await s.UnitOfWork.CommitAsync(ct);
                return MicroKit.Result.Result.Success();
            }, (Orders: Orders(context), UnitOfWork: unitOfWork, Id: id), TestContext.Current.CancellationToken);

        Assert.Equal("PERSISTENCE.SD_ORDER.REFERENCE", result.Error.Code.Value);
        await using var verify = db.Create();
        Assert.Equal(3, (await verify.Set<SdLine>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void KeysIntoASoftDeletableAggregateFromOutside_AreClientNoAction()
    {
        using var context = new SoftDeleteDbContext(Options<SoftDeleteDbContext>("unused"));
        var model = context.Model;

        Assert.Equal([DeleteBehavior.ClientNoAction], model.FindEntityType(typeof(SdTicket))!.GetForeignKeys().Select(fk => fk.DeleteBehavior).Distinct());
        var join = model.GetEntityTypes().Single(t => t.IsPropertyBag);
        Assert.Equal(DeleteBehavior.ClientNoAction, join.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(SdOrder)).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, model.FindEntityType(typeof(SdLine))!.GetForeignKeys().Single().DeleteBehavior);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnOwnedValueReplacedBeforeTheSoftDelete_IsWritten(bool onSaveChanges)
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            if (onSaveChanges)
                context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
            var orders = Orders(context);
            var order = Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken)));
            order.Address = new SdAddress { City = "Paris" };
            order.Notes.Add(new SdNote { Text = "late" });
            await orders.DeleteAsync(order, TestContext.Current.CancellationToken);
            await Commit(context);
        }

        await using var verify = db.Create();
        var stored = await Unfiltered(verify.Set<SdOrder>()).Include(o => o.Notes).SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Paris", Present.NotNull(stored.Address).City);
        Assert.Equal(["fragile", "late"], stored.Notes.Select(n => n.Text).OrderBy(t => t, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task ASoftDeleteRetriedAfterATransientFailure_DetachesOnlyWhatTheSuccessfulAttemptMarked()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var otherId = await SeedOrderAsync(db, "SO-2");
        var failing = new FailingUpdateInterceptor(failures: 1);
        await using var context = db.Create(o => o.AddInterceptors(failing), maxRetryCount: 3);
        var unitOfWork = new EfUnitOfWork<SoftDeleteDbContext>(context);

        await unitOfWork.ExecuteAsync(static async (s, ct) =>
            {
                await s.Orders.DeleteAsync(Present.NotNull((await s.Orders.FindAsync(s.Id, ct))), ct);
                await s.UnitOfWork.CommitAsync(ct);
            }, (Orders: Orders(context), UnitOfWork: unitOfWork, Id: deletedId), TestContext.Current.CancellationToken);

        Assert.Equal(1, failing.Thrown);
        await AssertTheContextStillTracksANewAggregateAsync(context, otherId);
    }

    [Fact]
    public async Task ASoftDeleteSavedWithoutAcceptingItsChanges_IsWrittenAgain_WhenTheSaveIsRetried()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var order = Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken)));
            await orders.DeleteAsync(order, TestContext.Current.CancellationToken);

            // The pattern of a save retried by hand inside a transaction: SaveChanges(false), and on a failure the
            // transaction rolls back and the same changes are saved again.
            await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
            {
                await context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken: TestContext.Current.CancellationToken);
                await transaction.RollbackAsync(TestContext.Current.CancellationToken);
            }

            Assert.Equal(EntityState.Modified, context.Entry(order).State);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var verify = db.Create();
        var stored = await Unfiltered(verify.Set<SdOrder>()).Include(o => o.Notes).SingleAsync(o => o.Id == id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Now, Mark(verify, stored));
        Assert.Single(stored.Notes);
        await AllMarkedAsync<SdLine>(verify, expected: 3);
        await AllMarkedAsync<SdOption>(verify, expected: 1);
        await AllMarkedAsync<SdContact>(verify, expected: 1);
        await AllMarkedAsync<SdRemark>(verify, expected: 1);
        await AllMarkedAsync<SdMemo>(verify, expected: 1);
    }

    [Fact]
    public async Task ASoftDeleteSavedWithoutAcceptingItsChanges_ThenAcceptedByHand_IsNotFoundAgain()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            await orders.DeleteAsync(Present.NotNull((await orders.FindAsync(id, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);

            // EF Core's resilient pattern: SaveChanges(false), commit, then AcceptAllChanges(), which raises no
            // SavedChanges.
            await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
            {
                await context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken: TestContext.Current.CancellationToken);
                await transaction.CommitAsync(TestContext.Current.CancellationToken);
            }

            context.ChangeTracker.AcceptAllChanges();

            Assert.Null((await orders.FindAsync(id, TestContext.Current.CancellationToken)));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var verify = db.Create();
        await AllMarkedAsync<SdLine>(verify, expected: 3);
    }

    [Theory]
    [InlineData(SdBaseKeyDbContext.Variant.PrincipalIsTheRootsBase, "required foreign key 'SdDocLine.DocId'")]
    [InlineData(SdBaseKeyDbContext.Variant.DeclaredOnAChildsBase, "'SdPartBase.OwnerId'")] // refused as a key from outside
    public void ARequiredKeyThroughAMappedBaseType_FailsModelBuilding(SdBaseKeyDbContext.Variant variant, string reason)
    {
        using var context = new SdBaseKeyDbContext(Options<SdBaseKeyDbContext>("unused", ModelVariantCacheKeyFactory.Use), variant);

        var thrown = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains(reason, thrown.Message);
        Assert.Contains("OnDelete(DeleteBehavior.Restrict)", thrown.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailedSoftDelete_ThenAClearedTracker_LeavesNothingForTheNextSave(bool pooled)
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var otherId = await SeedOrderAsync(db, "SO-2");
        await using (var other = db.Create())
        {
            (await Orders(other).FindAsync(deletedId, TestContext.Current.CancellationToken))!.Number = "SO-1B";
            await Commit(other);
        }

        var pool = new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<SoftDeleteDbContext>(db.OptionsFor(), poolSize: 1);
        var first = pooled ? pool.CreateDbContext() : db.Create();
        var orders = Orders(first);
        await orders.DeleteAsync(Present.NotNull((await orders.FindAsync(deletedId, expectedVersion: 1, ct: TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<PersistenceException>(() => Commit(first)); // stale: nothing written

        SoftDeleteDbContext next;
        if (pooled)
        {
            await first.DisposeAsync(); // back to the pool: the lease is reset
            next = pool.CreateDbContext();
            Assert.Same(first, next);
        }
        else
        {
            new EfUnitOfWork<SoftDeleteDbContext>(first).DiscardChanges();
            next = first;
        }

        await AssertTheContextStillTracksANewAggregateAsync(next, otherId);
        await next.DisposeAsync();
        await using var verify = db.Create();
        Assert.Equal(2, (await verify.Set<SdOrder>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AnAnonymousFilterOnAnotherAggregate_StillAppliesWithDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        await using (var context = db.Create())
        {
            context.Add(new SdBasket { Id = new SdOrderId(Guid.NewGuid()), Owner = SoftDeleteDbContext.HiddenTenant });
            await Commit(context);
        }

        await using var reads = db.Create();
        var baskets = new EfReadRepository<SdBasket, SoftDeleteDbContext>(reads, new EfSpecificationEvaluator(), PaginationLimits.Default);

        Assert.Empty((await baskets.ListAsync(new QueryOptions<SdBasket>().WithDeleted(), TestContext.Current.CancellationToken)));
        Assert.Equal(0, (await baskets.CountAsync(new QueryOptions<SdBasket>().WithDeleted(), TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void ARootWithAMappedBaseType_CarriesTheFilterOnTheBaseType()
    {
        using var context = new SdBaseTypeDbContext(Options<SdBaseTypeDbContext>("unused"));

        var record = context.Model.FindEntityType(typeof(SdRecord))!;
        Assert.NotNull(record.FindDeclaredQueryFilter(SoftDelete.FilterName));
        Assert.NotNull(record.FindDeclaredProperty(SoftDelete.PropertyName));
    }

    [Fact]
    public async Task ARootAndAChildSharingATable_ShareOneMarkColumn()
    {
        var name = "softdelete_split_" + Guid.NewGuid().ToString("N");
        await using (var setup = new SdSplitDbContext(Options<SdSplitDbContext>(name)))
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        try
        {
            var id = new SdFolderId(Guid.NewGuid());
            await using (var context = new SdSplitDbContext(Options<SdSplitDbContext>(name)))
            {
                var folder = new SdFolder { Id = id, Name = "invoices", Details = new SdFolderDetails { Description = "2026" } };
                await new EfRepository<SdFolder, SdFolderId, SdSplitDbContext>(context).AddAsync(folder, TestContext.Current.CancellationToken);
                await new EfUnitOfWork<SdSplitDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
            }

            await using (var context = new SdSplitDbContext(Options<SdSplitDbContext>(name)))
            {
                var folders = new EfRepository<SdFolder, SdFolderId, SdSplitDbContext>(context);
                await folders.DeleteAsync(Present.NotNull((await folders.FindAsync(id, TestContext.Current.CancellationToken))), TestContext.Current.CancellationToken);
                await new EfUnitOfWork<SdSplitDbContext>(context).CommitAsync(TestContext.Current.CancellationToken);
            }

            await using var verify = new SdSplitDbContext(Options<SdSplitDbContext>(name));
            var stored = await verify.Set<SdFolder>().IgnoreQueryFilters([SoftDelete.FilterName]).Include(f => f.Details).SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(Now, Mark(verify, stored));
            Assert.Equal(Now, Mark(verify, stored.Details));
            Assert.Equal("2026", stored.Details.Description);
        }
        finally
        {
            await using var cleanup = new SdSplitDbContext(Options<SdSplitDbContext>(name));
            await cleanup.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        }
    }

    // ---- The model ---------------------------------------------------------------------------------

    [Fact]
    public void TheModel_PutsTheMarkAndTheFilterOnEachHierarchyRoot_NotOnOwnedTypes()
    {
        using var context = new SoftDeleteDbContext(Options<SoftDeleteDbContext>("unused"));
        var model = context.Model;

        foreach (var type in new[] { typeof(SdOrder), typeof(SdLine), typeof(SdOption), typeof(SdContact), typeof(SdRemark), typeof(SdMemo), typeof(SdStamp) })
        {
            var entity = model.FindEntityType(type.FullName!)!;
            var mark = entity.FindDeclaredProperty(SoftDelete.PropertyName);
            Assert.True(mark is not null, type.Name);
            Assert.Equal(typeof(DateTimeOffset?), mark.ClrType);
            Assert.True(entity.FindDeclaredQueryFilter(SoftDelete.FilterName) is not null, type.Name);
        }

        Assert.Null(model.FindEntityType(typeof(SdGiftLine))!.FindDeclaredProperty(SoftDelete.PropertyName));
        Assert.Null(model.FindEntityType(typeof(SdAddress))!.FindProperty(SoftDelete.PropertyName));
        Assert.Null(model.FindEntityType(typeof(SdNote))!.FindProperty(SoftDelete.PropertyName));
        Assert.Null(model.FindEntityType(typeof(SdBasket))!.FindProperty(SoftDelete.PropertyName));
        Assert.NotNull(model.FindEntityType(typeof(SdOrder))!.FindDeclaredQueryFilter("Tenant"));
    }

    public enum Misuse
    {
        OnAChild,
        OnADerivedRoot,
        AnonymousFilter,
        RequiredRestrict,
        RequiredClientSetNull,
        RequiredWithoutNavigation,
        ClrMember,
        NotDateTimeOffset,
        SameColumn,
        SameColumnUnderTpc,
        UnrelatedTpcColumn,
        MarkRequired,
        MarkDefaultValueSql,
        MarkValueGenerated,
        MarkConverted,
        OutsideCascade,
        OutsideRestrict,
    }

    [Theory]
    [InlineData(Misuse.OnAChild, "is not the top-most aggregate root")]
    [InlineData(Misuse.OnADerivedRoot, "is not the top-most aggregate root")]
    [InlineData(Misuse.AnonymousFilter, "carries an anonymous query filter")]
    [InlineData(Misuse.RequiredRestrict, "OnDelete(DeleteBehavior.Restrict)")]
    [InlineData(Misuse.RequiredClientSetNull, "OnDelete(DeleteBehavior.ClientSetNull)")]
    [InlineData(Misuse.RequiredWithoutNavigation, "required foreign key 'SdGiftLine.SourceLineId' (no navigation) is configured with OnDelete(DeleteBehavior.Restrict)")]
    [InlineData(Misuse.ClrMember, "maps it to a CLR member")]
    [InlineData(Misuse.NotDateTimeOffset, "not 'DateTimeOffset?'")]
    [InlineData(Misuse.SameColumn, "is also mapped by")]
    [InlineData(Misuse.SameColumnUnderTpc, "reserved on 'SdTpcDocument', an entity of the soft-deletable aggregate 'SdTpcDocument'")]
    [InlineData(Misuse.MarkRequired, "configures it as required")]
    [InlineData(Misuse.MarkDefaultValueSql, "gives it a default value")]
    [InlineData(Misuse.MarkValueGenerated, "configures it as generated")]
    [InlineData(Misuse.MarkConverted, "gives it a value converter")]
    [InlineData(Misuse.OutsideCascade, "OnDelete(DeleteBehavior.Cascade)")]
    [InlineData(Misuse.OutsideRestrict, "OnDelete(DeleteBehavior.Restrict)")]
    public void AMisconfiguredSoftDelete_FailsModelBuilding(Misuse misuse, string reason)
    {
        using var context = new SoftDeleteMisuseDbContext(Options<SoftDeleteMisuseDbContext>("unused", ModelVariantCacheKeyFactory.Use), misuse);

        var thrown = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains(reason, thrown.Message);
    }

    [Fact]
    public void AMarkColumnInTheTableOfAnUnrelatedTpcType_IsNotACollision()
    {
        using var context = new SoftDeleteMisuseDbContext(
            Options<SoftDeleteMisuseDbContext>("unused", ModelVariantCacheKeyFactory.Use), Misuse.UnrelatedTpcColumn);

        Assert.Null(Record.Exception(() => _ = context.Model));
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private static IQueryable<T> Unfiltered<T>(IQueryable<T> query) where T : class =>
        query.IgnoreQueryFilters([SoftDelete.FilterName]);

    private static DateTimeOffset? Mark(DbContext context, object entity) =>
        (DateTimeOffset?)context.Entry(entity).Property(SoftDelete.PropertyName).CurrentValue;

    private static async Task AllMarkedAsync<T>(SoftDeleteDbContext context, int expected) where T : class
    {
        var rows = await Unfiltered(context.Set<T>()).ToListAsync();
        Assert.Equal(expected, rows.Count);
        foreach (var row in rows)
            Assert.Equal(Now, Mark(context, row));
    }

    private static EfRepository<SdOrder, SdOrderId, SoftDeleteDbContext> Orders(SoftDeleteDbContext context) => new(context);

    private static EfRepository<SdBasket, SdOrderId, SoftDeleteDbContext> Baskets(SoftDeleteDbContext context) => new(context);

    private static Task Commit(SoftDeleteDbContext context) => new EfUnitOfWork<SoftDeleteDbContext>(context).CommitAsync().AsTask();

    private static async Task SoftDeleteAsync(Database db, SdOrderId id)
    {
        await using var context = db.Create();
        var orders = Orders(context);
        await orders.DeleteAsync(Present.NotNull((await orders.FindAsync(id))));
        await Commit(context);
    }

    private static async Task AssertTheContextStillTracksANewAggregateAsync(SoftDeleteDbContext context, SdOrderId id)
    {
        Assert.Empty(context.ChangeTracker.Entries());
        var order = Present.NotNull((await Orders(context).FindAsync(id)));
        order.Number = "SO-2-RENAMED";
        await Commit(context);
        Assert.Equal(EntityState.Unchanged, context.Entry(order).State);
        Assert.True(context.ChangeTracker.Entries().Count() > 1);
    }

    private static async Task<Guid> SeedTicketAsync(Database db, SdOrderId? order, Guid? line)
    {
        var ticket = new SdTicket { OrderId = order, LineId = line };
        await using var context = db.Create();
        context.Add(ticket);
        await Commit(context);
        return ticket.Id;
    }

    private static async Task<SdOrderId> SeedOrderAsync(Database db, string number, bool withContact = true)
    {
        var order = new SdOrder
        {
            Id = new SdOrderId(Guid.NewGuid()),
            Number = number,
            Address = new SdAddress { City = "Lyon" },
            Contact = withContact ? new SdContact { Name = "Ada" } : null,
        };
        var book = new SdLine { Sku = "BOOK" };
        book.Options.Add(new SdOption { Name = "gift wrap" });
        order.Lines.Add(book);
        order.Lines.Add(new SdLine { Sku = "PEN" });
        order.Lines.Add(new SdGiftLine { Sku = "CARD", Message = "Happy birthday" });
        order.Notes.Add(new SdNote { Text = "fragile" });
        order.Remarks.Add(new SdRemark { Text = "call first" });
        order.Memos.Add(new SdMemo { Text = "VIP" });

        await using var context = db.Create();
        await Orders(context).AddAsync(order);
        await Commit(context);
        return order.Id;
    }

    private static async Task<SdOrderId> SeedBasketAsync(Database db)
    {
        var basket = new SdBasket { Id = new SdOrderId(Guid.NewGuid()) };
        basket.Items.Add(new SdBasketItem { Sku = "APPLE" });
        basket.Items.Add(new SdBasketItem { Sku = "PEAR" });
        await using var context = db.Create();
        await Baskets(context).AddAsync(basket);
        await Commit(context);
        return basket.Id;
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
        builder.UseApplicationServiceProvider(s_applicationServices).UseMicroKitPersistence(Classifier);
        return builder.Options;
    }

    private async Task<Database> CreateDatabaseAsync()
    {
        var db = new Database(this, "softdelete_" + Guid.NewGuid().ToString("N"));
        await using var context = db.Create();
        await context.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>One database of the provider, dropped on dispose.</summary>
    private sealed class Database(SoftDeleteScenarios owner, string name) : IAsyncDisposable
    {
        public string Name { get; } = name;

        public SoftDeleteDbContext Create(Action<DbContextOptionsBuilder>? configure = null, int? maxRetryCount = null) =>
            new(owner.Options<SoftDeleteDbContext>(Name, configure, maxRetryCount));

        public DbContextOptions<SoftDeleteDbContext> OptionsFor() => owner.Options<SoftDeleteDbContext>(Name);

        public async ValueTask DisposeAsync()
        {
            await using var context = Create();
            await context.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>Fails the first <c>failures</c> UPDATE commands with the exception a test classifies as transient.</summary>
    /// <summary>Counts the commands the context sends.</summary>
    private sealed class CommandCounter : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private int _count;

        public int Count => _count;

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class FailingUpdateInterceptor(int failures) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private int _remaining = failures;

        public int Thrown { get; private set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_remaining > 0 && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                _remaining--;
                Thrown++;
                throw new TransientMarkerException();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NumberIs(string number) : MicroKit.Domain.Specifications.Specification<SdOrder>
    {
        public override System.Linq.Expressions.Expression<Func<SdOrder, bool>> ToExpression() => o => o.Number == number;
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class SoftDeletePostgreSqlTests(PostgreSqlSuiteFixture fixture) : SoftDeleteScenarios
{
    protected override IDatabaseFailureClassifier Classifier => MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlFailureClassifier.Instance;

    protected override void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null) =>
        builder.UseNpgsql(fixture.GetConnectionString(database), npgsql =>
        {
            if (maxRetryCount is { } retries)
                npgsql.ExecutionStrategy(d => new MarkerRetryingExecutionStrategy(d, retries));
        });
}

public readonly record struct SdOrderId(Guid Value) : IEntityId;

/// <summary>
/// A soft-deletable aggregate: a collection with a grandchild and a derived type, a contact the root
/// references (one-to-one), an owned type in the root's table and an owned collection in its own, an
/// optional ClientSetNull child and an optional ClientNoAction one.
/// </summary>
public class SdOrder : IAggregateRoot
{
    public SdOrderId Id { get; init; }

    public string Number { get; set; } = string.Empty;

    public string Tenant { get; set; } = "main";

    public List<SdLine> Lines { get; } = [];

    public SdAddress? Address { get; set; }

    public List<SdNote> Notes { get; } = [];

    public Guid? ContactId { get; set; }

    public SdContact? Contact { get; set; }

    public List<SdRemark> Remarks { get; } = [];

    public List<SdMemo> Memos { get; } = [];

    public List<SdStamp> Stamps { get; } = [];

    public SdMoney Total { get; set; } = new(0m);

    public List<SdLabel> Labels { get; } = [];
}

/// <summary>A value object mapped as a complex property.</summary>
public sealed record SdMoney(decimal Amount);

/// <summary>Reference data outside every aggregate, linked to orders by a many-to-many marked outside.</summary>
public sealed class SdLabel
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>An entity outside every aggregate that references an order and one of its lines.</summary>
public sealed class SdTicket
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public SdOrderId? OrderId { get; set; }

    public Guid? LineId { get; set; }
}

public sealed class SdPriorityOrder : SdOrder;

public class SdLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;

    public List<SdOption> Options { get; } = [];
}

public sealed class SdGiftLine : SdLine
{
    public string Message { get; set; } = string.Empty;
}

/// <summary>A line type that maps the reserved name to a CLR member.</summary>
public sealed class SdMarkedLine : SdLine
{
    public DateTimeOffset? AggregateDeletedAt { get; set; }
}

public sealed class SdOption
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

public sealed class SdAddress
{
    public string City { get; set; } = string.Empty;
}

public sealed class SdNote
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

public sealed class SdContact
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

public sealed class SdRemark
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public SdOrderId? OrderId { get; set; }

    public string Text { get; set; } = string.Empty;
}

public sealed class SdMemo
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public SdOrderId? OrderId { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>A required child whose key is ClientNoAction: EF Core leaves it alone when its root is removed.</summary>
public sealed class SdStamp
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public SdOrderId OrderId { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>A soft-deletable TPC root whose abstract base has no table.</summary>
public abstract class SdTpcDocument : IAggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class SdTpcInvoice : SdTpcDocument
{
    public DateTimeOffset? Legacy { get; set; }
}

/// <summary>Outside every aggregate: a TPC hierarchy whose concrete row has its own AggregateDeletedAt column.</summary>
public abstract class SdTpcLogBase
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class SdTpcLog : SdTpcLogBase
{
    public DateTimeOffset? AggregateDeletedAt { get; set; }
}

/// <summary>An aggregate without soft delete.</summary>
public sealed class SdBasket : IAggregateRoot
{
    public SdOrderId Id { get; init; }

    public string Owner { get; set; } = "me";

    public List<SdBasketItem> Items { get; } = [];
}

public sealed class SdBasketItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;
}

public class SoftDeleteDbContext(DbContextOptions options) : DbContext(options)
{
    public const string HiddenTenant = "hidden";

    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<SdOrderId, Guid> OrderIdConverter =
        new(id => id.Value, v => new SdOrderId(v));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SdOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion(id => id.Value, v => new SdOrderId(v)).ValueGeneratedNever();
            b.HasSoftDelete();
            b.HasQueryFilter("Tenant", o => o.Tenant != HiddenTenant);
            // The README recipe: a soft-deleted order does not hold its number.
            b.HasIndex(o => o.Number).IsUnique().HasFilter("\"AggregateDeletedAt\" IS NULL");
            b.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.OwnsOne(o => o.Address);
            b.OwnsMany(o => o.Notes, n =>
            {
                n.ToTable("SdNotes");
                n.HasKey(x => x.Id);
                n.Property(x => x.Id).ValueGeneratedNever();
            });
            b.HasOne(o => o.Contact).WithOne().HasForeignKey<SdOrder>(o => o.ContactId);
            b.HasMany(o => o.Remarks).WithOne().HasForeignKey(r => r.OrderId).IsRequired(false).OnDelete(DeleteBehavior.ClientSetNull);
            b.HasMany(o => o.Memos).WithOne().HasForeignKey(m => m.OrderId).IsRequired(false).OnDelete(DeleteBehavior.ClientNoAction);
            b.HasMany(o => o.Stamps).WithOne().HasForeignKey(s => s.OrderId).IsRequired().OnDelete(DeleteBehavior.ClientNoAction);
            b.ComplexProperty(o => o.Total);
            b.HasMany(o => o.Labels).WithMany();
            b.Navigation(o => o.Labels).IsOutsideAggregate();
        });
        modelBuilder.Entity<SdLabel>().Property(l => l.Id).ValueGeneratedNever();
        modelBuilder.Entity<SdTicket>(b =>
        {
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.OrderId).HasConversion(OrderIdConverter);
            b.HasOne<SdOrder>().WithMany().HasForeignKey(t => t.OrderId).IsRequired(false);
            b.HasOne<SdLine>().WithMany().HasForeignKey(t => t.LineId).IsRequired(false);
        });
        modelBuilder.Entity<SdLine>(b =>
        {
            b.Property(l => l.Id).ValueGeneratedNever();
            b.HasMany(l => l.Options).WithOne().HasForeignKey("LineId").OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SdGiftLine>();
        modelBuilder.Entity<SdOption>().Property(o => o.Id).ValueGeneratedNever();
        modelBuilder.Entity<SdContact>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<SdRemark>(b =>
        {
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.OrderId).HasConversion(OrderIdConverter);
        });
        modelBuilder.Entity<SdMemo>(b =>
        {
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.OrderId).HasConversion(OrderIdConverter);
        });
        modelBuilder.Entity<SdStamp>(b =>
        {
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.OrderId).HasConversion(OrderIdConverter);
        });
        modelBuilder.Entity<SdBasket>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasQueryFilter(x => x.Owner != HiddenTenant); // anonymous: the basket is not soft-deletable
            b.Property(x => x.Id).HasConversion(id => id.Value, v => new SdOrderId(v)).ValueGeneratedNever();
            b.HasMany(x => x.Items).WithOne().HasForeignKey("BasketId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SdBasketItem>().Property(i => i.Id).ValueGeneratedNever();
    }
}

public sealed class SoftDeleteMisuseDbContext(DbContextOptions options, SoftDeleteScenarios.Misuse misuse)
    : DbContext(options), IModelVariant
{
    public object Variant => misuse;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SdOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion(id => id.Value, v => new SdOrderId(v));
            b.Ignore(o => o.Address).Ignore(o => o.Notes).Ignore(o => o.Contact).Ignore(o => o.Remarks).Ignore(o => o.Memos)
                .Ignore(o => o.Stamps).Ignore(o => o.Total).Ignore(o => o.Labels);
            if (misuse == SoftDeleteScenarios.Misuse.MarkRequired)
                b.Property<DateTimeOffset?>(SoftDelete.PropertyName).IsRequired();
            if (misuse == SoftDeleteScenarios.Misuse.MarkDefaultValueSql)
                b.Property<DateTimeOffset?>(SoftDelete.PropertyName).HasDefaultValueSql("CURRENT_TIMESTAMP");
            if (misuse == SoftDeleteScenarios.Misuse.MarkValueGenerated)
                b.Property<DateTimeOffset?>(SoftDelete.PropertyName).ValueGeneratedOnAddOrUpdate();
            if (misuse == SoftDeleteScenarios.Misuse.MarkConverted)
                b.Property<DateTimeOffset?>(SoftDelete.PropertyName).HasConversion<string>();
            if (misuse != SoftDeleteScenarios.Misuse.OnADerivedRoot)
                b.HasSoftDelete();
            if (misuse == SoftDeleteScenarios.Misuse.AnonymousFilter)
                b.HasQueryFilter(o => o.Tenant != "hidden");
            if (misuse == SoftDeleteScenarios.Misuse.NotDateTimeOffset)
                b.Property<bool>(SoftDelete.PropertyName);
            if (misuse == SoftDeleteScenarios.Misuse.SameColumn)
                b.Property(o => o.Tenant).HasColumnName(SoftDelete.PropertyName);

            var lines = b.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").IsRequired();
            if (misuse == SoftDeleteScenarios.Misuse.RequiredRestrict)
                lines.OnDelete(DeleteBehavior.Restrict);
            else if (misuse == SoftDeleteScenarios.Misuse.RequiredClientSetNull)
                lines.OnDelete(DeleteBehavior.ClientSetNull);
        });
        modelBuilder.Entity<SdLine>(b =>
        {
            b.Ignore(l => l.Options);
            if (misuse == SoftDeleteScenarios.Misuse.OnAChild)
                b.HasSoftDelete();
        });
        modelBuilder.Entity<SdGiftLine>(b =>
        {
            // A required key between two types of the aggregate that no navigation of the boundary walk reaches.
            if (misuse == SoftDeleteScenarios.Misuse.RequiredWithoutNavigation)
                b.HasOne<SdLine>().WithMany().HasForeignKey("SourceLineId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        });
        if (misuse == SoftDeleteScenarios.Misuse.UnrelatedTpcColumn)
        {
            modelBuilder.Entity<SdTpcLogBase>().UseTpcMappingStrategy();
            modelBuilder.Entity<SdTpcLog>();
        }

        if (misuse == SoftDeleteScenarios.Misuse.SameColumnUnderTpc)
        {
            modelBuilder.Entity<SdTpcDocument>(b =>
            {
                b.UseTpcMappingStrategy();
                b.HasSoftDelete();
            });
            modelBuilder.Entity<SdTpcInvoice>().Property(i => i.Legacy).HasColumnName(SoftDelete.PropertyName);
        }

        if (misuse is SoftDeleteScenarios.Misuse.OutsideCascade or SoftDeleteScenarios.Misuse.OutsideRestrict)
        {
            modelBuilder.Entity<SdTicket>(b =>
            {
                b.Property(t => t.OrderId).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<SdOrderId, Guid>(id => id.Value, v => new SdOrderId(v)));
                b.Ignore(t => t.LineId);
                b.HasOne<SdOrder>().WithMany().HasForeignKey(t => t.OrderId).IsRequired(false)
                    .OnDelete(misuse == SoftDeleteScenarios.Misuse.OutsideCascade ? DeleteBehavior.Cascade : DeleteBehavior.Restrict);
            });
        }

        if (misuse == SoftDeleteScenarios.Misuse.ClrMember)
            modelBuilder.Entity<SdMarkedLine>();
        if (misuse == SoftDeleteScenarios.Misuse.OnADerivedRoot)
            modelBuilder.Entity<SdPriorityOrder>().HasSoftDelete();
    }
}

/// <summary>A mapped base type that is not an aggregate root, under a soft-deletable root (TPH).</summary>
public abstract class SdRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class SdArchive : SdRecord, IAggregateRoot
{
    public string Title { get; set; } = string.Empty;
}

public sealed class SdJournal : SdRecord
{
    public string Text { get; set; } = string.Empty;
}

public sealed class SdBaseTypeDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SdRecord>().Property(r => r.Id).ValueGeneratedNever();
        modelBuilder.Entity<SdArchive>().HasSoftDelete();
        modelBuilder.Entity<SdJournal>();
    }
}

/// <summary>A soft-deletable root whose mapped base type, no aggregate root, holds the collection of lines.</summary>
public abstract class SdDocBase
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public List<SdDocLine> Lines { get; } = [];
}

public sealed class SdDoc : SdDocBase, IAggregateRoot;

public sealed class SdDocLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid DocId { get; set; }
}

/// <summary>A soft-deletable root whose child type derives from a mapped base that declares a key to the root.</summary>
public sealed class SdBinder : IAggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public List<SdPart> Parts { get; } = [];
}

public abstract class SdPartBase
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }
}

public sealed class SdPart : SdPartBase;

public sealed class SdBaseKeyDbContext(DbContextOptions options, SdBaseKeyDbContext.Variant variant) : DbContext(options), IModelVariant
{
    public enum Variant
    {
        PrincipalIsTheRootsBase,
        DeclaredOnAChildsBase,
    }

    object IModelVariant.Variant => variant;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (variant == Variant.PrincipalIsTheRootsBase)
        {
            modelBuilder.Entity<SdDocBase>()
                .HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.DocId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SdDoc>().HasSoftDelete();
        }
        else
        {
            modelBuilder.Entity<SdBinder>(b =>
            {
                b.HasSoftDelete();
                b.HasMany(x => x.Parts).WithOne().HasForeignKey("BinderId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<SdPartBase>()
                .HasOne<SdBinder>().WithMany().HasForeignKey(p => p.OwnerId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SdPart>();
        }
    }
}

public readonly record struct SdFolderId(Guid Value) : IEntityId;

/// <summary>A root and a one-to-one child of its aggregate that share the root's table.</summary>
public sealed class SdFolder : IAggregateRoot
{
    public SdFolderId Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public SdFolderDetails Details { get; set; } = new();
}

public sealed class SdFolderDetails
{
    public SdFolderId Id { get; init; }

    public string Description { get; set; } = string.Empty;
}

public sealed class SdSplitDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SdFolder>(b =>
        {
            b.ToTable("SdFolders");
            b.Property(f => f.Id).HasConversion(id => id.Value, v => new SdFolderId(v)).ValueGeneratedNever();
            b.HasSoftDelete();
            b.HasOne(f => f.Details).WithOne().HasForeignKey<SdFolderDetails>(d => d.Id);
            b.Navigation(f => f.Details).IsRequired();
        });
        modelBuilder.Entity<SdFolderDetails>(b =>
        {
            b.ToTable("SdFolders");
            b.Property(d => d.Id).HasConversion(id => id.Value, v => new SdFolderId(v));
        });
    }
}

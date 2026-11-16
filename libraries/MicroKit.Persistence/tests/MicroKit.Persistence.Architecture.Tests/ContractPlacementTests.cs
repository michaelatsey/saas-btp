namespace MicroKit.Persistence.Architecture.Tests;

public sealed class ContractPlacementTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(MicroKit.Persistence.Abstractions.IUnitOfWork).Assembly;

    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly;

    private static readonly Assembly EfCoreAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly;

    // --- In Abstractions ---

    [Fact]
    public void IUnitOfWork_IsInAbstractions()
    {
        Assert.Equal(AbstractionsAssembly, typeof(MicroKit.Persistence.Abstractions.IUnitOfWork).Assembly);
    }

    [Fact]
    public void ITransactionalContext_IsInAbstractions()
    {
        Assert.Equal(AbstractionsAssembly, typeof(MicroKit.Persistence.Abstractions.ITransactionalContext).Assembly);
    }

    [Fact]
    public void IPagedResult_IsInAbstractions()
    {
        Assert.Equal(AbstractionsAssembly, typeof(MicroKit.Persistence.Abstractions.IPagedResult<>).Assembly);
    }

    [Fact]
    public void IRepository_IsInAbstractions()
    {
        Assert.Equal(AbstractionsAssembly, typeof(MicroKit.Persistence.Abstractions.IRepository<,>).Assembly);
    }

    [Fact]
    public void PersistenceException_IsInAbstractions()
    {
        Assert.Equal(AbstractionsAssembly, typeof(MicroKit.Persistence.Abstractions.PersistenceException).Assembly);
    }

    // --- In Core (not Abstractions) ---

    [Fact]
    public void IReadRepository_Full_IsInCore()
    {
        // #196 D8: the only read repository contract; it needs QueryOptions, so it lives in Core.
        Assert.Equal(CoreAssembly, typeof(MicroKit.Persistence.IReadRepository<>).Assembly);
        Assert.NotEqual(AbstractionsAssembly, typeof(MicroKit.Persistence.IReadRepository<>).Assembly);
    }

    [Fact]
    public void ISpecificationEvaluator_IsInCore_NotAbstractions()
    {
        Assert.Equal(CoreAssembly, typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly);
        Assert.NotEqual(AbstractionsAssembly, typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly);
    }

    [Fact]
    public void QueryOptions_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(MicroKit.Persistence.QueryOptions<>).Assembly);
    }

    [Fact]
    public void PagedResult_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(MicroKit.Persistence.PagedResult<>).Assembly);
    }

    [Fact]
    public void TheResultPath_IsInCore_NotAbstractions()
    {
        // #197 phase 3: a handler depends on IResultUnitOfWork without referencing EF Core; the codes,
        // the messages and the version-conflict marker live with it.
        Assert.Equal(CoreAssembly, typeof(MicroKit.Persistence.IResultUnitOfWork).Assembly);
        Assert.Equal(CoreAssembly, typeof(MicroKit.Persistence.PersistenceErrors).Assembly);
        Assert.Equal(CoreAssembly, typeof(MicroKit.Persistence.AggregateVersionConflictException).Assembly);
    }

    [Fact]
    public void TheResultPath_IsImplementedByTheEfCoreUnitOfWork()
    {
        Assert.True(typeof(MicroKit.Persistence.IResultUnitOfWork)
            .IsAssignableFrom(typeof(MicroKit.Persistence.EntityFrameworkCore.EfUnitOfWork<>).MakeGenericType(typeof(Microsoft.EntityFrameworkCore.DbContext))));
    }

    // --- In EFCore (not Abstractions) ---

    [Fact]
    public void ITransactionalUnitOfWork_IsInEntityFrameworkCore_NotAbstractions()
    {
        // MicroKit ADR-004 (michaelatsey/microkit@a12097b): ITransactionalUnitOfWork is EF-specific and lives in EntityFrameworkCore, not Abstractions.
        Assert.Equal(EfCoreAssembly, typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly);
        Assert.NotEqual(AbstractionsAssembly, typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly);
    }

    [Fact]
    public void EfUnitOfWork_IsInEntityFrameworkCore()
    {
        Assert.Equal(EfCoreAssembly, typeof(MicroKit.Persistence.EntityFrameworkCore.EfUnitOfWork<>).Assembly);
    }

    [Fact]
    public void EfSpecificationEvaluator_IsInEntityFrameworkCore()
    {
        Assert.Equal(EfCoreAssembly, typeof(MicroKit.Persistence.EntityFrameworkCore.EfSpecificationEvaluator).Assembly);
    }
}

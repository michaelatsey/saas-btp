namespace MicroKit.Persistence.Architecture.Tests;

public sealed class SealedClassTests
{
    // IsSealed on open generic type definitions always returns false in the CLR via Type.IsSealed.
    // TypeAttributes.Sealed is the correct way to check sealed for both open generic and non-generic types.
    private static bool IsSealed(Type type) =>
        (type.Attributes & TypeAttributes.Sealed) != 0;

    [Fact]
    public void EfUnitOfWork_IsSealed()
    {
        Assert.True(IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.EfUnitOfWork<>)));
    }

    [Fact]
    public void EfSpecificationEvaluator_IsSealed()
    {
        Assert.True(IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.EfSpecificationEvaluator)));
    }

    [Fact]
    public void EfDomainEventsProvider_IsSealed()
    {
        Assert.True(IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.EfDomainEventsProvider<>)));
    }

    [Fact]
    public void AggregateVersionConflictException_IsSealed()
    {
        Assert.True(IsSealed(typeof(MicroKit.Persistence.AggregateVersionConflictException)));
    }

    [Fact]
    public void FailureClassifiers_AreSealed()
    {
        Assert.True(IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlFailureClassifier)));
    }

    [Fact]
    public void QueryOptions_IsSealed()
    {
        Assert.True(IsSealed(typeof(MicroKit.Persistence.QueryOptions<>)));
    }

    [Fact]
    public void PagedResult_IsSealed()
    {
        Assert.True(IsSealed(typeof(MicroKit.Persistence.PagedResult<>)));
    }

    [Fact]
    public void PaginationOptions_IsSealed()
    {
        Assert.True(IsSealed(typeof(MicroKit.Persistence.PaginationOptions)));
    }

    // EfRepository<,,> is intentionally not sealed — consumers subclass it for typed repositories.
    [Fact]
    public void EfRepository_BaseClass_IsNotSealed()
    {
        Assert.False(IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.EfRepository<,,>)));
    }

    [Fact]
    public void EfReadRepository_BaseClass_IsNotSealed()
    {
        // Open generic form typeof(EfReadRepository<,>) used — two type params.
        Assert.False(IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.EfReadRepository<,>)));
    }
}

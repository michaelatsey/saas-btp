// GetReferencedAssemblies() checks direct references only (assembly manifest). Transitive refs are not checked.

namespace MicroKit.Persistence.Architecture.Tests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(MicroKit.Persistence.Abstractions.IUnitOfWork).Assembly;

    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly;

    private static readonly Assembly EfCoreAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly;

    private static readonly Assembly PostgreSqlAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlEfCoreBuilderExtensions).Assembly;

    // --- Abstractions assembly ---

    [Fact]
    public void Abstractions_DoesNotReferenceResult()
    {
        // #196 D17: the Result path lives in core (#197 phase 3), never in Abstractions.
        Assert.DoesNotContain(AbstractionsAssembly.GetReferencedAssemblies(), a => a.Name == "MicroKit.Result");
    }

    [Fact]
    public void Core_ReferencesResult()
    {
        Assert.Contains(CoreAssembly.GetReferencedAssemblies(), a => a.Name == "MicroKit.Result");
    }

    [Fact]
    public void Abstractions_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("Microsoft.EntityFrameworkCore"));
    }

    [Fact]
    public void Abstractions_DoesNotReferenceNpgsql()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("Npgsql"));
    }

    [Fact]
    public void Abstractions_DoesNotReferenceSqlServer()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("SqlServer"));
    }

    [Fact]
    public void Abstractions_DoesNotReferencePersistenceCore()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name == "MicroKit.Persistence");
    }

    [Fact]
    public void Abstractions_DoesNotReferencePersistenceEntityFrameworkCore()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("MicroKit.Persistence.EntityFrameworkCore"));
    }

    // --- Core assembly ---

    [Fact]
    public void Core_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("Microsoft.EntityFrameworkCore"));
    }

    [Fact]
    public void Core_DoesNotReferenceNpgsql()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("Npgsql"));
    }

    [Fact]
    public void Core_DoesNotReferenceEntityFrameworkCoreProject()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name == "MicroKit.Persistence.EntityFrameworkCore");
    }

    [Fact]
    public void Core_DoesNotReference_MediatRContracts()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("MediatR.Contracts"));
    }

    // --- EntityFrameworkCore assembly ---

    [Fact]
    public void EntityFrameworkCore_DoesNotReferenceNpgsql()
    {
        var refs = EfCoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("Npgsql"));
    }

    [Fact]
    public void EntityFrameworkCore_DoesNotReferenceSqlServer()
    {
        var refs = EfCoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("SqlServer"));
    }

    [Fact]
    public void EntityFrameworkCore_DoesNotReference_MediatRContracts()
    {
        var refs = EfCoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("MediatR.Contracts"));
    }

    // --- Provider assembly dependency checks ---

    [Fact]
    public void PostgreSql_References_Npgsql()
    {
        var refs = PostgreSqlAssembly.GetReferencedAssemblies();
        Assert.Contains(refs, a => a.Name!.Contains("Npgsql"));
    }
}

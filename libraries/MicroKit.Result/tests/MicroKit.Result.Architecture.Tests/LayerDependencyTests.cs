// GetReferencedAssemblies() checks direct references only (assembly manifest). Transitive refs are not checked.

namespace MicroKit.Result.Architecture.Tests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Result.Result).Assembly;

    private static readonly Assembly AspNetCoreAssembly =
        typeof(MicroKit.Result.AspNetCore.ResultHttpExtensions).Assembly;

    // --- Core assembly: Level 0 — zero MicroKit dependencies ---

    [Fact]
    public void Core_DoesNotReferenceAnyMicroKitPackage()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.StartsWith("MicroKit.", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("EntityFrameworkCore"));
    }

    [Fact]
    public void Core_DoesNotReferenceMediatR()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("MediatR"));
    }

    [Fact]
    public void Core_DoesNotReferenceNpgsql()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("Npgsql"));
    }

    [Fact]
    public void Core_DoesNotReferenceFluentAssertions()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("FluentAssertions"));
    }

    [Fact]
    public void Core_DoesNotReferenceAspNetCore()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("AspNetCore"));
    }

    [Fact]
    public void Core_DoesNotReferenceShouldly()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("Shouldly"));
    }

    [Fact]
    public void Core_DoesNotReferenceNSubstitute()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("NSubstitute"));
    }

    // --- AspNetCore assembly ---

    [Fact]
    public void AspNetCore_References_CoreAssembly()
    {
        var refs = AspNetCoreAssembly.GetReferencedAssemblies();
        Assert.Contains(refs, a => a.Name == "MicroKit.Result");
    }

    [Fact]
    public void AspNetCore_DoesNotReferenceNonResultMicroKitPackage()
    {
        // AspNetCore is only allowed to reference MicroKit.Result — not any other MicroKit module.
        var refs = AspNetCoreAssembly.GetReferencedAssemblies();
        Assert.All(refs
            .Where(a => a.Name!.StartsWith("MicroKit.", StringComparison.Ordinal)), a => Assert.True(a.Name == "MicroKit.Result"));
    }

    [Fact]
    public void AspNetCore_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = AspNetCoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("EntityFrameworkCore"));
    }

    [Fact]
    public void AspNetCore_DoesNotReferenceMediatR()
    {
        var refs = AspNetCoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("MediatR"));
    }

    [Fact]
    public void AspNetCore_DoesNotReferenceFluentAssertions()
    {
        var refs = AspNetCoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, a => a.Name!.Contains("FluentAssertions"));
    }
}

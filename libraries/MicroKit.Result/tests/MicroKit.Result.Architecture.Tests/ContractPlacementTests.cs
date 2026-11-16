namespace MicroKit.Result.Architecture.Tests;

public sealed class ContractPlacementTests
{
    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Result.Result).Assembly;

    private static readonly Assembly AspNetCoreAssembly =
        typeof(MicroKit.Result.AspNetCore.ResultHttpExtensions).Assembly;

    // --- Core types live in the core assembly ---

    [Fact]
    public void Result_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(Result).Assembly);
    }

    [Fact]
    public void ResultOfT_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(Result<>).Assembly);
    }

    [Fact]
    public void Unit_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(Unit).Assembly);
    }

    [Fact]
    public void IError_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(IError).Assembly);
    }

    [Fact]
    public void Error_BaseRecord_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(Error).Assembly);
    }

    [Fact]
    public void ErrorCode_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(ErrorCode).Assembly);
    }

    [Fact]
    public void ErrorCollection_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(ErrorCollection).Assembly);
    }

    [Fact]
    public void ErrorMetadata_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(ErrorMetadata).Assembly);
    }

    [Fact]
    public void ErrorMetadataBuilder_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(ErrorMetadataBuilder).Assembly);
    }

    [Fact]
    public void ValidationError_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(ValidationError).Assembly);
    }

    [Fact]
    public void ValidationResult_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(ValidationResult).Assembly);
    }

    [Fact]
    public void ResultException_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(ResultException).Assembly);
    }

    [Fact]
    public void ResultFactory_IsInCore()
    {
        Assert.Equal(CoreAssembly, typeof(ResultFactory).Assembly);
    }

    // --- Serialization types live in the core assembly, under MicroKit.Result.Serialization ---

    [Fact]
    public void ResultJsonConverterFactory_IsInCore_NotAspNetCore()
    {
        Assert.Equal(CoreAssembly, typeof(MicroKit.Result.Serialization.ResultJsonConverterFactory).Assembly);
        Assert.NotEqual(AspNetCoreAssembly, typeof(MicroKit.Result.Serialization.ResultJsonConverterFactory).Assembly);
    }

    [Fact]
    public void ResultJsonConverter_IsInCore_NotAspNetCore()
    {
        Assert.Equal(CoreAssembly, typeof(MicroKit.Result.Serialization.ResultJsonConverter<>).Assembly);
        Assert.NotEqual(AspNetCoreAssembly, typeof(MicroKit.Result.Serialization.ResultJsonConverter<>).Assembly);
    }

    // --- AspNetCore types live in the AspNetCore assembly, not in core ---

    [Fact]
    public void ResultHttpExtensions_IsInAspNetCore_NotCore()
    {
        Assert.Equal(AspNetCoreAssembly, typeof(MicroKit.Result.AspNetCore.ResultHttpExtensions).Assembly);
        Assert.NotEqual(CoreAssembly, typeof(MicroKit.Result.AspNetCore.ResultHttpExtensions).Assembly);
    }

    [Fact]
    public void ResultProblemDetailsFactory_IsInAspNetCore_NotCore()
    {
        Assert.Equal(AspNetCoreAssembly, typeof(MicroKit.Result.AspNetCore.ResultProblemDetailsFactory).Assembly);
        Assert.NotEqual(CoreAssembly, typeof(MicroKit.Result.AspNetCore.ResultProblemDetailsFactory).Assembly);
    }
}

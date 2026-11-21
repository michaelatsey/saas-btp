namespace MicroKit.Result.Architecture.Tests;

public sealed class SealedClassTests
{
    // IsSealed on open generic type definitions always returns false in the CLR via Type.IsSealed.
    // TypeAttributes.Sealed is the correct way to check sealed for both open generic and non-generic types.
    private static bool IsSealed(Type type) =>
        (type.Attributes & TypeAttributes.Sealed) != 0;

    // --- Concrete error types must be sealed ---

    [Fact]
    public void ErrorCollection_IsSealed()
    {
        Assert.True(IsSealed(typeof(ErrorCollection)));
    }

    [Fact]
    public void ErrorMetadataBuilder_IsSealed()
    {
        Assert.True(IsSealed(typeof(ErrorMetadataBuilder)));
    }

    [Fact]
    public void ValidationError_IsSealed()
    {
        Assert.True(IsSealed(typeof(ValidationError)));
    }

    [Fact]
    public void ValidationResult_IsSealed()
    {
        Assert.True(IsSealed(typeof(ValidationResult)));
    }

    [Fact]
    public void ResultException_IsSealed()
    {
        Assert.True(IsSealed(typeof(ResultException)));
    }

    // --- Serialization types must be sealed ---

    [Fact]
    public void ResultJsonConverterFactory_IsSealed()
    {
        Assert.True(IsSealed(typeof(MicroKit.Result.Serialization.ResultJsonConverterFactory)));
    }

    [Fact]
    public void ResultJsonConverter_IsSealed()
    {
        // Open generic — TypeAttributes.Sealed is required; Type.IsSealed returns false for open generics.
        Assert.True(IsSealed(typeof(MicroKit.Result.Serialization.ResultJsonConverter<>)));
    }

    // --- AspNetCore extension types ---

    [Fact]
    public void ResultProblemDetailsFactory_IsStatic()
    {
        // Static classes are both abstract and sealed at the IL level.
        var t = typeof(MicroKit.Result.AspNetCore.ResultProblemDetailsFactory);
        Assert.True(IsSealed(t));
        Assert.NotEqual((TypeAttributes)0, (t.Attributes & TypeAttributes.Abstract));
    }

    // --- Error base record is intentionally NOT sealed: consumers must subclass it ---

    [Fact]
    public void Error_BaseRecord_IsNotSealed()
    {
        // abstract record Error — designed as an open base for domain error hierarchies.
        Assert.False(IsSealed(typeof(Error)));
    }
}

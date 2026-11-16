using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Xunit;

namespace MicroKit.Domain.Tests.ValueObjects.Common;

public sealed class FullNameTests
{
    [Fact]
    public void Constructor_PaddedComponents_TrimsBoth()
    {
        var name = new FullName("  Ada ", " Lovelace  ");

        Assert.Equal("Ada", name.FirstName);
        Assert.Equal("Lovelace", name.LastName);
        Assert.Equal("Ada Lovelace", name.DisplayName);
        Assert.Equal("Ada Lovelace", name.ToString());
    }

    [Theory]
    [InlineData(null, "Lovelace", "FirstName")]
    [InlineData("  ", "Lovelace", "FirstName")]
    [InlineData("Ada", "", "LastName")]
    [InlineData("Ada", null, "LastName")]
    public void Constructor_EmptyComponent_ThrowsDomainExceptionNamingIt(string? first, string? last, string component)
    {
        Assert.Equal($"{component} cannot be null or empty.", Assert.Throws<DomainException>(() => new FullName(first!, last!))
            .Message);
    }

    [Fact]
    public void Equality_IsStructural_AfterTrimming()
    {
        Assert.Equal(new FullName(" Ada", "Lovelace "), FullName.Create("Ada", "Lovelace"));
        Assert.NotEqual(new FullName("Ada", "Byron"), FullName.Create("Ada", "Lovelace"));
    }
}

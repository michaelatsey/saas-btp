using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Xunit;

namespace MicroKit.Domain.Tests.ValueObjects.Common;

public sealed class AddressTests
{
    [Fact]
    public void Constructor_PaddedComponents_TrimsEach()
    {
        var address = new Address("  10 rue de la Paix ", " Paris ", " 75002 ", " FR ");

        Assert.Equal("10 rue de la Paix", address.Street);
        Assert.Equal("Paris", address.City);
        Assert.Equal("75002", address.PostalCode);
        Assert.Equal("FR", address.Country);
    }

    [Theory]
    [InlineData(null, "Paris", "75002", "FR", "Street")]
    [InlineData("10 rue de la Paix", " ", "75002", "FR", "City")]
    [InlineData("10 rue de la Paix", "Paris", "", "FR", "PostalCode")]
    [InlineData("10 rue de la Paix", "Paris", "75002", "\t", "Country")]
    public void Constructor_EmptyComponent_ThrowsDomainExceptionNamingIt(
        string? street, string? city, string? postalCode, string? country, string component)
    {
        Assert.Equal($"Address {component} cannot be null or empty.", Assert.Throws<DomainException>(() => new Address(street!, city!, postalCode!, country!))
            .Message);
    }

    [Fact]
    public void Equality_IsStructural_AfterTrimming()
    {
        Assert.Equal(new Address(" 10 rue de la Paix", "Paris ", "75002", "FR"), Address.Create("10 rue de la Paix", "Paris", "75002", "FR"));
    }

    [Fact]
    public void ToString_ValidAddress_JoinsTheComponents()
    {
        Assert.Equal("10 rue de la Paix, Paris, 75002, FR", new Address("10 rue de la Paix", "Paris", "75002", "FR").ToString());
    }
}

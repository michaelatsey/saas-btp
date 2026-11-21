using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Xunit;

namespace MicroKit.Domain.Tests.ValueObjects.Common;

public sealed class CurrencyTests
{
    [Theory]
    [InlineData("XOF", 0)]
    [InlineData("JPY", 0)]
    [InlineData("EUR", 2)]
    [InlineData("USD", 2)]
    [InlineData("TND", 3)]
    [InlineData("KWD", 3)]
    [InlineData("CLF", 4)]
    public void FromCode_KnownCode_CarriesItsMinorUnit(string code, int minorUnit)
    {
        var currency = Currency.FromCode(code);

        Assert.Equal(code, currency.Code);
        Assert.Equal(minorUnit, currency.MinorUnit);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData(" EUR ")]
    public void FromCode_NormalizesCaseAndWhiteSpace(string code)
    {
        Assert.Equal(Currency.FromCode("EUR"), Currency.FromCode(code));
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("XYZ")]
    [InlineData("US")]
    [InlineData("USDA")]
    [InlineData("U1D")]
    [InlineData("HRK")] // withdrawn
    [InlineData("XAU")] // minor unit N.A.
    [InlineData("XXX")] // minor unit N.A.
    public void FromCode_UnknownCode_ThrowsDomainException(string code)
    {
        Assert.Throws<DomainException>(() => Currency.FromCode(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void FromCode_Empty_ThrowsDomainException(string? code)
    {
        var ex = Assert.Throws<DomainException>(() => Currency.FromCode(code!));
        Assert.Equal("Currency code cannot be null or empty.", ex.Message);
    }

    [Fact]
    public void ToString_ReturnsTheCode()
    {
        Assert.Equal("TND", Currency.FromCode("TND").ToString());
        Assert.Equal(string.Empty, default(Currency).ToString());
    }
}

using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Xunit;

namespace MicroKit.Domain.Tests.ValueObjects.Common;

public sealed class PercentageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(25.5)]
    public void Constructor_ValidValue_ShouldCreateInstance(decimal value)
    {
        // Act
        var percentage = new Percentage(value);

        // Assert
        Assert.Equal(value, percentage.Value);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(-50)]
    [InlineData(100.1)]
    [InlineData(150)]
    public void Constructor_InvalidValue_ShouldThrowDomainException(decimal value)
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => new Percentage(value));
        Assert.Equal("Percentage value must be between 0 and 100 inclusive.", ex.Message);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1)]
    [InlineData(25, 0.25)]
    public void AsFraction_ShouldReturnCorrectFraction(decimal value, decimal expectedFraction)
    {
        // Arrange
        var percentage = new Percentage(value);

        // Act & Assert
        Assert.Equal(expectedFraction, percentage.AsFraction);
    }

    [Theory]
    [InlineData(100, 50, 50)]
    [InlineData(200, 25, 50)]
    [InlineData(0, 50, 0)]
    public void Of_ShouldCalculateCorrectAmount(decimal amount, decimal percentageValue, decimal expected)
    {
        // Arrange
        var percentage = new Percentage(percentageValue);

        // Act
        var result = percentage.Of(amount);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Add_ValidSum_ShouldReturnCorrectPercentage()
    {
        // Arrange
        var percentage1 = new Percentage(30);
        var percentage2 = new Percentage(20);

        // Act
        var result = percentage1.Add(percentage2);

        // Assert
        Assert.Equal(50, result.Value);
    }

    [Fact]
    public void Add_SumExceeds100_ShouldThrowDomainException()
    {
        // Arrange
        var percentage1 = new Percentage(60);
        var percentage2 = new Percentage(50);

        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => percentage1.Add(percentage2));
        Assert.Equal("Percentage value must be between 0 and 100 inclusive.", ex.Message);
    }

    [Fact]
    public void Subtract_ValidDifference_ShouldReturnCorrectPercentage()
    {
        // Arrange
        var percentage1 = new Percentage(70);
        var percentage2 = new Percentage(20);

        // Act
        var result = percentage1.Subtract(percentage2);

        // Assert
        Assert.Equal(50, result.Value);
    }

    [Fact]
    public void Subtract_DifferenceBelowZero_ShouldThrowDomainException()
    {
        // Arrange
        var percentage1 = new Percentage(30);
        var percentage2 = new Percentage(50);

        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => percentage1.Subtract(percentage2));
        Assert.Equal("Percentage value must be between 0 and 100 inclusive.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(0.25)]
    public void FromFraction_ValidFraction_ShouldCreateCorrectPercentage(decimal fraction)
    {
        // Act
        var percentage = Percentage.FromFraction(fraction);

        // Assert
        Assert.Equal(fraction, percentage.AsFraction);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(2)]
    public void FromFraction_InvalidFraction_ShouldThrowDomainException(decimal fraction)
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => Percentage.FromFraction(fraction));
        Assert.Equal("Fraction value must be between 0.0 and 1.0 inclusive.", ex.Message);
    }

    [Fact]
    public void Zero_ShouldReturnZeroPercentage()
    {
        // Act
        var percentage = Percentage.Zero;

        // Assert
        Assert.Equal(0, percentage.Value);
    }

    [Fact]
    public void OneHundred_ShouldReturnOneHundredPercentage()
    {
        // Act
        var percentage = Percentage.OneHundred;

        // Assert
        Assert.Equal(100, percentage.Value);
    }

    [Fact]
    public void OperatorAdd_ShouldWorkLikeAddMethod()
    {
        // Arrange
        var percentage1 = new Percentage(30);
        var percentage2 = new Percentage(20);

        // Act
        var result = percentage1 + percentage2;

        // Assert
        Assert.Equal(percentage1.Add(percentage2), result);
    }

    [Fact]
    public void OperatorSubtract_ShouldWorkLikeSubtractMethod()
    {
        // Arrange
        var percentage1 = new Percentage(70);
        var percentage2 = new Percentage(20);

        // Act
        var result = percentage1 - percentage2;

        // Assert
        Assert.Equal(percentage1.Subtract(percentage2), result);
    }

    [Fact]
    public void ImplicitConversionFromDecimal_ShouldWork()
    {
        // Act
        Percentage percentage = 50m;

        // Assert
        Assert.Equal(50, percentage.Value);
    }

    [Fact]
    public void ImplicitConversionToDecimal_ShouldWork()
    {
        // Arrange
        var percentage = new Percentage(50);

        // Act
        decimal value = percentage;

        // Assert
        Assert.Equal(50, value);
    }

    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var percentage = new Percentage(75.25m);

        // Act
        var result = percentage.ToString();

        // Assert
        Assert.Equal("75.25%", result);
    }

    [Theory]
    [InlineData(50, 50, true)]
    [InlineData(50, 60, false)]
    [InlineData(0, 0, true)]
    [InlineData(100, 100, true)]
    public void Equality_ShouldWorkCorrectly(decimal value1, decimal value2, bool expected)
    {
        // Arrange
        var percentage1 = new Percentage(value1);
        var percentage2 = new Percentage(value2);

        // Act & Assert
        Assert.Equal(expected, percentage1.Equals(percentage2));
        Assert.Equal(expected, (percentage1 == percentage2));
        Assert.Equal(!expected, (percentage1 != percentage2));
    }

    [Fact]
    public void GetHashCode_EqualObjects_ShouldHaveSameHashCode()
    {
        // Arrange
        var percentage1 = new Percentage(50);
        var percentage2 = new Percentage(50);

        // Act & Assert
        Assert.Equal(percentage2.GetHashCode(), percentage1.GetHashCode());
    }

}
